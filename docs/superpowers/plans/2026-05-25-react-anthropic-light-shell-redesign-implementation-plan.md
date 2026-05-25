# React Anthropic Light Shell Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the standalone React shell around the approved Anthropic-like light prototype and redesign the Documents, Upload Document, and Document Preview pages without changing untouched page behavior.

**Architecture:** Replace the current dark topbar/sidebar/statusbar shell with a grouped left-shell layout and shared light design tokens. Establish small shared UI primitives for buttons, icon buttons, panels, table surfaces, empty/error states, and shell status, then apply them to the three document pages while keeping existing API and SignalR behavior intact.

**Tech Stack:** React 19, TypeScript, Vite, Vitest, Testing Library, `lucide-react`, existing LightRAGNet REST and SignalR clients.

---

## Source Of Truth

- Spec: `docs/superpowers/specs/2026-05-25-react-anthropic-light-shell-redesign-design.md`
- Approved prototype: `docs/superpowers/visuals/anthropic-light-workbench/app-frame-documents-drawer-prototype.html`
- Current React app root: `src/LightRAGNet.React`

## File Map

Modify:

- `src/LightRAGNet.React/src/shared/styles/theme.css`: replace dark tokens with approved light semantic tokens and compatibility aliases.
- `src/LightRAGNet.React/src/shared/styles/app.css`: rebuild shell, shared component, table, form, drawer, modal, and responsive layout styles.
- `src/LightRAGNet.React/src/app/AppLayout.tsx`: move to approved two-column shell, grouped sidebar, topbar, SignalR footer, and icon standard.
- `src/LightRAGNet.React/src/app/navigation.ts`: add grouped navigation metadata and per-item icon ids.
- `src/LightRAGNet.React/src/app/router.tsx`: keep routes stable; verify `/document-preview/:id` continues to resolve as `document-preview`.
- `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`: rebuild document list markup around shared light workbench classes; keep behavior and handlers.
- `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`: rebuild upload workbench markup; keep validation and multipart behavior.
- `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`: rebuild full preview page markup; keep safe preview API behavior.
- `src/LightRAGNet.React/src/features/document-preview/document-preview.css`: align preview reading surface with shared tokens, or reduce to preview-specific content styles only.
- `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`: update shell structure and SignalR footer assertions.
- `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`: update visual contract assertions for light shell and standard icon actions.
- `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`: update visual contract assertions for light upload workbench.
- `src/LightRAGNet.React/tests/integration/features/document-preview/DocumentPreviewPage.test.tsx`: update preview page visual contract assertions.
- `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`: update token expectations from dark to approved light tokens.

Create:

- `src/LightRAGNet.React/src/app/appVersion.ts`: central app version display value.
- `src/LightRAGNet.React/src/app/AppBrandMark.tsx`: LightRAGNet logo mark used by the shell.
- `src/LightRAGNet.React/src/shared/components/Button.tsx`: shared anchor/button styling contract.
- `src/LightRAGNet.React/src/shared/components/IconButton.tsx`: accessible compact icon action primitive.
- `src/LightRAGNet.React/src/shared/components/Panel.tsx`: shared elevated surface primitive.
- `src/LightRAGNet.React/src/shared/components/EmptyState.tsx`: shared empty-state surface.
- `src/LightRAGNet.React/src/shared/components/ErrorState.tsx`: shared alert/error surface.

No changes:

- `src/LightRAGNet.React/src/features/rag-chat/*`: no content redesign in this phase.
- `src/LightRAGNet.React/src/features/graph-workbench/*`: no graph interaction redesign in this phase.
- `src/LightRAGNet.React/src/features/system-status/*`: no content redesign in this phase.
- `src/LightRAGNet.React/src/features/cache-management/*`: no content redesign in this phase.
- Backend API projects.

---

### Task 1: Lock Shell Navigation And SignalR Footer Tests

**Files:**

- Modify: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`
- Modify: `src/LightRAGNet.React/src/app/navigation.ts`

- [ ] **Step 1: Update grouped navigation test expectations**

In `AppLayout.test.tsx`, update the shell test so it verifies grouped navigation and the sidebar footer:

```tsx
expect(screen.getByRole('banner')).toHaveTextContent('LightRAGNet');

const navigation = within(screen.getByRole('navigation', { name: 'Primary' }));
expect(screen.getByText('Workspace')).toBeInTheDocument();
expect(screen.getByText('Document Flow')).toBeInTheDocument();
expect(screen.getByText('Operations')).toBeInTheDocument();
expect(navigation.getByRole('link', { name: 'RAG Chat' })).toHaveAttribute('href', '/');
expect(navigation.getByRole('link', { name: 'Documents' })).toHaveAttribute('href', '/documents');
expect(navigation.getByRole('link', { name: 'Knowledge Graph' })).toHaveAttribute('href', '/graph-view');
expect(navigation.getByRole('link', { name: 'Upload Document' })).toHaveAttribute('href', '/documents/upload');
expect(navigation.getByRole('link', { name: 'Document Preview' })).toHaveAttribute('href', '/document-preview');
expect(navigation.getByRole('link', { name: 'System Status' })).toHaveAttribute('href', '/system-status');
expect(navigation.getByRole('link', { name: 'Cache Management' })).toHaveAttribute('href', '/cache-management');

