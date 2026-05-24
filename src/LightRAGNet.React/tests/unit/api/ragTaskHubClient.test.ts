import { describe, expect, it, vi } from 'vitest';
import { createRagTaskHubClient, type RagTaskHubConnection } from '@/api/ragTaskHubClient';

function createConnection(overrides?: Partial<RagTaskHubConnection>): RagTaskHubConnection {
  return {
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    invoke: vi.fn().mockResolvedValue(undefined),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
}

type EventRegistration = [eventName: string, callback: (...args: unknown[]) => void];

function getEventRegistrations(connection: RagTaskHubConnection): EventRegistration[] {
  return vi.mocked(connection.on).mock.calls as EventRegistration[];
}

describe('ragTaskHubClient', () => {
  it('creates the SignalR hub connection with the rag task hub url', () => {
    const connection = createConnection();
    const factory = vi.fn(() => connection);

    createRagTaskHubClient('http://localhost:5261/', factory);

    expect(factory).toHaveBeenCalledWith('http://localhost:5261/hubs/ragtask');
  });

  it('registers task status and data cleared event dispatchers', () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onTaskStatusUpdated = vi.fn();
    const onDataCleared = vi.fn();

    client.configure({ onTaskStatusUpdated, onDataCleared });

    expect(connection.on).toHaveBeenCalledWith('TaskStatusUpdated', expect.any(Function));
    expect(connection.on).toHaveBeenCalledWith('DataCleared', expect.any(Function));
  });

  it('joins all tasks group after start before reporting connected', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Connected');
    expect(vi.mocked(connection.invoke).mock.invocationCallOrder[0])
      .toBeLessThan(onConnectionStateChanged.mock.invocationCallOrder[0]);
  });

  it('does not start or join again after already connected', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    await client.start();

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('ServerNotStarted');
  });

  it('reuses the same start operation while connecting', async () => {
    let resolveStart: (() => void) | undefined;
    const startPromise = new Promise<void>((resolve) => {
      resolveStart = resolve;
    });
    const connection = createConnection({
      start: vi.fn(() => startPromise)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    const firstStart = client.start({ onConnectionStateChanged });
    const secondStart = client.start();

    expect(connection.start).toHaveBeenCalledTimes(1);
    resolveStart?.();
    await Promise.all([firstStart, secondStart]);

    expect(connection.invoke).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(onConnectionStateChanged).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Connected');
  });

  it('rejoins all tasks group after reconnected before reporting connected', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    const onReconnected = vi.mocked(connection.onreconnected).mock.calls[0]?.[0];
    onReconnected?.();
    await Promise.resolve();
    await Promise.resolve();

    expect(connection.invoke).toHaveBeenCalledTimes(2);
    expect(connection.invoke).toHaveBeenLastCalledWith('JoinAllTasksGroup');
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Connected');
  });

  it('does not register task event handlers more than once when configured repeatedly', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);

    client.configure({ onTaskStatusUpdated: vi.fn(), onDataCleared: vi.fn() });
    client.configure({ onTaskStatusUpdated: vi.fn(), onDataCleared: vi.fn() });
    await client.start({ onTaskStatusUpdated: vi.fn(), onDataCleared: vi.fn() });

    const taskStatusRegistrations = getEventRegistrations(connection).filter(([eventName]) => eventName === 'TaskStatusUpdated');
    const dataClearedRegistrations = getEventRegistrations(connection).filter(([eventName]) => eventName === 'DataCleared');

    expect(taskStatusRegistrations).toHaveLength(1);
    expect(dataClearedRegistrations).toHaveLength(1);
  });

  it('dispatches task events to the latest configured handlers', () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const staleTaskStatusUpdated = vi.fn();
    const latestTaskStatusUpdated = vi.fn();
    const staleDataCleared = vi.fn();
    const latestDataCleared = vi.fn();

    client.configure({ onTaskStatusUpdated: staleTaskStatusUpdated, onDataCleared: staleDataCleared });
    client.configure({ onTaskStatusUpdated: latestTaskStatusUpdated, onDataCleared: latestDataCleared });

    const taskStatusDispatcher = getEventRegistrations(connection).find(([eventName]) => eventName === 'TaskStatusUpdated')?.[1];
    const dataClearedDispatcher = getEventRegistrations(connection).find(([eventName]) => eventName === 'DataCleared')?.[1];
    const update = { documentId: 7, status: 'Processing', progress: 50 };

    taskStatusDispatcher?.(update);
    dataClearedDispatcher?.();

    expect(staleTaskStatusUpdated).not.toHaveBeenCalled();
    expect(latestTaskStatusUpdated).toHaveBeenCalledWith(update);
    expect(staleDataCleared).not.toHaveBeenCalled();
    expect(latestDataCleared).toHaveBeenCalledTimes(1);
  });

  it('leaves all tasks group and stops the hub connection', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    await client.stop();

    expect(connection.invoke).toHaveBeenCalledWith('LeaveAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).toHaveBeenLastCalledWith('Disconnected');
  });

  it('continues stopping when leaving all tasks group fails', async () => {
    const connection = createConnection({
      invoke: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new Error('Leave failed'))
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);

    await client.start();
    await expect(client.stop()).resolves.toBeUndefined();

    expect(connection.invoke).toHaveBeenLastCalledWith('LeaveAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(1);
  });

  it('rejects stop failures without reporting disconnected', async () => {
    const connection = createConnection({
      stop: vi.fn().mockRejectedValue(new Error('Stop failed'))
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    await expect(client.stop()).rejects.toThrow('Stop failed');

    expect(connection.invoke).toHaveBeenLastCalledWith('LeaveAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Disconnected');
  });

  it('reports server not started when the hub connection fails to start', async () => {
    const connection = createConnection({
      start: vi.fn().mockRejectedValue(new Error('Connection refused'))
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });

    expect(onConnectionStateChanged).toHaveBeenCalledWith('ServerNotStarted');
  });

  it('reports disconnected and stops cleanup when joining all tasks group fails', async () => {
    const connection = createConnection({
      invoke: vi.fn().mockRejectedValue(new Error('Join failed'))
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });

    expect(connection.invoke).toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Disconnected');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('ServerNotStarted');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
  });
});
