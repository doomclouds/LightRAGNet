import { AppLayout } from './AppLayout';
import { resolveRoute } from './router';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';

export function App() {
  const route = resolveRoute();

  return (
    <AppLayout>
      {route.path === '/documents/upload' ? (
        <UploadDocumentPage />
      ) : (
        <DocumentsPage />
      )}
    </AppLayout>
  );
}
