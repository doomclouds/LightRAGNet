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

  it('does not join or report connected when stopped before pending start resolves', async () => {
    let resolveStart: (() => void) | undefined;
    const pendingStart = new Promise<void>((resolve) => {
      resolveStart = resolve;
    });
    const connection = createConnection({
      start: vi.fn(() => pendingStart)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    const startOperation = client.start({ onConnectionStateChanged });
    await client.stop();

    resolveStart?.();
    await startOperation;

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('LeaveAllTasksGroup');
    expect(connection.invoke).not.toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(2);
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Disconnected');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
  });

  it('does not report server not started when stopped before pending start rejects', async () => {
    let rejectStart: ((error: Error) => void) | undefined;
    const pendingStart = new Promise<void>((_, reject) => {
      rejectStart = reject;
    });
    const connection = createConnection({
      start: vi.fn(() => pendingStart)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    const startOperation = client.start({ onConnectionStateChanged });
    await client.stop();

    rejectStart?.(new Error('Connection aborted'));
    await startOperation;

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('LeaveAllTasksGroup');
    expect(connection.invoke).not.toHaveBeenCalledWith('JoinAllTasksGroup');
    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Disconnected');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('ServerNotStarted');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
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

  it('does not report connected when close wins a pending reconnected join', async () => {
    let resolveReconnectJoin: (() => void) | undefined;
    const pendingReconnectJoin = new Promise<void>((resolve) => {
      resolveReconnectJoin = resolve;
    });
    const connection = createConnection({
      invoke: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockReturnValueOnce(pendingReconnectJoin)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    const onReconnected = vi.mocked(connection.onreconnected).mock.calls[0]?.[0];
    const onClose = vi.mocked(connection.onclose).mock.calls[0]?.[0];
    onReconnected?.();
    onClose?.();

    resolveReconnectJoin?.();
    await pendingReconnectJoin;
    await Promise.resolve();

    expect(onConnectionStateChanged).toHaveBeenLastCalledWith('Disconnected');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
  });

  it('does not report connected when stop wins a pending reconnected join', async () => {
    let resolveReconnectJoin: (() => void) | undefined;
    const pendingReconnectJoin = new Promise<void>((resolve) => {
      resolveReconnectJoin = resolve;
    });
    const connection = createConnection({
      invoke: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockReturnValueOnce(pendingReconnectJoin)
        .mockResolvedValue(undefined)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    const onReconnected = vi.mocked(connection.onreconnected).mock.calls[0]?.[0];
    onReconnected?.();
    await client.stop();

    resolveReconnectJoin?.();
    await pendingReconnectJoin;
    await Promise.resolve();

    expect(connection.invoke).toHaveBeenCalledWith('LeaveAllTasksGroup');
    expect(onConnectionStateChanged).toHaveBeenLastCalledWith('Disconnected');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
  });

  it('does not report connected when reconnecting wins a pending reconnected join', async () => {
    let resolveReconnectJoin: (() => void) | undefined;
    const pendingReconnectJoin = new Promise<void>((resolve) => {
      resolveReconnectJoin = resolve;
    });
    const connection = createConnection({
      invoke: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockReturnValueOnce(pendingReconnectJoin)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    const onReconnected = vi.mocked(connection.onreconnected).mock.calls[0]?.[0];
    const onReconnecting = vi.mocked(connection.onreconnecting).mock.calls[0]?.[0];
    onReconnected?.();
    onReconnecting?.();

    resolveReconnectJoin?.();
    await pendingReconnectJoin;
    await Promise.resolve();

    expect(onConnectionStateChanged).toHaveBeenLastCalledWith('Reconnecting');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Connected');
  });

  it('does not report disconnected when reconnecting wins a rejected reconnected join', async () => {
    let rejectReconnectJoin: ((error: Error) => void) | undefined;
    const pendingReconnectJoin = new Promise<void>((_, reject) => {
      rejectReconnectJoin = reject;
    });
    const connection = createConnection({
      invoke: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockReturnValueOnce(pendingReconnectJoin)
    });
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    onConnectionStateChanged.mockClear();

    const onReconnected = vi.mocked(connection.onreconnected).mock.calls[0]?.[0];
    const onReconnecting = vi.mocked(connection.onreconnecting).mock.calls[0]?.[0];
    onReconnected?.();
    onReconnecting?.();

    rejectReconnectJoin?.(new Error('Join lost'));
    await expect(pendingReconnectJoin).rejects.toThrow('Join lost');
    await Promise.resolve();

    expect(onConnectionStateChanged).toHaveBeenLastCalledWith('Reconnecting');
    expect(onConnectionStateChanged).not.toHaveBeenCalledWith('Disconnected');
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
