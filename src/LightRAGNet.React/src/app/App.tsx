import { AppLayout } from './AppLayout';
import { resolveRoute } from './router';

export function App() {
  const route = resolveRoute();

  return (
    <AppLayout>
      <section className="document-panel" aria-labelledby="document-panel-title">
        <h1 id="document-panel-title">{route.title}</h1>
        <p>{route.description}</p>
      </section>
    </AppLayout>
  );
}
