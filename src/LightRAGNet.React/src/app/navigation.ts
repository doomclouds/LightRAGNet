import type { AppRouteId } from './router';

export type NavigationIconId =
  | 'message-square'
  | 'files'
  | 'network'
  | 'upload-cloud'
  | 'file-search'
  | 'activity'
  | 'database';

export type NavigationItem = {
  routeId: AppRouteId;
  label: string;
  href: string;
  icon: NavigationIconId;
};

export type NavigationGroup = {
  id: string;
  label: string;
  items: NavigationItem[];
};

export const primaryNavigationGroups: NavigationGroup[] = [
  {
    id: 'workspace',
    label: 'Workspace',
    items: [
      { routeId: 'rag-chat', label: 'RAG Chat', href: '/', icon: 'message-square' },
      { routeId: 'documents', label: 'Documents', href: '/documents', icon: 'files' },
      { routeId: 'graph', label: 'Knowledge Graph', href: '/graph-view', icon: 'network' }
    ]
  },
  {
    id: 'document-flow',
    label: 'Document Flow',
    items: [
      { routeId: 'upload', label: 'Upload Document', href: '/documents/upload', icon: 'upload-cloud' },
      { routeId: 'document-preview', label: 'Document Preview', href: '/document-preview', icon: 'file-search' }
    ]
  },
  {
    id: 'operations',
    label: 'Operations',
    items: [
      { routeId: 'system-status', label: 'System Status', href: '/system-status', icon: 'activity' },
      { routeId: 'cache-management', label: 'Cache Management', href: '/cache-management', icon: 'database' }
    ]
  }
];

export const primaryNavigation: NavigationItem[] = primaryNavigationGroups.flatMap((group) => group.items);