const status = screen.getByRole('contentinfo', { name: 'Application status' });
expect(status).toHaveTextContent('SignalR Connecting');
expect(status).toHaveTextContent('LightRAGNet v0.1.0');
```

- [ ] **Step 2: Update SignalR state assertions**

In the SignalR test, expect the status in the sidebar footer and tone classes on the status line:

```tsx
const status = screen.getByRole('contentinfo', { name: 'Application status' });
expect(status).toHaveTextContent('SignalR Connecting');
expect(status.querySelector('.app-realtime-status--connecting')).toBeInTheDocument();

await act(async () => {
  client.capturedHandlers?.onConnectionStateChanged?.('Connected');
});

expect(status).toHaveTextContent('SignalR Connected');
expect(status.querySelector('.app-realtime-status--connected')).toBeInTheDocument();

await act(async () => {
  client.capturedHandlers?.onConnectionStateChanged?.('ServerNotStarted');
});

expect(status).toHaveTextContent('SignalR ServerNotStarted');
expect(status.querySelector('.app-realtime-status--disconnected')).toBeInTheDocument();
```

- [ ] **Step 3: Run the focused failing test**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx --run
```

Expected: FAIL because grouped navigation, sidebar footer classes, and version display are not implemented yet.

- [ ] **Step 4: Update navigation data shape**

Replace `navigation.ts` with grouped metadata:

```ts
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
  label: string;
  items: NavigationItem[];
};

export const primaryNavigationGroups: NavigationGroup[] = [
  {
    label: 'Workspace',
    items: [
      { routeId: 'rag-chat', label: 'RAG Chat', href: '/', icon: 'message-square' },
      { routeId: 'documents', label: 'Documents', href: '/documents', icon: 'files' },
      { routeId: 'graph', label: 'Knowledge Graph', href: '/graph-view', icon: 'network' }
    ]
  },
  {
    label: 'Document Flow',
    items: [
      { routeId: 'upload', label: 'Upload Document', href: '/documents/upload', icon: 'upload-cloud' },
      { routeId: 'document-preview', label: 'Document Preview', href: '/document-preview', icon: 'file-search' }
    ]
  },
  {
    label: 'Operations',
    items: [
      { routeId: 'system-status', label: 'System Status', href: '/system-status', icon: 'activity' },
      { routeId: 'cache-management', label: 'Cache Management', href: '/cache-management', icon: 'database' }
    ]
  }
];
```

- [ ] **Step 5: Commit task**

After implementation and passing focused tests later in Task 2, commit together with shell implementation rather than committing a deliberately failing test alone.

---

### Task 2: Implement Approved App Shell

**Files:**

- Create: `src/LightRAGNet.React/src/app/appVersion.ts`
- Create: `src/LightRAGNet.React/src/app/AppBrandMark.tsx`
- Modify: `src/LightRAGNet.React/src/app/AppLayout.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/theme.css`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`
- Test: `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`

- [ ] **Step 1: Update theme token test**

In `theme.test.ts`, assert the approved light tokens:

```ts
expect(rootStyles).toContain('--app-bg: #fbfaf6');
expect(rootStyles).toContain('--panel-bg: #fffefa');
expect(rootStyles).toContain('--panel-border: #e5ded2');
expect(rootStyles).toContain('--accent: #c8552d');
expect(rootStyles).toContain('--shadow-panel: 0 18px 46px rgba(64, 46, 24, .08)');
```

- [ ] **Step 2: Run the focused failing tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx tests/unit/shared/styles/theme.test.ts --run
```

Expected: FAIL because dark tokens and old shell are still present.

- [ ] **Step 3: Add version helper**

Create `appVersion.ts`:

```ts
const fallbackVersion = '0.1.0';

export const appVersion = import.meta.env.VITE_APP_VERSION?.trim() || fallbackVersion;
```

- [ ] **Step 4: Add brand mark component**

Create `AppBrandMark.tsx`:

```tsx
export function AppBrandMark() {
  return (
    <span className="app-brand__mark" aria-hidden="true">
      <svg viewBox="0 0 32 32" focusable="false">
        <circle cx="16" cy="16" r="13" />
        <path d="M10.5 17.5c3.6-7.2 8-8.1 11-5.1 3 3 .9 7.7-5.4 9.6" />
        <path d="M11 21.3c5.3.9 9.4-.7 11.8-4.4" />
        <path d="M12.8 10.3c-1.3 2.8-1.1 5.1.7 6.9" />
      </svg>
    </span>
  );
}
```

- [ ] **Step 5: Rebuild `AppLayout.tsx`**

Use grouped navigation, `lucide-react` icons, topbar, sidebar footer, and existing `ClearAllDataAction`:

```tsx
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
import { ClearAllDataAction } from './ClearAllDataAction';
import { AppBrandMark } from './AppBrandMark';
import { appVersion } from './appVersion';
import { primaryNavigationGroups, type NavigationIconId } from './navigation';
import { resolveRoute } from './router';
```

Define icon and status helpers:

```tsx
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
```

Render structure:

