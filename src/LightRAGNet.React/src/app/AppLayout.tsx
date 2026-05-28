import type { ComponentType, ReactNode } from 'react';
import {
  Activity,
  Bell,
  BookOpen,
  CloudUpload,
  Database,
  FileSearch,
  FileText,
  Menu,
  MessageCircle,
  Network,
  Sun,
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
  'message-circle': MessageCircle,
  'file-text': FileText,
  network: Network,
  'cloud-upload': CloudUpload,
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
                      <span className="app-nav__icon" data-nav-icon={item.icon}>
                        <Icon size={17} aria-hidden="true" />
                      </span>
                      <span>{item.label}</span>
                    </a>
                  );
                })}
              </section>
            );
          })}
        </nav>

        <footer className="app-sidebar-status" role="contentinfo" aria-label="Application status">
          <span className={`app-realtime-status ${getRealtimeStatusClass(connectionStatus)}`}>
            <span className="app-realtime-status__dot" aria-hidden="true" />
            <span>SignalR {connectionStatus}</span>
          </span>
          <span className="app-version">LightRAGNet v{appVersion}</span>
        </footer>
      </aside>

      <section className="app-main-shell">
        <div className="app-topbar">
          <div className="app-topbar__left">
            <button className="app-topbar__icon-action" type="button" aria-label="Open navigation menu">
              <Menu size={17} aria-hidden="true" />
            </button>
            <div className="app-route-context">
              <span className="app-route-context__eyebrow">Current workspace</span>
              <strong>{activeRoute.title}</strong>
            </div>
          </div>
          <div className="app-topbar__actions">
            <button className="app-topbar__icon-action" type="button" aria-label="Toggle appearance">
              <Sun size={17} aria-hidden="true" />
            </button>
            <a className="app-topbar__icon-action" href="/document-preview" aria-label="Open documentation">
              <BookOpen size={17} aria-hidden="true" />
            </a>
            <button className="app-topbar__icon-action" type="button" aria-label="Open notifications">
              <Bell size={17} aria-hidden="true" />
            </button>
            <ClearAllDataAction />
          </div>
        </div>
        <main className={`app-main app-main--${activeRoute.id}`}>{children}</main>
      </section>
    </div>
  );
}
