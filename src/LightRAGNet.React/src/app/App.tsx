import { useCallback, useRef } from 'react';
import { getApiBase } from '@/api/http';
import { AppLayout } from './AppLayout';
import { resolveRoute } from './router';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';
import { useRagTaskHub } from '@/shared/hooks/useRagTaskHub';

export function App() {
  const route = resolveRoute();

  return (
    <AppLayout>
      {route.path === '/documents/upload' ? (
        <UploadDocumentPage />
      ) : (
        <DocumentsRoute />
      )}
    </AppLayout>
  );
}

function DocumentsRoute() {
  const apiBase = getApiBase();
  const taskUpdateSubscribersRef = useRef(new Set<(update: TaskStatusUpdate) => void>());
  const dataClearedSubscribersRef = useRef(new Set<() => void>());

  const subscribeToTaskUpdates = useCallback((handler: (update: TaskStatusUpdate) => void) => {
    taskUpdateSubscribersRef.current.add(handler);

    return () => {
      taskUpdateSubscribersRef.current.delete(handler);
    };
  }, []);

  const subscribeToDataCleared = useCallback((handler: () => void) => {
    dataClearedSubscribersRef.current.add(handler);

    return () => {
      dataClearedSubscribersRef.current.delete(handler);
    };
  }, []);

  useRagTaskHub(apiBase, {
    onTaskStatusUpdated(update) {
      taskUpdateSubscribersRef.current.forEach((handler) => handler(update));
    },
    onDataCleared() {
      dataClearedSubscribersRef.current.forEach((handler) => handler());
    }
  });

  return (
    <DocumentsPage
      apiBase={apiBase}
      subscribeToTaskUpdates={subscribeToTaskUpdates}
      subscribeToDataCleared={subscribeToDataCleared}
    />
  );
}