```tsx
return (
  <div className="app-frame">
    <aside className="app-sidebar" aria-label="Application sidebar">
      <header className="app-brand-row" role="banner">
        <a className="app-brand" href="/" aria-label="LightRAGNet home">
          <AppBrandMark />
          <span>LightRAGNet</span>
        </a>
      </header>

      <nav className="app-nav" aria-label="Primary">
        {primaryNavigationGroups.map((group) => (
          <section className="app-nav__group" key={group.label} aria-labelledby={`nav-${group.label.replace(/\s+/g, '-').toLowerCase()}`}>
            <h2 className="app-nav__heading" id={`nav-${group.label.replace(/\s+/g, '-').toLowerCase()}`}>
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
        ))}
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
      <header className="app-topbar">
        <div className="app-route-context">
          <span className="app-route-context__eyebrow">Current workspace</span>
          <strong>{activeRoute.title}</strong>
        </div>
        <ClearAllDataAction />
      </header>
      <main className="app-main">{children}</main>
    </section>
  </div>
);
```

- [ ] **Step 6: Replace light theme tokens**

In `theme.css`, set `color-scheme: light` and keep compatibility aliases:

```css
:root {
  color-scheme: light;
  font-family: Inter, "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
  --app-bg: #fbfaf6;
  --panel-bg: #fffefa;
  --panel-bg-elevated: #f7f3ea;
  --panel-bg-raised: #f0eadf;
  --panel-border: #e5ded2;
  --panel-border-strong: #d7ccbd;
  --text-primary: #191817;
  --text-secondary: #5f5a52;
  --text-muted: #8f887d;
  --accent: #c8552d;
  --accent-strong: #a94221;
  --accent-soft: #f3e2d8;
  --accent-border: #e0b9a5;
  --accent-fill: #c8552d;
  --accent-fill-hover: #a94221;
  --accent-on-fill: #fffefa;
  --danger: #ce4c34;
  --danger-soft: #f7dfd9;
  --warning: #c6871d;
  --warning-soft: #f8ead0;
  --success: #4d8a58;
  --success-soft: #e6f0e3;
  --control-bg: #fffefa;
  --control-border: #d7ccbd;
  --shadow-panel: 0 18px 46px rgba(64, 46, 24, .08);
  --shadow-card: 0 10px 24px rgba(64, 46, 24, .06);
  --shadow-popover: 0 18px 34px rgba(64, 46, 24, .14);
  --shadow-modal: 0 28px 80px rgba(36, 31, 26, .22);
  --scrim: rgba(36, 31, 26, .30);
  --radius-panel: 10px;
  --radius-control: 8px;
  --sidebar-width: 202px;
  --topbar-height: 64px;

  --color-bg: var(--app-bg);
  --color-surface: var(--panel-bg);
  --color-surface-muted: var(--panel-bg-elevated);
  --color-border: var(--panel-border);
  --color-text: var(--text-primary);
  --color-text-muted: var(--text-secondary);
  --color-primary: var(--accent);
  --color-primary-strong: var(--accent-strong);
  --color-primary-soft: var(--accent-soft);
  --shadow-soft: var(--shadow-panel);
}
```

- [ ] **Step 7: Rebuild shell CSS**

In `app.css`, replace the old `.app-shell`, `.app-content`, and `.app-statusbar` layout with `.app-frame`, `.app-sidebar`, `.app-main-shell`, `.app-topbar`, `.app-main`, `.app-sidebar-status`, and `.app-realtime-status` styles from the approved prototype. Preserve existing shared classes such as `.lrn-page-header`, `.lrn-status-pill`, `.lrn-panel`, and `.lrn-data-table`, but recolor them to the light tokens.

Required CSS selectors:

```css
.app-frame { min-height: 100vh; display: grid; grid-template-columns: var(--sidebar-width) minmax(0, 1fr); }
.app-sidebar { display: grid; grid-template-rows: var(--topbar-height) minmax(0, 1fr) auto; border-right: 1px solid var(--panel-border); }
.app-brand-row { display: flex; align-items: center; border-bottom: 1px solid var(--panel-border); }
.app-nav__group + .app-nav__group { margin-top: 28px; }
.app-nav__link[aria-current="page"] { color: #873819; background: var(--accent-soft); }
.app-sidebar-status { margin: 0 18px 16px; padding-top: 16px; border-top: 1px solid var(--panel-border); }
.app-realtime-status { display: inline-flex; align-items: center; gap: 8px; font-weight: 720; }
.app-realtime-status--connected { color: #3c6f45; }
.app-realtime-status--connecting { color: #8a5d12; }
.app-realtime-status--disconnected { color: #a63a28; }
.app-main-shell { display: grid; grid-template-rows: var(--topbar-height) minmax(0, 1fr); min-width: 0; }
.app-main { min-width: 0; padding: 24px 28px 36px; }
```

- [ ] **Step 8: Run focused tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx tests/unit/shared/styles/theme.test.ts --run
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/LightRAGNet.React/src/app src/LightRAGNet.React/src/shared/styles src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts
git commit -m "feat: rebuild light React app shell"
```

---

### Task 3: Add Shared Light Workbench Primitives

**Files:**

- Create: `src/LightRAGNet.React/src/shared/components/Button.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/IconButton.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/Panel.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/EmptyState.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/ErrorState.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`

- [ ] **Step 1: Add component usage assertions to Documents visual contract test**

In `DocumentsPage.test.tsx`, extend the visual contract test:

```tsx
expect(screen.getByRole('region', { name: 'Document summary' })).toHaveClass('document-list__summary-grid');
expect(screen.getByRole('table', { name: 'Document lifecycle' })).toHaveClass('lrn-data-table');
expect(screen.getByRole('button', { name: 'View system-architecture.md' })).toHaveClass('lrn-icon-button');
```

- [ ] **Step 2: Run the focused failing test**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx --run
```

