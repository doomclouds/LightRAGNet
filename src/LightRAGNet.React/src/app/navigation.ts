import type { AppRouteId } from './router';

export type NavigationItem = {
  routeId: AppRouteId;
  label: string;
  href: string;
};

export const primaryNavigation: NavigationItem[] = [
  { routeId: 'rag-chat', label: 'RAG Chat', href: '/' },
  { routeId: 'documents', label: 'Documents', href: '/documents' },
  { routeId: 'upload', label: 'Upload', href: '/documents/upload' },
  { routeId: 'graph', label: 'Knowledge Graph', href: '/graph-view' },
  { routeId: 'system-status', label: 'System Status', href: '/system-status' },
  { routeId: 'cache-management', label: 'Cache Management', href: '/cache-management' },
  { routeId: 'document-preview', label: 'Document Preview', href: '/document-preview' }
];
