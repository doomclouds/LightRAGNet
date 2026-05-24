export type AppRoute = {
  id: AppRouteId;
  path: string;
  title: string;
  description: string;
};

export type AppRouteId =
  | 'rag-chat'
  | 'documents'
  | 'upload'
  | 'graph'
  | 'system-status'
  | 'cache-management'
  | 'document-preview';

export const routes: AppRoute[] = [
  {
    id: 'rag-chat',
    path: '/',
    title: 'RAG Chat',
    description: 'Chat with the knowledge workspace from the standalone React shell.'
  },
  {
    id: 'rag-chat',
    path: '/rag-chat',
    title: 'RAG Chat',
    description: 'Chat with the knowledge workspace from the standalone React shell.'
  },
  {
    id: 'documents',
    path: '/documents',
    title: 'Documents',
    description: 'Review document processing state and knowledge ingestion results.'
  },
  {
    id: 'upload',
    path: '/documents/upload',
    title: 'Upload Document',
    description: 'Prepare a document ingestion flow for the standalone React experience.'
  },
  {
    id: 'graph',
    path: '/graph-view',
    title: 'Knowledge Graph',
    description: 'Explore entity and relationship structure after graph migration lands.'
  },
  {
    id: 'system-status',
    path: '/system-status',
    title: 'System Status',
    description: 'Monitor service health and runtime checks after this page is migrated.'
  },
  {
    id: 'cache-management',
    path: '/cache-management',
    title: 'Cache Management',
    description: 'Inspect and manage cache state after the React migration reaches this area.'
  },
  {
    id: 'document-preview',
    path: '/document-preview',
    title: 'Document Preview',
    description: 'Preview document content from the standalone shell once the page is migrated.'
  }
];

export function resolveRoute(pathname: string = window.location.pathname): AppRoute {
  const normalizedPath = normalizePath(pathname);

  if (normalizedPath === '/document-preview' || normalizedPath.startsWith('/document-preview/')) {
    return routes.find((route) => route.id === 'document-preview') ?? routes[0];
  }

  return routes.find((route) => route.path === normalizedPath) ?? routes[0];
}

function normalizePath(pathname: string): string {
  if (pathname.length > 1 && pathname.endsWith('/')) {
    return pathname.slice(0, -1);
  }

  return pathname || '/';
}