Expected: FAIL because shared primitive classes are not consistently used yet.

- [ ] **Step 3: Create `Button.tsx`**

```tsx
import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from 'react';

type ButtonTone = 'primary' | 'secondary' | 'danger';

type ButtonBaseProps = {
  tone?: ButtonTone;
  children: ReactNode;
};

type ButtonProps = ButtonBaseProps & ButtonHTMLAttributes<HTMLButtonElement>;
type ButtonLinkProps = ButtonBaseProps & AnchorHTMLAttributes<HTMLAnchorElement>;

function getButtonClassName(tone: ButtonTone = 'secondary', className?: string): string {
  return ['lrn-button', `lrn-button--${tone}`, className].filter(Boolean).join(' ');
}

export function Button({ tone = 'secondary', className, children, ...props }: ButtonProps) {
  return (
    <button className={getButtonClassName(tone, className)} type="button" {...props}>
      {children}
    </button>
  );
}

export function ButtonLink({ tone = 'secondary', className, children, ...props }: ButtonLinkProps) {
  return (
    <a className={getButtonClassName(tone, className)} {...props}>
      {children}
    </a>
  );
}
```

- [ ] **Step 4: Create `IconButton.tsx`**

```tsx
import type { ButtonHTMLAttributes, ComponentType } from 'react';
import type { LucideProps } from 'lucide-react';

type IconButtonTone = 'neutral' | 'primary' | 'danger';

type IconButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  icon: ComponentType<LucideProps>;
  label: string;
  tone?: IconButtonTone;
};

export function IconButton({ icon: Icon, label, tone = 'neutral', className, ...props }: IconButtonProps) {
  return (
    <button
      className={['lrn-icon-button', `lrn-icon-button--${tone}`, className].filter(Boolean).join(' ')}
      type="button"
      aria-label={label}
      title={label}
      {...props}
    >
      <Icon size={16} aria-hidden="true" />
    </button>
  );
}
```

- [ ] **Step 5: Create surface and state components**

Create `Panel.tsx`:

```tsx
import type { HTMLAttributes, ReactNode } from 'react';

type PanelProps = HTMLAttributes<HTMLElement> & {
  as?: 'section' | 'article' | 'div';
  children: ReactNode;
};

export function Panel({ as: Component = 'section', className, children, ...props }: PanelProps) {
  return (
    <Component className={['lrn-panel', className].filter(Boolean).join(' ')} {...props}>
      {children}
    </Component>
  );
}
```

Create `EmptyState.tsx`:

```tsx
type EmptyStateProps = {
  title: string;
  description: string;
};

export function EmptyState({ title, description }: EmptyStateProps) {
  return (
    <div className="lrn-empty-state">
      <strong>{title}</strong>
      <p>{description}</p>
    </div>
  );
}
```

Create `ErrorState.tsx`:

```tsx
type ErrorStateProps = {
  message: string;
};

export function ErrorState({ message }: ErrorStateProps) {
  return (
    <div className="lrn-error-state" role="alert">
      {message}
    </div>
  );
}
```

- [ ] **Step 6: Add primitive CSS**

Add or update these selectors in `app.css`:

```css
.lrn-button { min-height: 36px; display: inline-flex; align-items: center; justify-content: center; gap: 8px; padding: 0 12px; border-radius: var(--radius-control); border: 1px solid var(--control-border); font-weight: 720; cursor: pointer; }
.lrn-button--primary { color: var(--accent-on-fill); background: var(--accent-fill); border-color: var(--accent-fill); }
.lrn-button--secondary { color: var(--text-primary); background: var(--panel-bg); }
.lrn-button--danger { color: #8d2f21; background: var(--danger-soft); border-color: rgba(206, 76, 52, .34); }
.lrn-icon-button { width: 34px; height: 34px; display: inline-grid; place-items: center; border-radius: 8px; border: 1px solid var(--control-border); color: var(--text-secondary); background: var(--panel-bg); cursor: pointer; }
.lrn-icon-button:hover, .lrn-icon-button:focus-visible { color: var(--accent-strong); border-color: var(--accent-border); background: var(--accent-soft); outline: none; }
.lrn-icon-button--danger:hover, .lrn-icon-button--danger:focus-visible { color: #8d2f21; border-color: rgba(206, 76, 52, .38); background: var(--danger-soft); }
.lrn-empty-state, .lrn-error-state { border: 1px solid var(--panel-border); border-radius: var(--radius-panel); background: var(--panel-bg); box-shadow: var(--shadow-card); padding: 18px; }
.lrn-error-state { color: #8d2f21; background: var(--danger-soft); border-color: rgba(206, 76, 52, .34); }
```

