import { Component, lazy, Suspense, useCallback, useMemo, useRef, useState, type ComponentType, type ReactNode } from 'react';
import { getApiBase } from '@/api/http';
import { AppLayout } from './AppLayout';
import { type AppRoute, resolveRoute } from './router';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';
import { DocumentPreviewPage } from '@/features/document-preview/DocumentPreviewPage';
import { CacheManagementWorkbench } from '@/features/cache-management/CacheManagementWorkbench';
import { RagChatWorkbench } from '@/features/rag-chat/RagChatWorkbench';
import { SystemStatusWorkbench } from '@/features/system-status/SystemStatusWorkbench';
import { useRagTaskHub } from '@/shared/hooks/useRagTaskHub';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusPill } from '@/shared/components/StatusPill';
import { notifySubscribers } from './subscribers';

type GraphWorkbenchComponent = ComponentType<{ apiBase: string }>;
type GraphWorkbenchModule = { default: GraphWorkbenchComponent };
type GraphWorkbenchLoader = () => Promise<GraphWorkbenchModule>;

const loadGraphWorkbench: GraphWorkbenchLoader = () =>
  import('@/features/graph-workbench/GraphWorkbench').then((module) => ({ default: module.GraphWorkbench }));

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
      ) : route.id === 'document-preview' ? (
        <DocumentPreviewPage apiBase={apiBase} documentId={getPreviewDocumentId(window.location.pathname)} />
      ) : route.id === 'rag-chat' ? (
        <RagChatWorkbench apiBase={apiBase} />
      ) : route.id === 'graph' ? (
        <GraphRoute apiBase={apiBase} />
      ) : route.id === 'system-status' ? (
        <SystemStatusWorkbench apiBase={apiBase} />
      ) : route.id === 'cache-management' ? (
        <CacheManagementWorkbench apiBase={apiBase} />
      ) : (
        <PlaceholderRoute route={route} />
      )}
    </AppLayout>
  );
}

export function getPreviewDocumentId(pathname: string): number | undefined {
  const segments = pathname.split('/').filter(Boolean);
  const routeIndex = segments.findIndex((segment) => segment === 'document-preview');
  const idText = routeIndex >= 0 ? segments.at(routeIndex + 1) : undefined;
  const id = Number(idText);
  return Number.isInteger(id) && id > 0 ? id : undefined;
}

type GraphRouteProps = {
  apiBase: string;
  loadWorkbench?: GraphWorkbenchLoader;
};

export function GraphRoute({ apiBase, loadWorkbench = loadGraphWorkbench }: GraphRouteProps) {
  const [routeVersion, setRouteVersion] = useState(0);
  const GraphWorkbench = useMemo(() => lazy(loadWorkbench), [loadWorkbench, routeVersion]);

  return (
    <GraphRouteErrorBoundary key={routeVersion} onRetry={() => setRouteVersion((version) => version + 1)}>
      <Suspense fallback={<GraphRouteLoadingPanel />}>
        <GraphWorkbench apiBase={apiBase} />
      </Suspense>
    </GraphRouteErrorBoundary>
  );
}

function GraphRouteLoadingPanel() {
  return (
    <section className="lrn-panel app-placeholder" aria-label="Knowledge Graph loading">
      <PageHeader
        title="Loading Knowledge Graph"
        description="Preparing the graph workbench."
        meta={<StatusPill tone="accent">Loading</StatusPill>}
      />
    </section>
  );
}

type GraphRouteErrorBoundaryProps = {
  children: ReactNode;
  onRetry: () => void;
};

type GraphRouteErrorBoundaryState = {
  error: Error | null;
};

class GraphRouteErrorBoundary extends Component<GraphRouteErrorBoundaryProps, GraphRouteErrorBoundaryState> {
  state: GraphRouteErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): GraphRouteErrorBoundaryState {
    return { error };
  }

  render() {
    if (this.state.error) {
      return (
        <section className="lrn-panel app-placeholder" aria-label="Knowledge Graph failed">
          <PageHeader
            title="Knowledge Graph failed to load"
            description="The graph workbench could not be loaded. Retry the route or refresh the application."
            meta={<StatusPill tone="warning">Unavailable</StatusPill>}
          />
          <button className="lrn-button lrn-button--secondary" type="button" onClick={this.props.onRetry}>
            Retry graph route
          </button>
        </section>
      );
    }

    return this.props.children;
  }
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
