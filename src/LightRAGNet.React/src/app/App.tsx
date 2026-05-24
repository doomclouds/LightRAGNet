import { AppLayout } from './AppLayout';
import { resolveRoute } from './router';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';

export function App() {
  const route = resolveRoute();

  return (
    <AppLayout>
      {route.path === '/documents/upload' ? (
        <UploadDocumentPage />
      ) : (
        <section className="document-panel" aria-labelledby="document-panel-title">
          <h1 id="document-panel-title">{route.title}</h1>
          <p>{route.description}</p>
        </section>
      )}
    </AppLayout>
  );
}