- [ ] **Step 7: Run focused tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx --run
```

Expected: PASS after Task 4 updates the Documents markup. If this task is implemented before Task 4, the expected result remains FAIL on Documents-specific assertions and PASS on component compile.

- [ ] **Step 8: Commit**

Commit after Task 4 if Documents markup is updated in the same patch:

```powershell
git add src/LightRAGNet.React/src/shared/components src/LightRAGNet.React/src/shared/styles/app.css src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx
git commit -m "feat: add light workbench primitives"
```

---

### Task 4: Redesign Documents Page With Existing Behavior

**Files:**

- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentPreviewDrawer.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentActions.test.tsx`

- [ ] **Step 1: Update tests for icon actions and light summary**

Keep existing behavior tests. Update only presentation assertions:

```tsx
expect(screen.getByRole('region', { name: 'Document summary' })).toBeInTheDocument();
expect(screen.getByText('Active on this page')).toBeInTheDocument();
expect(screen.getByText('Failed on this page')).toBeInTheDocument();
expect(screen.getByText('Completed on this page')).toBeInTheDocument();

const row = within(table).getByRole('row', { name: /system-architecture\.md/i });
expect(within(row).getByRole('button', { name: 'View system-architecture.md' })).toHaveClass('lrn-icon-button');
expect(within(row).getByRole('button', { name: 'Delete system-architecture.md' })).toHaveClass('lrn-icon-button--danger');
```

- [ ] **Step 2: Run focused failing tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx tests/integration/features/documents/DocumentPreviewDrawer.test.tsx tests/integration/features/documents/DocumentActions.test.tsx --run
```

Expected: FAIL on updated visual class assertions only. Existing behavior assertions should keep passing unless the markup query needs an accessibility adjustment.

- [ ] **Step 3: Import shared primitives and icons**

At the top of `DocumentsPage.tsx`, add:

```tsx
import { Download, Eye, Play, RotateCcw, Trash2, XCircle } from 'lucide-react';
import { ButtonLink } from '@/shared/components/Button';
import { EmptyState } from '@/shared/components/EmptyState';
import { ErrorState } from '@/shared/components/ErrorState';
import { IconButton } from '@/shared/components/IconButton';
import { Panel } from '@/shared/components/Panel';
```

- [ ] **Step 4: Replace upload action with shared button link**

Use:

```tsx
actions={
  <ButtonLink tone="primary" className="document-list__upload-link" href="/documents/upload" aria-label="Upload Document">
    Upload Document
  </ButtonLink>
}
```

- [ ] **Step 5: Replace summary block with real list-derived cards**

Render summary region:

```tsx
<section className="document-list__summary-grid" aria-label="Document summary">
  <Panel as="article" className="document-list__summary-card">
    <span>Total in result</span>
    <strong>{totalCount}</strong>
  </Panel>
  <Panel as="article" className="document-list__summary-card">
    <span>Active on this page</span>
    <strong>{activeCount}</strong>
  </Panel>
  <Panel as="article" className="document-list__summary-card">
    <span>Failed on this page</span>
    <strong>{failedCount}</strong>
  </Panel>
  <Panel as="article" className="document-list__summary-card">
    <span>Completed on this page</span>
    <strong>{completedCount}</strong>
  </Panel>
</section>
```

- [ ] **Step 6: Keep status tabs and select behavior**

Keep existing `PageTabs`, `handleStatusTabClick`, `handleStatusChange`, and URL sync logic. Wrap the controls with light toolbar classes:

```tsx
<div className="document-list__toolbar">
  <div onClickCapture={handleStatusTabClick}>
    <PageTabs items={statusTabs} activeId={status.length > 0 ? status.toLowerCase() : 'all'} label="Document status views" />
  </div>
  <label className="document-list__filter">
    <span>Status</span>
    <select value={status} onChange={handleStatusChange}>
      <option value="">All</option>
      {statusOptions.map((option) => (
        <option key={option} value={option}>
          {option}
        </option>
      ))}
    </select>
  </label>
</div>
```

- [ ] **Step 7: Replace error and empty states**

Use:

```tsx
{errorMessage ? <ErrorState message={errorMessage} /> : null}
{!isLoading && !errorMessage && documents.length === 0 ? (
  <EmptyState title="No documents found" description="Upload documents first, or adjust the selected status filter." />
) : null}
```

- [ ] **Step 8: Replace row action buttons with `IconButton`**

Keep all existing handler logic. Render only actions that are currently eligible:

```tsx
<IconButton icon={Eye} label={`View ${document.fileName}`} onClick={() => handleView(document)} disabled={isPending} />
<a className="lrn-icon-link" href={getDownloadHref(apiBase, document.id)} aria-label={`Download ${document.fileName}`} title={`Download ${document.fileName}`}>
  <Download size={16} aria-hidden="true" />
