import type { ComponentType, ReactNode } from 'react';
import {
  Activity,
  Database,
  FileSearch,
  Files,
  MessageSquare,
  Network,
  UploadCloud,
  type LucideProps
} from 'lucide-react';
import type { RagTaskHubConnectionState } from '@/api/ragTaskHubClient';
import { AppBrandMark } from './AppBrandMark';
import { ClearAllDataAction } from './ClearAllDataAction';
import { appVersion } from './appVersion';
import { primaryNavigationGroups, type NavigationIconId } from './navigation';
import { resolveRoute } from './router';

type AppLayoutProps = {
  currentPath: string;
  connectionStatus: RagTaskHubConnectionState;
  children: ReactNode;
};

const navigationIcons: Record<NavigationIconId, ComponentType<LucideProps>> = {
  'message-square': MessageSquare,
  files: Files,
  network: Network,
  'upload-cloud': UploadCloud,
  'file-search': FileSearch,
  activity: Activity,
  database: Database
};

function getRealtimeStatusClass(connectionStatus: RagTaskHubConnectionState): string {
  if (connectionStatus === 'Connected') {
    return 'app-realtime-status--connected';
  }

  if (connectionStatus === 'Connecting' || connectionStatus === 'Reconnecting') {
    return 'app-realtime-status--connecting';
  }

  return 'app-realtime-status--disconnected';
}

function getNavigationHeadingId(id: string): string {
  return `nav-${id}`;
}

export function AppLayout({ currentPath, connectionStatus, children }: AppLayoutProps) {
  const activeRoute = resolveRoute(currentPath);

  return (
    <div className="app-frame">
      <aside className="app-sidebar" aria-label="Application sidebar">
        <header className="app-brand-row">
          <a className="app-brand" href="/" aria-label="LightRAGNet home">
            <AppBrandMark />
            <span>LightRAGNet</span>
          </a>
        </header>

        <nav className="app-nav" aria-label="Primary">
          {primaryNavigationGroups.map((group) => {
            const headingId = getNavigationHeadingId(group.id);

            return (
              <section className="app-nav__group" key={group.id} aria-labelledby={headingId}>
                <h2 className="app-nav__heading" id={headingId}>
                  {group.label}
                </h2>
                {group.items.map((item) => {
                  const Icon = navigationIcons[item.icon];

                  return (
                    <a
                      key={item.routeId}
                      className="app-nav__link"
                      href={item.href}
                      aria-current={item.routeId === activeRoute.id ? 'page' : undefined}
                    >
                      <Icon size={17} aria-hidden="true" />
                      <span>{item.label}</span>
                    </a>
                  );
                })}
              </section>
            );
          })}
        </nav>

        <div className="app-sidebar-status" aria-label="Application status">
          <span className={`app-realtime-status ${getRealtimeStatusClass(connectionStatus)}`}>
            <span className="app-realtime-status__dot" aria-hidden="true" />
            <span>SignalR {connectionStatus}</span>
          </span>
          <span className="app-version">LightRAGNet v{appVersion}</span>
        </div>
      </aside>

      <section className="app-main-shell">
        <div className="app-topbar">
          <div className="app-route-context">
            <span className="app-route-context__eyebrow">Current workspace</span>
            <strong>{activeRoute.title}</strong>
          </div>
          <ClearAllDataAction />
        </div>
        <div className="app-main">{children}</div>
      </section>
    </div>
  );
}
