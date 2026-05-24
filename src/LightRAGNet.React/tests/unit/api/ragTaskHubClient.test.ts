import { describe, expect, it, vi } from 'vitest';
import { createRagTaskHubClient, type RagTaskHubConnection } from '@/api/ragTaskHubClient';

function createConnection(overrides?: Partial<RagTaskHubConnection>): RagTaskHubConnection {
  return {
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
}

describe('ragTaskHubClient', () => {
  it('creates the SignalR hub connection with the rag task hub url', () => {
    const connection = createConnection();
    const factory = vi.fn(() => connection);

    createRagTaskHubClient('http://localhost:5261/', factory);

    expect(factory).toHaveBeenCalledWith('http://localhost:5261/hubs/ragtask');
  });

  it('registers task status and data cleared event handlers', () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onTaskStatusUpdated = vi.fn();
    const onDataCleared = vi.fn();

    client.configure({ onTaskStatusUpdated, onDataCleared });

    expect(connection.on).toHaveBeenCalledWith('TaskStatusUpdated', onTaskStatusUpdated);
    expect(connection.on).toHaveBeenCalledWith('DataCleared', onDataCleared);
  });

  it('starts and stops the hub connection', async () => {
    const connection = createConnection();
    const client = createRagTaskHubClient('http://localhost:5261', () => connection);
    const onConnectionStateChanged = vi.fn();

    await client.start({ onConnectionStateChanged });
    await client.stop();

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(onConnectionStateChanged).toHaveBeenCalledWith('Connected');
    expect(connection.stop).toHaveBeenCalledTimes(1);
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
});