</a>
{canAddToRag(document) ? (
  <IconButton icon={Play} label={`Add ${document.fileName} to RAG`} tone="primary" onClick={() => handleAddToRag(document)} disabled={isPending} />
) : null}
{canRetry(document) ? (
  <IconButton icon={RotateCcw} label={`Retry ${document.fileName}`} onClick={() => handleRetry(document)} disabled={isPending} />
) : null}
{canCancel(document) ? (
  <IconButton icon={XCircle} label={`Cancel ${document.fileName}`} onClick={() => handleCancel(document)} disabled={isPending} />
) : null}
<IconButton icon={Trash2} label={`Delete ${document.fileName}`} tone="danger" onClick={() => handleDelete(document)} disabled={isPending} />
```

- [ ] **Step 9: Preserve preview drawer behavior**

Do not remove `DocumentPreviewPanel` in this implementation, because existing tests and current user journey rely on same-page preview. Only style it to match the light overlay layer. Keep:

```tsx
{previewDocument ? (
  <DocumentPreviewPanel
    apiBase={apiBase}
    document={previewDocument}
    loadPreview={loadPreview}
    onClose={closePreview}
  />
) : null}
```

- [ ] **Step 10: Add document page CSS**

In `app.css`, add light workbench selectors:

```css
.document-list { display: grid; gap: 18px; }
.document-list__summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
.document-list__summary-card { padding: 14px 16px; }
.document-list__summary-card span { color: var(--text-muted); font-size: 12px; font-weight: 720; }
.document-list__summary-card strong { display: block; margin-top: 8px; font-size: 22px; }
.document-list__toolbar { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
.document-list__filter { display: inline-flex; align-items: center; gap: 8px; color: var(--text-secondary); font-weight: 700; }
.document-list__table-panel { overflow: hidden; }
.document-list__actions { display: inline-flex; align-items: center; gap: 6px; }
.lrn-icon-link { width: 34px; height: 34px; display: inline-grid; place-items: center; border-radius: 8px; border: 1px solid var(--control-border); color: var(--text-secondary); background: var(--panel-bg); }
```

- [ ] **Step 11: Run focused tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx tests/integration/features/documents/DocumentPreviewDrawer.test.tsx tests/integration/features/documents/DocumentActions.test.tsx --run
```

Expected: PASS.

- [ ] **Step 12: Commit**

Run:

```powershell
git add src/LightRAGNet.React/src/features/documents src/LightRAGNet.React/src/shared src/LightRAGNet.React/tests/integration/features/documents
git commit -m "feat: redesign document list workbench"
```

---

### Task 5: Redesign Upload Document Page

**Files:**

- Modify: `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`

- [ ] **Step 1: Update upload visual contract test**

Keep validation tests unchanged. Update visual assertions:

```tsx
expect(screen.getByRole('region', { name: 'Upload workbench' })).toHaveClass('document-upload__workbench');
expect(screen.getByText('Accepted formats')).toBeInTheDocument();
expect(screen.getByText('.md, .markdown, .pdf, .docx')).toBeInTheDocument();
expect(screen.getByRole('button', { name: 'Upload' })).toHaveClass('lrn-button--primary');
expect(screen.getByRole('button', { name: 'Clear selection' })).toBeInTheDocument();
```

- [ ] **Step 2: Run focused failing test**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/UploadDocumentPage.test.tsx --run
```

Expected: FAIL because the clear selection action and light workbench classes are not implemented.

- [ ] **Step 3: Import shared components**

Add:

```tsx
import { FileUp, Trash2 } from 'lucide-react';
import { Button, ButtonLink } from '@/shared/components/Button';
import { EmptyState } from '@/shared/components/EmptyState';
import { ErrorState } from '@/shared/components/ErrorState';
import { Panel } from '@/shared/components/Panel';
```

- [ ] **Step 4: Add clear selection handler**

Add:

```tsx
function clearSelection() {
  if (isUploading) {
    return;
  }

  setFiles([]);
  setMessages([]);
  setHasBlockingErrors(false);
  setSuccessMessage(null);
  setErrorMessage(null);

  if (inputRef.current) {
    inputRef.current.value = '';
  }
}
```

- [ ] **Step 5: Rebuild page actions**

Use:

```tsx
actions={
  <ButtonLink href="/documents">
    Back to Documents
  </ButtonLink>
}
```

- [ ] **Step 6: Rebuild workbench markup**

Wrap upload and selected files in a labelled region:

```tsx
<section className="document-upload__workbench" aria-label="Upload workbench">
  <Panel className="document-upload__panel" aria-labelledby="batch-upload-title">
    <div className="document-upload__panel-header">
      <h2 id="batch-upload-title">Batch Upload</h2>
      <span>Local validation runs before submit.</span>
    </div>
    <div className="document-upload__dropzone" onDragOver={(event) => event.preventDefault()} onDrop={handleDrop}>
      <FileUp size={30} aria-hidden="true" />
      <strong>Drop documents here</strong>
      <label className="document-upload__picker">
        <span>Choose documents</span>
        <input ref={inputRef} type="file" multiple accept={acceptedExtensions.join(',')} aria-label="Choose documents" disabled={isUploading} onChange={handleFileChange} />
      </label>
      <span className="document-upload__hint">Accepted formats</span>
      <span className="document-upload__hint">.md, .markdown, .pdf, .docx</span>
    </div>
  </Panel>

  <Panel className="document-upload__panel document-upload__selected-panel" aria-labelledby="selected-files-title">
    <div className="document-upload__panel-header">
      <h2 id="selected-files-title">Selected Files</h2>
      <span>{files.length} / {maxFiles} staged</span>
    </div>
    {/* keep existing selected file list and empty state */}
  </Panel>
</section>
```

- [ ] **Step 7: Use shared empty and error surfaces**

For no files:

```tsx
<EmptyState title="No files selected" description="Choose up to 10 documents before uploading." />
```

For upload errors:

```tsx
{errorMessage ? <ErrorState message={errorMessage} /> : null}
```

- [ ] **Step 8: Add primary and secondary actions**

Replace the submit button with:

```tsx
<div className="document-upload__actions">
  <Button tone="primary" onClick={handleUpload} disabled={isUploading}>
    {isUploading ? 'Uploading...' : 'Upload'}
  </Button>
  <Button onClick={clearSelection} disabled={isUploading || files.length === 0}>
    <Trash2 size={15} aria-hidden="true" />
    Clear selection
  </Button>
</div>
```

- [ ] **Step 9: Add upload CSS**

In `app.css`, add or update:

```css
.document-upload { display: grid; gap: 18px; }
.document-upload__workbench { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(320px, .95fr); gap: 16px; }
.document-upload__panel { padding: 18px; }
.document-upload__panel-header { display: flex; align-items: start; justify-content: space-between; gap: 14px; margin-bottom: 16px; }
.document-upload__panel-header h2 { margin: 0; font-size: 16px; }
.document-upload__panel-header span { color: var(--text-muted); font-size: 13px; }
.document-upload__dropzone { min-height: 240px; display: grid; place-items: center; align-content: center; gap: 12px; border: 1px dashed var(--panel-border-strong); border-radius: var(--radius-panel); background: var(--panel-bg-elevated); color: var(--text-secondary); }
.document-upload__picker { min-height: 34px; display: inline-flex; align-items: center; justify-content: center; padding: 0 12px; border-radius: var(--radius-control); color: var(--accent-on-fill); background: var(--accent-fill); cursor: pointer; font-weight: 720; }
.document-upload__picker input { position: absolute; inline-size: 1px; block-size: 1px; opacity: 0; pointer-events: none; }
.document-upload__file-list { display: grid; gap: 8px; margin: 12px 0 0; padding: 0; list-style: none; }
.document-upload__file-list li { display: flex; justify-content: space-between; gap: 12px; padding: 9px 10px; border: 1px solid var(--panel-border); border-radius: 8px; background: var(--panel-bg-elevated); }
.document-upload__actions { display: flex; align-items: center; justify-content: flex-end; gap: 10px; }
```

- [ ] **Step 10: Run focused tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/documents/UploadDocumentPage.test.tsx --run
```

Expected: PASS.

- [ ] **Step 11: Commit**

Run:

```powershell
git add src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx src/LightRAGNet.React/src/shared/styles/app.css src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx
git commit -m "feat: redesign upload document workbench"
```

---

### Task 6: Redesign Full Document Preview Page

**Files:**

- Modify: `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/document-preview/document-preview.css`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/document-preview/DocumentPreviewPage.test.tsx`

- [ ] **Step 1: Update preview visual contract test**

Keep API loading and error behavior tests unchanged. Add visual assertions:

```tsx
expect(screen.getByRole('region', { name: 'Document Preview' })).toHaveClass('document-preview-page');
expect(screen.getByText('Reading workspace')).toBeInTheDocument();
expect(screen.getByRole('article', { name: 'Document preview content' })).toHaveClass('document-preview-page__content');
```

- [ ] **Step 2: Run focused failing test**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/document-preview/DocumentPreviewPage.test.tsx --run
```

Expected: FAIL on new visual assertions.

- [ ] **Step 3: Import shared state components**

Add:

```tsx
import { EmptyState } from '@/shared/components/EmptyState';
import { ErrorState } from '@/shared/components/ErrorState';
import { Panel } from '@/shared/components/Panel';
```

- [ ] **Step 4: Update header meta**

Include a real workspace label without adding unsupported commands:

```tsx
meta={
  <>
    <StatusPill tone="accent">Reading workspace</StatusPill>
    <StatusPill tone={documentId ? 'accent' : 'neutral'}>
      {documentId ? `Document ${documentId}` : 'No document selected'}
    </StatusPill>
    {preview?.fileName ? <StatusPill tone="neutral">{preview.fileName}</StatusPill> : null}
    {preview?.contentType ? <StatusPill tone="neutral">{preview.contentType}</StatusPill> : null}
  </>
}
```

- [ ] **Step 5: Replace empty, loading, and error states**

Use:

```tsx
{!documentId ? (
  <EmptyState title="No document selected" description="Open a document from Documents or a RAG Chat reference." />
) : null}

{isLoading ? (
  <Panel className="document-preview-page__state">Loading preview</Panel>
) : null}

{errorMessage ? <ErrorState message={errorMessage} /> : null}
```

- [ ] **Step 6: Wrap content in reading surface**

Use:

```tsx
{documentId && preview && !isLoading && !errorMessage ? (
  <Panel as="article" className="document-preview-page__content" aria-label="Document preview content">
    {hasContent ? (
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{preview.content}</ReactMarkdown>
    ) : (
      <p className="document-preview-page__empty">No preview content available.</p>
    )}
  </Panel>
) : null}
```

- [ ] **Step 7: Update preview CSS**

Keep preview-specific Markdown/readability styles in `document-preview.css`:

```css
.document-preview-page {
  display: grid;
  gap: 18px;
}

.document-preview-page__state {
  padding: 18px;
}

.document-preview-page__content {
  max-width: 980px;
  padding: 26px 30px;
  line-height: 1.72;
}

.document-preview-page__content h1,
.document-preview-page__content h2,
.document-preview-page__content h3 {
  line-height: 1.25;
}

.document-preview-page__content pre {
  overflow: auto;
  padding: 14px;
  border-radius: 8px;
  background: var(--panel-bg-elevated);
  border: 1px solid var(--panel-border);
}

.document-preview-page__content code {
  font-family: "Cascadia Code", "Consolas", monospace;
}
```

- [ ] **Step 8: Run focused tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/document-preview/DocumentPreviewPage.test.tsx --run
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/LightRAGNet.React/src/features/document-preview src/LightRAGNet.React/src/shared/styles/app.css src/LightRAGNet.React/tests/integration/features/document-preview/DocumentPreviewPage.test.tsx
git commit -m "feat: redesign document preview workspace"
```

---

### Task 7: Verify Untouched Pages Still Route And Render

**Files:**

- Modify: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`
- Test only: existing untouched page tests

- [ ] **Step 1: Add route smoke assertions**

In `AppLayout.test.tsx`, add a test that does not assert redesigned content, only that untouched pages render through the shell:

```tsx
it('keeps untouched feature routes mounted inside the new shell', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify({}), {
      status: 200,
      headers: { 'Content-Type': 'application/json' }
    })
  );

  window.history.pushState({}, '', '/graph-view');
  const { unmount } = render(<App />);
  expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
  expect(screen.getByText('Knowledge Graph')).toBeInTheDocument();
  unmount();

  window.history.pushState({}, '', '/system-status');
  render(<App />);
  expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
  expect(screen.getByText('System Status')).toBeInTheDocument();
});
```

If mocked API response shape causes a feature page to fail before shell assertions, use the existing page-specific mock data from that feature's current test file.

- [ ] **Step 2: Run untouched route tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/features/rag-chat/RagChatWorkbench.test.tsx tests/integration/features/graph-workbench/GraphWorkbenchMigration.test.tsx tests/integration/features/system-status/SystemStatusWorkbench.test.tsx tests/integration/features/cache-management/CacheManagementWorkbench.test.tsx --run
```

Expected: PASS. Failures caused only by global shell CSS overflow should be fixed in shared CSS without changing feature controls.

- [ ] **Step 3: Run AppLayout test**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx --run
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx src/LightRAGNet.React/src/shared/styles/app.css
git commit -m "test: cover untouched routes in light shell"
```

---

### Task 8: Full Verification And Visual Evidence

**Files:**

- No source changes expected.
- Create screenshots only if the execution session needs durable review artifacts under `docs/superpowers/visuals/anthropic-light-workbench/implementation-checks/`.

- [ ] **Step 1: Run full React tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run
```

Expected: PASS.

- [ ] **Step 2: Run production build**

Run:

```powershell
npm run build --prefix src/LightRAGNet.React
```

Expected: PASS with TypeScript and Vite build output.

- [ ] **Step 3: Start local dev server for screenshots**

Run:

```powershell
npm run dev --prefix src/LightRAGNet.React -- --host 127.0.0.1
```

Expected: Vite prints a local URL such as `http://127.0.0.1:5173/`. Keep this session running until screenshots are captured, then stop it.

- [ ] **Step 4: Capture browser screenshots**

Use the browser or Playwright skill to capture:

```text
/documents
/documents/upload
/document-preview
/graph-view
```

Expected visual evidence:

- Sidebar groups match the approved prototype.
- Brand mark is a real logo mark, not a dot.
- Sidebar footer shows SignalR state and `LightRAGNet v0.1.0`.
- Document table uses dense light workbench styling.
- Upload page uses two-column workbench layout.
- Preview page uses reading workspace styling.
- `/graph-view` still shows the existing graph implementation inside the new shell.

- [ ] **Step 5: Stop dev server**

Stop the Vite process cleanly from the terminal session.

- [ ] **Step 6: Final git status**

Run:

```powershell
git status --short
```

Expected: only intentional source/test/style changes and any chosen screenshot artifacts remain.

- [ ] **Step 7: Commit final verification artifacts if present**

If screenshot artifacts were intentionally kept:

```powershell
git add docs/superpowers/visuals/anthropic-light-workbench/implementation-checks
git commit -m "docs: add light shell verification screenshots"
```

If screenshots were only temporary, do not commit them.

---

## Self-Review Checklist

- Spec coverage:
  - Shell rebuild covered by Tasks 1 and 2.
  - Standard icons covered by Tasks 1 through 4.
  - Elevation and overlays covered by Tasks 2 through 6.
  - Documents page covered by Task 4.
  - Upload page covered by Task 5.
  - Full document preview covered by Task 6.
  - Untouched page policy covered by Task 7.
  - Verification covered by Task 8.
- Scope check:
  - RAG Chat, Knowledge Graph, System Status, and Cache Management are not redesigned.
  - Backend API contracts are not changed.
  - Theme switching is not introduced.
- Type consistency:
  - Navigation uses `NavigationIconId` in `navigation.ts` and the same keys in `AppLayout.tsx`.
  - SignalR state still uses `RagTaskHubConnectionState`.
  - Shared primitive class names are stable across tests and CSS.
- Execution boundary:
  - Implement in an isolated worktree when development starts.
  - Keep each task reviewable before moving to the next task.

