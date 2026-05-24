import { FileText } from 'lucide-react';
import type { ReactNode } from 'react';

type AppLayoutProps = {
  children: ReactNode;
};

export function AppLayout({ children }: AppLayoutProps) {
  return (
    <div className="app-shell">
      <header className="app-header">
        <a className="app-brand" href="/documents" aria-label="LightRAGNet documents home">
          <span className="app-brand__mark" aria-hidden="true">
            <FileText size={20} strokeWidth={2.4} />
          </span>
          <span>LightRAGNet</span>
        </a>
        <nav className="app-nav" aria-label="Primary">
          <a className="app-nav__link" href="/documents">
            Documents
          </a>
          <a className="app-nav__link" href="/documents/upload">
            Upload
          </a>
        </nav>
      </header>
      <main className="app-main">{children}</main>
    </div>
  );
}
