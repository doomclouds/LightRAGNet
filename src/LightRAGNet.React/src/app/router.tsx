export type AppRoute = {
  path: string;
  title: string;
  description: string;
};

const routes: AppRoute[] = [
  {
    path: '/documents/upload',
    title: 'Upload Document',
    description: 'Prepare a document ingestion flow for the standalone React experience.'
  },
  {
    path: '/documents',
    title: 'Documents',
    description: 'Review document processing state and knowledge ingestion results.'
  }
];

export function resolveRoute(pathname: string = window.location.pathname): AppRoute {
  return routes.find((route) => route.path === pathname) ?? routes[1];
}
