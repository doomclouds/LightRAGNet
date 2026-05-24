import type { ReactNode } from 'react';
import { Boxes, CircleDot, FileText } from 'lucide-react';
import type { RagTaskHubConnectionState } from '@/api/ragTaskHubClient';
import { StatusPill } from '@/shared/components/StatusPill';
import { ClearAllDataAction } from './ClearAllDataAction';
import { primaryNavigation } from './navigation';
import { resolveRoute } from './router';

type AppLayoutProps = {
  currentPath: string;
  connectionStatus: RagTaskHubConnectionState;
  children: ReactNode;
};

function getShellStatusTone(connectionStatus: RagTaskHubConnectionState): 'success' | 'accent' | 'warning' {
  if (connectionStatus === 'Connected') {
    return 'success';
  }

  if (connectionStatus === 'Connecting' || connectionStatus === 'Reconnecting') {
    return 'accent';
  }

  return 'warning';
}

export function AppLayout({ currentPath, connectionStatus, children }: AppLayoutProps) {
  const activeRoute = resolveRoute(currentPath);
  const shellStatusTone = getShellStatusTone(connectionStatus);

  return (
    <div className="app-shell">
      <header className="app-topbar">
        <a className="app-brand" href="/" aria-label="LightRAGNet home">
          <span className="app-brand__mark" aria-hidden="true">
            <Boxes size={20} strokeWidth={2.4} />
          </span>
          <span>LightRAGNet</span>
        </a>
        <ClearAllDataAction />
      </header>

      <div className="app-content">
        <aside className="app-sidebar" aria-label="Application sections">
          <nav className="app-nav" aria-label="Primary">
            {primaryNavigation.map((item) => (
              <a
                key={item.routeId}
                className="app-nav__link"
                href={item.href}
                aria-current={item.routeId === activeRoute.id ? 'page' : undefined}
              >
                <FileText size={16} aria-hidden="true" />
                <span>{item.label}</span>
              </a>
            ))}
          </nav>
        </aside>

        <main className="app-main">{children}</main>
      </div>

      <div className="app-statusbar" role="contentinfo" aria-label="Application status">
        <span className="app-statusbar__item">
          <CircleDot size={14} aria-hidden="true" />
          SignalR {connectionStatus}
        </span>
        <StatusPill tone={shellStatusTone}>{activeRoute.title}</StatusPill>
      </div>
    </div>
  );
}
