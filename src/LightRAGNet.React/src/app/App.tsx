import { useCallback, useRef } from 'react';
import { getApiBase } from '@/api/http';
import { AppLayout } from './AppLayout';
import { type AppRoute, resolveRoute } from './router';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';
import { RagChatWorkbench } from '@/features/rag-chat/RagChatWorkbench';
import { useRagTaskHub } from '@/shared/hooks/useRagTaskHub';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusPill } from '@/shared/components/StatusPill';
import { notifySubscribers } from './subscribers';

export function App() {
  const route = resolveRoute();
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

  const { connectionState } = useRagTaskHub(apiBase, {
    onTaskStatusUpdated(update) {
      notifySubscribers(taskUpdateSubscribersRef.current, update);
    },
    onDataCleared() {
      notifySubscribers(dataClearedSubscribersRef.current);
    }
  });

  return (
    <AppLayout currentPath={window.location.pathname} connectionStatus={connectionState}>
      {route.id === 'upload' ? (
        <UploadDocumentPage />
      ) : route.id === 'documents' ? (
        <DocumentsRoute
          apiBase={apiBase}
          subscribeToTaskUpdates={subscribeToTaskUpdates}
          subscribeToDataCleared={subscribeToDataCleared}
        />
      ) : route.id === 'rag-chat' ? (
        <RagChatWorkbench apiBase={apiBase} />
      ) : (
        <PlaceholderRoute route={route} />
      )}
    </AppLayout>
  );
}

type DocumentsRouteProps = {
  apiBase: string;
  subscribeToTaskUpdates: (handler: (update: TaskStatusUpdate) => void) => () => void;
  subscribeToDataCleared: (handler: () => void) => () => void;
};

function DocumentsRoute({
  apiBase,
  subscribeToTaskUpdates,
  subscribeToDataCleared
}: DocumentsRouteProps) {
  return (
    <DocumentsPage
      apiBase={apiBase}
      subscribeToTaskUpdates={subscribeToTaskUpdates}
      subscribeToDataCleared={subscribeToDataCleared}
    />
  );
}

function PlaceholderRoute({ route }: { route: AppRoute }) {
  return (
    <section className="lrn-panel app-placeholder" aria-label={route.title}>
      <PageHeader
        title={route.title}
        description={route.description}
        meta={<StatusPill tone="accent">Pending migration</StatusPill>}
      />
      <p>
        This standalone React route is registered in the shell. Its production workflow will be migrated in a later
        task.
      </p>
    </section>
  );
}
