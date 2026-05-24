import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRagTaskHubClient, type RagTaskHubClient, type RagTaskHubHandlers } from '@/api/ragTaskHubClient';
import { useRagTaskHub } from '@/shared/hooks/useRagTaskHub';

vi.mock('@/api/ragTaskHubClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/ragTaskHubClient')>();

  return {
    ...actual,
    createRagTaskHubClient: vi.fn()
  };
});

const createRagTaskHubClientMock = vi.mocked(createRagTaskHubClient);

type HookProps = {
  handlers: RagTaskHubHandlers;
};

function createClient(): RagTaskHubClient & { capturedHandlers?: RagTaskHubHandlers } {
  const client: RagTaskHubClient & { capturedHandlers?: RagTaskHubHandlers } = {
    configure: vi.fn(),
    start: vi.fn(async (handlers?: RagTaskHubHandlers) => {
      client.capturedHandlers = handlers;
    }),
    stop: vi.fn().mockRejectedValue(new Error('Stop failed'))
  };

  return client;
}

describe('useRagTaskHub', () => {
  beforeEach(() => {
    createRagTaskHubClientMock.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('does not reconnect when handler identity changes across renders', async () => {
    const client = createClient();
    createRagTaskHubClientMock.mockReturnValue(client);

    const { rerender, unmount } = renderHook(
      ({ handlers }) => useRagTaskHub('http://localhost:5261', handlers),
      { initialProps: { handlers: { onTaskStatusUpdated: vi.fn() } } as HookProps }
    );

    await act(async () => {
      await Promise.resolve();
    });

    rerender({ handlers: { onTaskStatusUpdated: vi.fn(), onDataCleared: vi.fn() } });

    expect(createRagTaskHubClientMock).toHaveBeenCalledTimes(1);
    expect(client.start).toHaveBeenCalledTimes(1);
    expect(client.stop).not.toHaveBeenCalled();

    unmount();
  });

  it('reports connecting before the hub client resolves startup', async () => {
    const client = createClient();
    createRagTaskHubClientMock.mockReturnValue(client);

    const { result, unmount } = renderHook(() => useRagTaskHub('http://localhost:5261', {}));

    expect(result.current.connectionState).toBe('Connecting');

    await act(async () => {
      await Promise.resolve();
    });

    unmount();
  });

  it('reports connected when hub startup succeeds', async () => {
    const client = createClient();
    client.start = vi.fn(async (handlers?: RagTaskHubHandlers) => {
      client.capturedHandlers = handlers;
      handlers?.onConnectionStateChanged?.('Connected');
    });
    createRagTaskHubClientMock.mockReturnValue(client);

    const { result, unmount } = renderHook(() => useRagTaskHub('http://localhost:5261', {}));

    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.connectionState).toBe('Connected');

    unmount();
  });

  it('reports server not started when hub startup fails', async () => {
    const client = createClient();
    client.start = vi.fn(async () => {
      throw new Error('Server unavailable');
    });
    createRagTaskHubClientMock.mockReturnValue(client);

    const { result, unmount } = renderHook(() => useRagTaskHub('http://localhost:5261', {}));

    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.connectionState).toBe('ServerNotStarted');

    unmount();
  });

  it('does not call external handlers after unmount', async () => {
    const client = createClient();
    createRagTaskHubClientMock.mockReturnValue(client);
    const onTaskStatusUpdated = vi.fn();
    const onDataCleared = vi.fn();
    const onConnectionStateChanged = vi.fn();

    const { unmount } = renderHook(() =>
      useRagTaskHub('http://localhost:5261', {
        onTaskStatusUpdated,
        onDataCleared,
        onConnectionStateChanged
      })
    );

    await act(async () => {
      await Promise.resolve();
    });

    unmount();

    await act(async () => {
      client.capturedHandlers?.onTaskStatusUpdated?.({ documentId: 7, status: 'Processing', progress: 25 });
      client.capturedHandlers?.onDataCleared?.();
      client.capturedHandlers?.onConnectionStateChanged?.('Connected');
      await Promise.resolve();
    });

    expect(onTaskStatusUpdated).not.toHaveBeenCalled();
    expect(onDataCleared).not.toHaveBeenCalled();
    expect(onConnectionStateChanged).not.toHaveBeenCalled();
    expect(client.stop).toHaveBeenCalledTimes(1);
  });
});
