import { useEffect, useRef, useState } from 'react';
import {
  createRagTaskHubClient,
  type RagTaskHubConnectionState,
  type RagTaskHubHandlers
} from '@/api/ragTaskHubClient';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';

type UseRagTaskHubResult = {
  connectionState: RagTaskHubConnectionState;
};

export function useRagTaskHub(apiBase: string, handlers: RagTaskHubHandlers): UseRagTaskHubResult {
  const handlersRef = useRef(handlers);
  const [connectionState, setConnectionState] = useState<RagTaskHubConnectionState>('Connecting');

  handlersRef.current = handlers;

  useEffect(() => {
    let isMounted = true;

    const updateConnectionState = (state: RagTaskHubConnectionState) => {
      if (isMounted) {
        setConnectionState(state);
        handlersRef.current.onConnectionStateChanged?.(state);
      }
    };

    setConnectionState('Connecting');

    const client = createRagTaskHubClient(apiBase);

    void client.start({
      onTaskStatusUpdated(update: TaskStatusUpdate) {
        if (isMounted) {
          handlersRef.current.onTaskStatusUpdated?.(update);
        }
      },
      onDataCleared() {
        if (isMounted) {
          handlersRef.current.onDataCleared?.();
        }
      },
      onConnectionStateChanged(state) {
        updateConnectionState(state);
      }
    }).catch(() => updateConnectionState('ServerNotStarted'));

    return () => {
      isMounted = false;
      void client.stop().catch(() => undefined);
    };
  }, [apiBase]);

  return { connectionState };
}
