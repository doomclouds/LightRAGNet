import type { AppRouteId } from './router';

export type NavigationIconId =
  | 'message-circle'
  | 'file-text'
  | 'network'
  | 'cloud-upload'
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
      { routeId: 'rag-chat', label: 'RAG Chat', href: '/', icon: 'message-circle' },
      { routeId: 'documents', label: 'Documents', href: '/documents', icon: 'file-text' },
      { routeId: 'graph', label: 'Knowledge Graph', href: '/graph-view', icon: 'network' }
    ]
  },
  {
    id: 'document-flow',
    label: 'Document Flow',
    items: [
      { routeId: 'upload', label: 'Upload Document', href: '/documents/upload', icon: 'cloud-upload' },
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
