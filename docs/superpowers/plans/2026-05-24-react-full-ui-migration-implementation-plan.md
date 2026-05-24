# React Full UI Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move all Blazor-hosted React UI into `src/LightRAGNet.React`, establish the standalone React shell and `dark-ops` UI framework, and preserve existing React island behavior unless the approved design explicitly requires local redesign.

**Architecture:** `LightRAGNet.Server` remains the API, SignalR, and preview backend. `src/LightRAGNet.React` becomes the complete React frontend with routes for RAG Chat, Documents, Upload, Knowledge Graph, System Status, Cache Management, and Document Preview. Existing Blazor-hosted React code is migrated directly first, with minimal route/import/theme adaptations; Documents and Upload are redesigned to match the approved shell and table style.

**Tech Stack:** React 19, TypeScript, Vite, Vitest, Testing Library, SignalR JavaScript client, React Markdown, Sigma/Graphology, lucide-react, CSS custom properties, ASP.NET Core Server APIs.

---

## Source Inputs

- Spec: `docs/superpowers/specs/2026-05-24-react-full-ui-migration-design.md`
- Visual: `docs/superpowers/visuals/2026-05-24-react-full-ui-migration-concepts.html`
- Existing standalone React app: `src/LightRAGNet.React`
- Existing Blazor-hosted React islands: `src/LightRAGNet.Web/ClientApp`

## Migration Rules

- Existing React pages under Blazor are migrated directly by default.
- Preserve existing components, API clients, types, stores, tests, CSS classes, buttons, controls, labels, dialogs, and interaction semantics.
- Only adapt route mounting, import aliases, CSS imports, shell sizing, API base delivery, test paths, and build config.
- Documents and Upload are the exception: they already live in standalone React but need visual and table redesign.
- Knowledge Graph is strict direct migration: no button/control/content redesign.
- RAG Chat must keep every current setting: `Mode`, `Response`, `Streaming`, `References`, `Rerank`, `TopK`, `ChunkTopK`, `High keywords`, `Low keywords`, `Debug output`.
- Page title chips must show real state only. Do not add static marketing chips.

## File Structure And Responsibilities

### Standalone React App

- Modify: `src/LightRAGNet.React/package.json`
  - add dependencies that exist in Blazor-hosted React and are needed after migration.
- Modify: `src/LightRAGNet.React/vite.config.ts`
  - keep alias, Vitest, SignalR warning suppression, and dev port.
- Modify: `src/LightRAGNet.React/src/main.tsx`
  - bootstrap app and shared styles.
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
  - top-level route composition and shared SignalR state.
- Modify: `src/LightRAGNet.React/src/app/AppLayout.tsx`
  - new shell: top bar, left navigation, main content, bottom SignalR status.
- Modify: `src/LightRAGNet.React/src/app/router.tsx`
  - route table for all migrated pages.
- Create: `src/LightRAGNet.React/src/app/navigation.ts`
  - typed navigation metadata.
- Create: `src/LightRAGNet.React/src/app/ClearAllDataAction.tsx`
  - shell-level clear-all command and confirmation.
- Create: `src/LightRAGNet.React/src/shared/styles/theme.css`
  - migrated `dark-ops` tokens from Blazor ClientApp.
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
  - shell, common panel/table/button/tabs/status styles.
- Create: `src/LightRAGNet.React/src/shared/components/PageHeader.tsx`
  - title, state chips, toolbar surface.
- Create: `src/LightRAGNet.React/src/shared/components/PageTabs.tsx`
  - tab bar used by Documents and diagnostics-style pages.
- Create: `src/LightRAGNet.React/src/shared/components/DataTable.tsx`
  - dense table wrapper classes, not a generic table engine.
- Create: `src/LightRAGNet.React/src/shared/components/StatusPill.tsx`
  - status tone rendering.
- Modify: `src/LightRAGNet.React/src/api/http.ts`
  - preserve existing helpers and add generic no-content/error handling if needed.
- Create: `src/LightRAGNet.React/src/api/systemStatusApi.ts`
  - direct migration from Blazor ClientApp.
- Create: `src/LightRAGNet.React/src/api/cacheManagementApi.ts`
  - direct migration from Blazor ClientApp.
- Create: `src/LightRAGNet.React/src/api/graphApi.ts`
  - direct migration from Blazor ClientApp.
- Create: `src/LightRAGNet.React/src/api/ragChatApi.ts`
  - direct migration from Blazor ClientApp.
- Create: `src/LightRAGNet.React/src/types/*.ts`
  - direct migration of `cacheManagement.ts`, `graph.ts`, and `ragChat.ts`.

### Migrated Feature Areas

- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
  - redesign around shell/table standard while preserving workflow behavior.
- Modify: `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`
  - redesign around dark upload workbench.
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`
  - route-aware preview entry or transition wrapper.
- Create: `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`
  - safe preview route.
- Create: `src/LightRAGNet.React/src/features/rag-chat/*`
  - direct migration from `src/LightRAGNet.Web/ClientApp/src/rag-chat`.
- Create: `src/LightRAGNet.React/src/features/graph-workbench/*`
  - direct migration from `src/LightRAGNet.Web/ClientApp/src/graph-workbench` and `src/components/graph`.
- Create: `src/LightRAGNet.React/src/stores/*`
  - direct migration of graph stores.
- Create: `src/LightRAGNet.React/src/features/system-status/*`
  - direct migration from `src/LightRAGNet.Web/ClientApp/src/system-status`.
- Create: `src/LightRAGNet.React/src/features/cache-management/*`
  - direct migration from `src/LightRAGNet.Web/ClientApp/src/cache-management`.

### Tests

- Create/modify under `src/LightRAGNet.React/tests/unit/**`
- Create/modify under `src/LightRAGNet.React/tests/integration/**`
- Do not create React test files under `src/LightRAGNet.React/src`.
- Keep existing `.NET` tests until Blazor removal phase.

---

### Task 1: Establish Shell, Route Table, And `dark-ops` Theme

**Files:**
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Modify: `src/LightRAGNet.React/src/app/AppLayout.tsx`
- Modify: `src/LightRAGNet.React/src/app/router.tsx`
- Create: `src/LightRAGNet.React/src/app/navigation.ts`
- Create: `src/LightRAGNet.React/src/app/ClearAllDataAction.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/theme.css`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Create: `src/LightRAGNet.React/src/shared/components/PageHeader.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/PageTabs.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/StatusPill.tsx`
- Test: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`
- Test: `src/LightRAGNet.React/tests/unit/app/router.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`

- [ ] **Step 1: Write shell navigation test**

Create or replace `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppLayout } from "@/app/AppLayout";

describe("AppLayout", () => {
  it("renders the approved standalone React shell navigation", () => {
    render(
      <AppLayout currentPath="/documents" connectionStatus="Connected">
        <div>Documents content</div>
      </AppLayout>
    );

    expect(screen.getByRole("banner")).toHaveTextContent("LightRAGNet");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("RAG Chat");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("Documents");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("Upload");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("Knowledge Graph");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("System Status");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("Cache Management");
    expect(screen.getByRole("navigation", { name: "Primary" })).toHaveTextContent("Document Preview");
    expect(screen.getByRole("contentinfo")).toHaveTextContent("SignalR Connected");
    expect(screen.getByText("Documents content")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Write route table test**

Create `src/LightRAGNet.React/tests/unit/app/router.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { resolveRoute } from "@/app/router";

describe("resolveRoute", () => {
  it("maps approved frontend routes", () => {
    expect(resolveRoute("/").id).toBe("rag-chat");
    expect(resolveRoute("/documents").id).toBe("documents");
    expect(resolveRoute("/documents/upload").id).toBe("upload");
    expect(resolveRoute("/graph-view").id).toBe("graph");
    expect(resolveRoute("/system-status").id).toBe("system-status");
    expect(resolveRoute("/cache-management").id).toBe("cache-management");
    expect(resolveRoute("/document-preview").id).toBe("document-preview");
    expect(resolveRoute("/document-preview/42").id).toBe("document-preview");
  });

  it("falls back to RAG Chat for unknown routes", () => {
    expect(resolveRoute("/missing").id).toBe("rag-chat");
  });
});
```

- [ ] **Step 3: Write theme token test**

Create `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`:

```ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const themeCss = readFileSync(resolve(process.cwd(), "src/shared/styles/theme.css"), "utf8");
const appCss = readFileSync(resolve(process.cwd(), "src/shared/styles/app.css"), "utf8");

describe("standalone dark-ops theme", () => {
  it("keeps the shared dark-ops tokens", () => {
    [
      "--app-bg",
      "--panel-bg",
      "--panel-bg-elevated",
      "--panel-border",
      "--text-primary",
      "--text-secondary",
      "--accent",
      "--danger",
      "--warning",
      "--success",
      "--control-bg",
      "--control-border",
      "--shadow-panel",
      "--shadow-popover",
      "--shadow-modal",
      "--scrim",
      "--radius-panel",
      "--radius-control"
    ].forEach((token) => expect(themeCss).toContain(token));
  });

  it("defines shell, table, tabs and status surfaces", () => {
    [".app-shell", ".app-topbar", ".app-sidebar", ".app-statusbar", ".lrn-page-tabs", ".lrn-data-table", ".lrn-status-pill", ".lrn-scrim", ".lrn-drawer", ".lrn-modal"].forEach((selector) =>
      expect(appCss).toContain(selector)
    );
  });
});
```

- [ ] **Step 4: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx tests/unit/app/router.test.ts tests/unit/shared/styles/theme.test.ts
```

Expected: tests fail because the new shell, route table, and shared classes are not implemented.

- [ ] **Step 5: Implement navigation metadata**

Create `src/LightRAGNet.React/src/app/navigation.ts`:

```ts
export type NavigationItem = {
  id: string;
  label: string;
  href: string;
  section: "workbenches" | "operations";
  shortLabel: string;
};

export const navigationItems: NavigationItem[] = [
  { id: "rag-chat", label: "RAG Chat", href: "/", section: "workbenches", shortLabel: "C" },
  { id: "documents", label: "Documents", href: "/documents", section: "workbenches", shortLabel: "D" },
  { id: "upload", label: "Upload", href: "/documents/upload", section: "workbenches", shortLabel: "U" },
  { id: "graph", label: "Knowledge Graph", href: "/graph-view", section: "workbenches", shortLabel: "G" },
  { id: "system-status", label: "System Status", href: "/system-status", section: "operations", shortLabel: "S" },
  { id: "cache-management", label: "Cache Management", href: "/cache-management", section: "operations", shortLabel: "K" },
  { id: "document-preview", label: "Document Preview", href: "/document-preview", section: "operations", shortLabel: "P" }
];
```

- [ ] **Step 6: Implement route table**

Replace `src/LightRAGNet.React/src/app/router.tsx` with:

```tsx
export type RouteId =
  | "rag-chat"
  | "documents"
  | "upload"
  | "graph"
  | "system-status"
  | "cache-management"
  | "document-preview";

export type AppRoute = {
  id: RouteId;
  path: string;
  title: string;
};

const routeTable: AppRoute[] = [
  { id: "rag-chat", path: "/", title: "RAG Chat" },
  { id: "documents", path: "/documents", title: "Documents" },
  { id: "upload", path: "/documents/upload", title: "Upload Document" },
  { id: "graph", path: "/graph-view", title: "Knowledge Graph" },
  { id: "system-status", path: "/system-status", title: "System Status" },
  { id: "cache-management", path: "/cache-management", title: "Cache Management" },
  { id: "document-preview", path: "/document-preview", title: "Document Preview" }
];

export function resolveRoute(pathname: string = window.location.pathname): AppRoute {
  if (pathname === "/document-preview" || pathname.startsWith("/document-preview/")) {
    return routeTable.find((route) => route.id === "document-preview")!;
  }

  return routeTable.find((route) => route.path === pathname) ?? routeTable[0];
}
```

- [ ] **Step 7: Implement shared components**

Create `src/LightRAGNet.React/src/shared/components/PageHeader.tsx`:

```tsx
import type { ReactNode } from "react";

type PageHeaderProps = {
  title: string;
  chips?: string[];
  actions?: ReactNode;
};

export function PageHeader({ title, chips = [], actions }: PageHeaderProps) {
  return (
    <header className="lrn-page-head">
      <div>
        <h1>{title}</h1>
        {chips.length > 0 ? (
          <div className="lrn-page-meta">
            {chips.map((chip) => (
              <span className="lrn-chip" key={chip}>{chip}</span>
            ))}
          </div>
        ) : null}
      </div>
      {actions ? <div className="lrn-toolbar">{actions}</div> : null}
    </header>
  );
}
```

Create `src/LightRAGNet.React/src/shared/components/PageTabs.tsx`:

```tsx
export type PageTab = {
  id: string;
  label: string;
};

type PageTabsProps = {
  tabs: PageTab[];
  activeId: string;
  onSelect: (id: string) => void;
  label: string;
};

export function PageTabs({ tabs, activeId, onSelect, label }: PageTabsProps) {
  return (
    <nav className="lrn-page-tabs" aria-label={label}>
      {tabs.map((tab) => (
        <button
          className={tab.id === activeId ? "lrn-page-tab lrn-page-tab--active" : "lrn-page-tab"}
          key={tab.id}
          type="button"
          onClick={() => onSelect(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}
```

Create `src/LightRAGNet.React/src/shared/components/StatusPill.tsx`:

```tsx
export type StatusTone = "default" | "info" | "good" | "warn" | "bad";

type StatusPillProps = {
  tone?: StatusTone;
  children: string;
};

export function StatusPill({ tone = "default", children }: StatusPillProps) {
  return <span className={`lrn-status-pill lrn-status-pill--${tone}`}>{children}</span>;
}
```

- [ ] **Step 8: Implement shell and styles**

Replace `src/LightRAGNet.React/src/app/AppLayout.tsx`:

```tsx
import type { ReactNode } from "react";
import { navigationItems } from "./navigation";
import { ClearAllDataAction } from "./ClearAllDataAction";

type AppLayoutProps = {
  currentPath: string;
  connectionStatus: "Connected" | "Disconnected" | "Reconnecting" | "ServerNotStarted" | "Unknown";
  children: ReactNode;
};

export function AppLayout({ currentPath, connectionStatus, children }: AppLayoutProps) {
  return (
    <div className="app-shell">
      <header className="app-topbar" role="banner">
        <a className="app-brand" href="/">
          <span className="app-brand__mark">L</span>
          <span>LightRAGNet</span>
        </a>
        <span className="app-topbar__meta">React UI Frontend</span>
        <div className="app-topbar__actions">
          <ClearAllDataAction />
        </div>
      </header>

      <aside className="app-sidebar">
        <nav className="app-nav" aria-label="Primary">
          <div className="app-nav__section-label">Workbenches</div>
          {navigationItems.filter((item) => item.section === "workbenches").map((item) => (
            <a
              aria-current={isActiveRoute(currentPath, item.href) ? "page" : undefined}
              className="app-nav__item"
              href={item.href}
              key={item.id}
            >
              <span className="app-nav__icon">{item.shortLabel}</span>
              <span>{item.label}</span>
            </a>
          ))}
          <div className="app-nav__section-label">Operations</div>
          {navigationItems.filter((item) => item.section === "operations").map((item) => (
            <a
              aria-current={isActiveRoute(currentPath, item.href) ? "page" : undefined}
              className="app-nav__item"
              href={item.href}
              key={item.id}
            >
              <span className="app-nav__icon">{item.shortLabel}</span>
              <span>{item.label}</span>
            </a>
          ))}
        </nav>
      </aside>

      <main className="app-main">{children}</main>

      <footer className="app-statusbar">
        <span className="lrn-status-pill lrn-status-pill--good">{getConnectionText(connectionStatus)}</span>
      </footer>
    </div>
  );
}

function isActiveRoute(currentPath: string, href: string): boolean {
  if (href === "/") {
    return currentPath === "/";
  }

  return currentPath === href || currentPath.startsWith(`${href}/`);
}

function getConnectionText(status: AppLayoutProps["connectionStatus"]): string {
  return status === "Connected" ? "SignalR Connected" : `SignalR ${status}`;
}
```

Create `src/LightRAGNet.React/src/app/ClearAllDataAction.tsx`:

```tsx
export function ClearAllDataAction() {
  return (
    <button className="lrn-button lrn-button--danger" type="button">
      Clear All Data
    </button>
  );
}
```

Replace `src/LightRAGNet.React/src/shared/styles/theme.css` with the token set from `src/LightRAGNet.Web/ClientApp/src/styles/theme.css`, then extend it with the shared elevation tokens:

```css
:root {
  --shadow-popover: 0 22px 56px rgba(0, 0, 0, 0.34);
  --shadow-modal: 0 30px 90px rgba(0, 0, 0, 0.48);
  --scrim: rgba(2, 6, 12, 0.58);
}
```

Update `src/LightRAGNet.React/src/shared/styles/app.css` to include shell/table/tabs/status classes:

```css
@import "./theme.css";

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  min-width: 320px;
  min-height: 100vh;
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
  letter-spacing: 0;
}

button,
input,
select,
textarea {
  font: inherit;
}

a {
  color: inherit;
  text-decoration: none;
}

.app-shell {
  min-height: 100vh;
  display: grid;
  grid-template-columns: 238px minmax(0, 1fr);
  grid-template-rows: 56px minmax(0, 1fr) 42px;
  background: var(--app-bg);
}

.app-topbar {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 12px;
  border-bottom: 1px solid var(--panel-border);
  background: #111821;
  padding: 0 14px;
}

.app-brand {
  min-width: 210px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-weight: 780;
}

.app-brand__mark {
  width: 32px;
  height: 32px;
  display: grid;
  place-items: center;
  border: 1px solid var(--accent-border);
  border-radius: 7px;
  background: var(--accent-soft);
  color: #c7f3ff;
}

.app-topbar__meta {
  color: var(--text-secondary);
  font-size: 13px;
}

.app-topbar__actions {
  margin-left: auto;
}

.app-sidebar {
  grid-row: 2 / 4;
  overflow: auto;
  border-right: 1px solid var(--panel-border);
  background: #0f151d;
  padding: 12px;
}

.app-nav {
  display: grid;
  gap: 6px;
}

.app-nav__section-label {
  margin: 14px 8px 4px;
  color: var(--text-muted);
  font-size: 11px;
  font-weight: 850;
  text-transform: uppercase;
}

.app-nav__item {
  min-height: 38px;
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  border: 1px solid transparent;
  border-radius: 7px;
  color: var(--text-secondary);
  padding: 0 10px;
  font-size: 13px;
  font-weight: 750;
}

.app-nav__item[aria-current="page"] {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: #d7f7ff;
}

.app-main {
  min-width: 0;
  overflow: auto;
  padding: 18px 20px 22px;
}

.app-statusbar {
  grid-column: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  border-top: 1px solid var(--panel-border);
  background: #111821;
}

.lrn-page-head {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 18px;
  align-items: start;
  margin-bottom: 14px;
}

.lrn-page-head h1 {
  margin: 0 0 7px;
  font-size: 26px;
  line-height: 1.2;
}

.lrn-page-meta,
.lrn-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.lrn-toolbar {
  justify-content: flex-end;
}

.lrn-panel {
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
}

.lrn-scrim {
  position: fixed;
  inset: 0;
  background: var(--scrim);
  backdrop-filter: blur(3px);
  z-index: 40;
}

.lrn-drawer,
.lrn-modal {
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg-elevated);
  box-shadow: var(--shadow-modal);
  z-index: 50;
}

.lrn-drawer {
  position: fixed;
  top: 72px;
  right: 18px;
  bottom: 42px;
  width: min(720px, calc(100vw - 36px));
}

.lrn-modal {
  max-width: min(760px, calc(100vw - 36px));
}

.lrn-button,
.lrn-icon-button,
.lrn-select,
.lrn-input,
.lrn-textarea {
  min-height: 34px;
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  background: var(--control-bg);
  color: var(--text-primary);
  padding: 0 11px;
  font-size: 13px;
  font-weight: 750;
}

.lrn-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  cursor: pointer;
}

.lrn-button--accent {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: #c7f3ff;
}

.lrn-button--danger {
  border-color: rgba(255, 107, 107, .45);
  background: var(--danger-soft);
  color: #ffd5d5;
}

.lrn-chip,
.lrn-status-pill {
  min-height: 26px;
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--panel-border);
  border-radius: 999px;
  background: var(--panel-bg);
  color: var(--text-secondary);
  padding: 0 10px;
  font-size: 12px;
  font-weight: 750;
  white-space: nowrap;
}

.lrn-status-pill--good {
  border-color: rgba(123, 216, 143, .36);
  background: var(--success-soft);
  color: #dff7e5;
}

.lrn-status-pill--info {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: #c7f3ff;
}

.lrn-status-pill--warn {
  border-color: rgba(246, 200, 95, .38);
  background: var(--warning-soft);
  color: #ffe6ad;
}

.lrn-status-pill--bad {
  border-color: rgba(255, 107, 107, .42);
  background: var(--danger-soft);
  color: #ffd5d5;
}

.lrn-page-tabs {
  display: flex;
  gap: 4px;
  min-width: 0;
  overflow-x: auto;
  border-bottom: 1px solid var(--panel-border);
  margin-bottom: 14px;
}

.lrn-page-tab {
  min-height: 36px;
  border: 1px solid transparent;
  border-bottom: 0;
  border-radius: 6px 6px 0 0;
  background: transparent;
  color: var(--text-secondary);
  padding: 0 13px;
  font-weight: 780;
  white-space: nowrap;
  cursor: pointer;
}

.lrn-page-tab--active {
  border-color: var(--panel-border);
  background: var(--panel-bg-elevated);
  color: var(--text-primary);
}

.lrn-data-table-wrap {
  overflow-x: auto;
}

.lrn-data-table {
  width: 100%;
  min-width: 900px;
  border-collapse: collapse;
}

.lrn-data-table th,
.lrn-data-table td {
  border-bottom: 1px solid #263140;
  padding: 12px 15px;
  text-align: left;
  vertical-align: middle;
  font-size: 13px;
}

.lrn-data-table th {
  background: #111922;
  color: #7c8898;
  font-size: 12px;
  font-weight: 850;
  text-transform: uppercase;
}
```

- [ ] **Step 9: Wire App shell**

Update `src/LightRAGNet.React/src/app/App.tsx`:

```tsx
import { getApiBase } from "@/api/http";
import { AppLayout } from "./AppLayout";
import { resolveRoute } from "./router";
import "@/shared/styles/app.css";

export function App() {
  const route = resolveRoute();
  const apiBase = getApiBase();

  return (
    <AppLayout currentPath={window.location.pathname} connectionStatus="Connected">
      <section className="lrn-panel" style={{ padding: 18 }}>
        <h1>{route.title}</h1>
        <p>API base: {apiBase}</p>
      </section>
    </AppLayout>
  );
}
```

- [ ] **Step 10: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx tests/unit/app/router.test.ts tests/unit/shared/styles/theme.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add standalone react shell"
```

---

### Task 2: Migrate Shared Types And API Clients From Blazor React

**Files:**
- Copy/create: `src/LightRAGNet.React/src/types/ragChat.ts`
- Copy/create: `src/LightRAGNet.React/src/types/graph.ts`
- Copy/create: `src/LightRAGNet.React/src/types/cacheManagement.ts`
- Copy/create: `src/LightRAGNet.React/src/api/ragChatApi.ts`
- Copy/create: `src/LightRAGNet.React/src/api/graphApi.ts`
- Copy/create: `src/LightRAGNet.React/src/api/systemStatusApi.ts`
- Copy/create: `src/LightRAGNet.React/src/api/cacheManagementApi.ts`
- Modify: `src/LightRAGNet.React/src/api/http.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/ragChatApi.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/graphApi.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/systemStatusApi.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/cacheManagementApi.test.ts`

- [ ] **Step 1: Copy existing API and type tests**

Copy these tests from Blazor ClientApp into matching standalone React test paths:

```powershell
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\ragChatApi.test.ts src\LightRAGNet.React\tests\unit\api\ragChatApi.test.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\graphApi.test.ts src\LightRAGNet.React\tests\unit\api\graphApi.test.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\systemStatusApi.test.ts src\LightRAGNet.React\tests\unit\api\systemStatusApi.test.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\cacheManagementApi.test.ts src\LightRAGNet.React\tests\unit\api\cacheManagementApi.test.ts
```

In each copied test, change imports from Blazor-relative paths to standalone alias imports. Example:

```ts
import { queryRagStream } from "@/api/ragChatApi";
```

- [ ] **Step 2: Run copied tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/ragChatApi.test.ts tests/unit/api/graphApi.test.ts tests/unit/api/systemStatusApi.test.ts tests/unit/api/cacheManagementApi.test.ts
```

Expected: tests fail until API clients and types are copied.

- [ ] **Step 3: Copy type files**

Run:

```powershell
Copy-Item src\LightRAGNet.Web\ClientApp\src\types\ragChat.ts src\LightRAGNet.React\src\types\ragChat.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\types\graph.ts src\LightRAGNet.React\src\types\graph.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\types\cacheManagement.ts src\LightRAGNet.React\src\types\cacheManagement.ts
```

- [ ] **Step 4: Copy API clients**

Run:

```powershell
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\ragChatApi.ts src\LightRAGNet.React\src\api\ragChatApi.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\graphApi.ts src\LightRAGNet.React\src\api\graphApi.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\systemStatusApi.ts src\LightRAGNet.React\src\api\systemStatusApi.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\api\cacheManagementApi.ts src\LightRAGNet.React\src\api\cacheManagementApi.ts
```

- [ ] **Step 5: Normalize imports**

In copied files, replace imports that assume Blazor ClientApp relative paths with standalone paths:

```ts
import type { RagQueryDataResponse, RagQueryEvent, RagQueryRequest } from "@/types/ragChat";
import type { GraphQueryResponse } from "@/types/graph";
import type { CacheOverviewResponse } from "@/types/cacheManagement";
```

Keep endpoint URLs and response parsing behavior unchanged.

- [ ] **Step 6: Ensure HTTP helper remains compatible**

Review `src/LightRAGNet.React/src/api/http.ts`. It must expose `buildUrl`, `getApiBase`, and JSON error parsing compatible with both existing documents API and copied clients.

Expected helper shape:

```ts
export function getApiBase(): string {
  return import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5261";
}

export function buildUrl(apiBase: string, path: string): string {
  return `${apiBase.replace(/\/+$/, "")}/${path.replace(/^\/+/, "")}`;
}

export async function readJson<T>(response: Response): Promise<T> {
  if (response.ok) {
    return await response.json() as T;
  }

  throw new Error(await readErrorMessage(response));
}

export async function readErrorMessage(response: Response): Promise<string> {
  const body = await response.text();

  if (!body) {
    return response.statusText || `HTTP ${response.status}`;
  }

  try {
    const parsed = JSON.parse(body) as Record<string, unknown>;
    return String(parsed.message ?? parsed.error ?? parsed.title ?? response.statusText ?? body);
  } catch {
    return response.statusText || body;
  }
}
```

- [ ] **Step 7: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/ragChatApi.test.ts tests/unit/api/graphApi.test.ts tests/unit/api/systemStatusApi.test.ts tests/unit/api/cacheManagementApi.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: migrate react api clients"
```

---

### Task 3: Redesign Documents And Upload On The New Shell

**Files:**
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/documentStatus.ts`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsParityChecklist.test.ts`

- [ ] **Step 1: Update Documents visual contract test**

Modify `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx` to assert shell-compatible page structure:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DocumentsPage } from "@/features/documents/DocumentsPage";

const documentsPage = {
  items: [{
    id: 1,
    fileName: "system-architecture.md",
    fileSize: 2048,
    uploadTime: "2026-05-24T12:00:00Z",
    isInRagSystem: true,
    ragStatus: "Completed",
    ragProgress: 100,
    ragCurrentStage: null,
    ragRetryCount: 0,
    ragAddedTime: "2026-05-24T12:10:00Z",
    ragErrorMessage: null,
    fileUrl: "/uploads/system-architecture.md"
  }],
  page: 1,
  pageSize: 10,
  totalCount: 1,
  totalPages: 1
};

describe("DocumentsPage", () => {
  it("renders the approved dark table workbench structure", async () => {
    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={vi.fn().mockResolvedValue(documentsPage)} />);

    expect(await screen.findByRole("heading", { name: "Documents" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Document status views" })).toHaveTextContent("All Documents");
    expect(screen.getByRole("navigation", { name: "Document status views" })).toHaveTextContent("Processing");
    expect(screen.getByRole("table", { name: "Document lifecycle" })).toBeInTheDocument();
    expect(screen.getByText("system-architecture.md")).toBeInTheDocument();
    expect(screen.getByText("Completed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /View/i })).toBeInTheDocument();
  });

  it("opens document preview in an elevated drawer without leaving the list", async () => {
    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={vi.fn().mockResolvedValue(documentsPage)} />);

    await screen.findByText("system-architecture.md");
    await userEvent.click(screen.getByRole("button", { name: /View/i }));

    expect(screen.getByRole("dialog", { name: /Preview system-architecture.md/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close preview" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open full preview" })).toHaveAttribute("href", "/document-preview/1");
    expect(document.querySelector(".lrn-scrim")).not.toBeNull();
  });
});
```

- [ ] **Step 2: Update Upload visual contract test**

Modify `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { UploadDocumentPage } from "@/features/documents/UploadDocumentPage";

describe("UploadDocumentPage", () => {
  it("renders as a dark upload workbench instead of a temporary card", () => {
    render(<UploadDocumentPage apiBase="http://localhost:5261" />);

    expect(screen.getByRole("heading", { name: "Upload Document" })).toBeInTheDocument();
    expect(screen.getByText(/Drop documents here|Choose documents/i)).toBeInTheDocument();
    expect(screen.getByText(/.md, .markdown, .pdf, .docx/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Upload/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx tests/integration/features/documents/UploadDocumentPage.test.tsx
```

Expected: tests fail until the approved visual structure is implemented.

- [ ] **Step 4: Wire Documents and Upload routes in App**

Update `src/LightRAGNet.React/src/app/App.tsx` route rendering:

```tsx
import { getApiBase } from "@/api/http";
import { DocumentsPage } from "@/features/documents/DocumentsPage";
import { UploadDocumentPage } from "@/features/documents/UploadDocumentPage";
import { AppLayout } from "./AppLayout";
import { resolveRoute } from "./router";
import "@/shared/styles/app.css";

export function App() {
  const route = resolveRoute();
  const apiBase = getApiBase();

  return (
    <AppLayout currentPath={window.location.pathname} connectionStatus="Connected">
      {route.id === "upload" ? <UploadDocumentPage apiBase={apiBase} /> : null}
      {route.id === "documents" ? <DocumentsPage apiBase={apiBase} /> : null}
      {route.id !== "upload" && route.id !== "documents" ? (
        <section className="lrn-panel" style={{ padding: 18 }}>
          <h1>{route.title}</h1>
        </section>
      ) : null}
    </AppLayout>
  );
}
```

- [ ] **Step 5: Redesign Documents with shared shell components**

In `DocumentsPage.tsx`, keep all existing data loading, actions, SignalR subscriptions, refresh policy, and status helpers. Replace only the rendered structure and class names:

```tsx
<section className="documents-workbench">
  <PageHeader
    title="Documents"
    chips={["Workspace _", "SignalR task updates", "Markdown / PDF / DOCX"]}
    actions={
      <>
        <select className="lrn-select" aria-label="RAG status filter" value={status} onChange={handleStatusChange}>
          <option value="">All statuses</option>
          {statusOptions.map((option) => <option key={option} value={option}>{option}</option>)}
        </select>
        <button className="lrn-button" type="button" onClick={refreshNow}>Refresh</button>
        <a className="lrn-button lrn-button--accent" href="/documents/upload">Upload Document</a>
      </>
    }
  />

  <PageTabs
    label="Document status views"
    activeId={status || "all"}
    onSelect={(id) => {
      setStatus(id === "all" ? "" : id);
      setPage(1);
    }}
    tabs={[
      { id: "all", label: "All Documents" },
      { id: "Queued", label: "Queued" },
      { id: "Processing", label: "Processing" },
      { id: "Completed", label: "Completed" },
      { id: "Failed", label: "Failed" },
      { id: "Cancelled", label: "Cancelled" }
    ]}
  />

  <div className="documents-summary-grid">
    <SummaryCard label="Total" value={String(totalCount)} description="Documents tracked by server metadata." />
    <SummaryCard label="Current Page" value={String(documents.length)} description="Rows loaded from the current filter." />
    <SummaryCard label="Processing" value={String(documents.filter((x) => x.ragStatus === "Processing").length)} description="Active conversion or indexing rows." />
    <SummaryCard label="Failed" value={String(documents.filter((x) => x.ragStatus === "Failed").length)} description="Rows that can be retried." />
  </div>

  <section className="lrn-panel">
    <div className="lrn-panel__head">
      <div>
        <h2>Document Lifecycle</h2>
        <p>Review uploaded documents and their current RAG ingestion state.</p>
      </div>
      <span className="lrn-chip">Page {page} / {totalPages}</span>
    </div>

    <div className="lrn-data-table-wrap">
      <table className="lrn-data-table" aria-label="Document lifecycle">
        ...
      </table>
    </div>
  </section>

  {previewDocument ? (
    <>
      <div className="lrn-scrim" aria-hidden="true" onClick={() => setPreviewDocument(null)} />
      <DocumentPreviewPanel
        apiBase={apiBase}
        document={previewDocument}
        onClose={() => setPreviewDocument(null)}
      />
    </>
  ) : null}
</section>
```

Use `StatusPill` in the table for status rendering:

```tsx
<StatusPill tone={getDocumentStatusTone(document.ragStatus)}>{getStatusText(document)}</StatusPill>
```

The row `View` / eye action sets `previewDocument`; it does not navigate away from `/documents`. The drawer keeps the list, filter, and current page mounted behind a scrim.

Update `DocumentPreviewPanel.tsx` into a drawer-style component. Keep the existing preview rendering behavior in Task 3, then Task 7 rewires the content loading to the safe preview API:

```tsx
export function DocumentPreviewPanel({ apiBase, document, onClose }: DocumentPreviewPanelProps) {
  return (
    <aside className="lrn-drawer document-preview" role="dialog" aria-modal="true" aria-label={`Preview ${document.fileName}`}>
      <header className="document-preview__header">
        <div>
          <h2>{document.fileName}</h2>
          <p>{document.ragStatus || "Not indexed"}</p>
        </div>
        <div className="lrn-toolbar">
          <a className="lrn-button" href={`/document-preview/${document.id}`}>Open full preview</a>
          <button className="lrn-icon-button" type="button" onClick={onClose} aria-label="Close preview">Close</button>
        </div>
      </header>
      <div className="document-preview__content">...</div>
    </aside>
  );
}
```

The drawer must use `.lrn-drawer` and `.lrn-scrim` so it has visible elevation and an overlay. On narrow viewports, CSS should make it a full-screen sheet.

- [ ] **Step 6: Add document summary and tone helpers**

Add inside `DocumentsPage.tsx` or a focused helper file if the page becomes too large:

```tsx
function SummaryCard({ label, value, description }: { label: string; value: string; description: string }) {
  return (
    <article className="lrn-metric-card">
      <div>
        <small>{label}</small>
        <strong>{value}</strong>
      </div>
      <p>{description}</p>
    </article>
  );
}

function getDocumentStatusTone(status?: string | null): "default" | "info" | "good" | "warn" | "bad" {
  if (status === "Completed") {
    return "good";
  }

  if (status === "Processing" || status === "Queued" || status === "Pending" || status === "Deleting") {
    return "warn";
  }

  if (status === "Failed" || status === "DeletionFailed") {
    return "bad";
  }

  return "default";
}
```

- [ ] **Step 7: Redesign Upload with dark upload workbench**

Keep upload validation and API behavior intact. Replace the render structure with:

```tsx
<section className="upload-workbench">
  <PageHeader
    title="Upload Document"
    chips={["10 files max", "10 MB each", "Add to RAG later"]}
    actions={<a className="lrn-button" href="/documents">Back to Documents</a>}
  />

  <div className="upload-layout">
    <section className="lrn-panel">
      <div className="lrn-panel__head">
        <div>
          <h2>Batch Upload</h2>
          <p>Prepare documents for later knowledge ingestion.</p>
        </div>
      </div>
      <div className="upload-dropzone">
        <UploadCloud size={28} aria-hidden="true" />
        <label className="lrn-button lrn-button--accent">
          Choose documents
          <input ... />
        </label>
        <span>.md, .markdown, .pdf, .docx up to 10 MB each</span>
      </div>
    </section>

    <section className="lrn-panel">
      <div className="lrn-panel__head">
        <div>
          <h2>Selected Files</h2>
          <p>Files will be uploaded as one multipart batch.</p>
        </div>
        <span className="lrn-chip">{files.length} files</span>
      </div>
      ...
    </section>
  </div>
</section>
```

- [ ] **Step 8: Add shared document/upload CSS**

Append focused classes to `src/shared/styles/app.css`:

```css
.documents-summary-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 14px;
}

.lrn-metric-card {
  min-height: 106px;
  display: grid;
  align-content: space-between;
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
  padding: 14px;
}

.lrn-metric-card small {
  color: var(--text-secondary);
  font-size: 11px;
  font-weight: 850;
  text-transform: uppercase;
}

.lrn-metric-card strong {
  display: block;
  margin-top: 8px;
  font-size: 28px;
  line-height: 1;
}

.lrn-metric-card p {
  margin: 8px 0 0;
  color: var(--text-secondary);
  font-size: 12px;
  line-height: 1.45;
}

.upload-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(320px, 380px);
  gap: 14px;
}

.upload-dropzone {
  min-height: 250px;
  display: grid;
  place-items: center;
  gap: 12px;
  border: 1px dashed var(--accent-border);
  border-radius: var(--radius-panel);
  background: rgba(76, 201, 240, .07);
  color: var(--text-secondary);
  padding: 22px;
  text-align: center;
}

.upload-dropzone input {
  position: absolute;
  width: 1px;
  height: 1px;
  opacity: 0;
  pointer-events: none;
}
```

- [ ] **Step 9: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx tests/integration/features/documents/UploadDocumentPage.test.tsx tests/integration/features/documents/DocumentsParityChecklist.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "style: redesign standalone document workbench"
```

---

### Task 4: Directly Migrate RAG Chat Into Standalone React

**Files:**
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/RagChatWorkbench.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/ChatPane.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/AssistantMessage.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/QuerySettingsPanel.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/QueryDetailsDialog.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/rag-chat/ragChatSettings.ts`
- Create/modify: `src/LightRAGNet.React/src/features/rag-chat/rag-chat.css`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/rag-chat/RagChatWorkbench.test.tsx`
- Test: `src/LightRAGNet.React/tests/unit/features/rag-chat/ragChatSettings.test.ts`

- [ ] **Step 1: Copy RAG Chat tests**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\tests\integration\features\rag-chat, src\LightRAGNet.React\tests\unit\features\rag-chat
Copy-Item src\LightRAGNet.Web\ClientApp\src\rag-chat\RagChatWorkbench.test.tsx src\LightRAGNet.React\tests\integration\features\rag-chat\RagChatWorkbench.test.tsx
Copy-Item src\LightRAGNet.Web\ClientApp\src\rag-chat\ragChatSettings.test.ts src\LightRAGNet.React\tests\unit\features\rag-chat\ragChatSettings.test.ts
```

Update imports:

```ts
import { RagChatWorkbench } from "@/features/rag-chat/RagChatWorkbench";
import { buildRagQueryRequest } from "@/features/rag-chat/ragChatSettings";
```

- [ ] **Step 2: Add title chip regression test**

Add this test to `RagChatWorkbench.test.tsx`:

```tsx
it("shows only real current state chips in the page header", async () => {
  await renderWorkbench();

  const heading = screen.getByRole("heading", { name: "RAG Chat" });
  const header = heading.closest("header");

  expect(header).toHaveTextContent("Mix");
  expect(header).toHaveTextContent("Streaming");
  expect(header).not.toHaveTextContent("Message diagnostics");
  expect(header).not.toHaveTextContent("References");
});
```

- [ ] **Step 3: Add settings completeness regression test**

Add this test to `RagChatWorkbench.test.tsx`:

```tsx
it("keeps all existing query settings visible", async () => {
  await renderWorkbench();

  expect(screen.getByLabelText("Mode")).toBeInTheDocument();
  expect(screen.getByLabelText("Response")).toBeInTheDocument();
  expect(screen.getByLabelText("Streaming")).toBeInTheDocument();
  expect(screen.getByLabelText("References")).toBeInTheDocument();
  expect(screen.getByLabelText("Rerank")).toBeInTheDocument();
  expect(screen.getByLabelText("TopK")).toBeInTheDocument();
  expect(screen.getByLabelText("ChunkTopK")).toBeInTheDocument();
  expect(screen.getByLabelText("High keywords")).toBeInTheDocument();
  expect(screen.getByLabelText("Low keywords")).toBeInTheDocument();
  expect(screen.getByLabelText("Debug output")).toBeInTheDocument();
});
```

- [ ] **Step 4: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/rag-chat/RagChatWorkbench.test.tsx tests/unit/features/rag-chat/ragChatSettings.test.ts
```

Expected: tests fail until RAG Chat is copied and imports are normalized.

- [ ] **Step 5: Copy RAG Chat source**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\src\features\rag-chat
Copy-Item src\LightRAGNet.Web\ClientApp\src\rag-chat\*.tsx src\LightRAGNet.React\src\features\rag-chat\
Copy-Item src\LightRAGNet.Web\ClientApp\src\rag-chat\*.ts src\LightRAGNet.React\src\features\rag-chat\
Copy-Item src\LightRAGNet.Web\ClientApp\src\styles\rag-chat.css src\LightRAGNet.React\src\features\rag-chat\rag-chat.css
```

- [ ] **Step 6: Normalize imports**

In copied RAG Chat files:

```ts
import { queryRagStream } from "@/api/ragChatApi";
import type { ChatMessage, RagQueryReference } from "@/types/ragChat";
import "@/features/rag-chat/rag-chat.css";
```

Keep component structure and behavior from Blazor-hosted React. Do not remove settings.

- [ ] **Step 7: Adapt RAG Chat page header only**

In `RagChatWorkbench.tsx`, keep current layout and logic, but ensure header chips are real state only:

```tsx
<PageHeader
  title="RAG Chat"
  chips={[settings.mode, settings.streamResponse ? "Streaming" : "Non-stream"]}
  actions={
    <button
      className="lrn-button lrn-button--danger"
      type="button"
      disabled={isRunning || messages.length === 0}
      onClick={() => setMessages([])}
    >
      Clear History
    </button>
  }
/>
```

- [ ] **Step 8: Wire route**

Update `App.tsx`:

```tsx
import { RagChatWorkbench } from "@/features/rag-chat/RagChatWorkbench";

{route.id === "rag-chat" ? <RagChatWorkbench apiBase={apiBase} /> : null}
```

- [ ] **Step 9: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/rag-chat/RagChatWorkbench.test.tsx tests/unit/features/rag-chat/ragChatSettings.test.ts tests/unit/api/ragChatApi.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: migrate rag chat to standalone react"
```

---

### Task 5: Directly Migrate Knowledge Graph Workbench

**Files:**
- Copy/create: `src/LightRAGNet.React/src/features/graph-workbench/GraphWorkbench.tsx`
- Copy/create: `src/LightRAGNet.React/src/features/graph-workbench/graph-workbench.css`
- Copy/create: `src/LightRAGNet.React/src/components/graph/*`
- Copy/create: `src/LightRAGNet.React/src/stores/graphStore.ts`
- Copy/create: `src/LightRAGNet.React/src/stores/graphSettingsStore.ts`
- Modify: `src/LightRAGNet.React/package.json`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Test: `src/LightRAGNet.React/tests/unit/components/graph/graphologyAdapter.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/stores/graphStore.test.ts`
- Test: `src/LightRAGNet.React/tests/integration/features/graph-workbench/GraphWorkbenchMigration.test.tsx`

- [ ] **Step 1: Add graph dependencies**

Ensure `src/LightRAGNet.React/package.json` includes the graph dependencies already used by Blazor ClientApp:

```powershell
npm install --prefix src/LightRAGNet.React @react-sigma/core @react-sigma/layout-circular @react-sigma/layout-force @react-sigma/layout-forceatlas2 @react-sigma/layout-noverlap @react-sigma/layout-random @sigma/edge-curve @sigma/node-border graphology sigma
```

- [ ] **Step 2: Copy graph tests**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\tests\unit\components\graph, src\LightRAGNet.React\tests\unit\stores, src\LightRAGNet.React\tests\integration\features\graph-workbench
Copy-Item src\LightRAGNet.Web\ClientApp\src\components\graph\graphologyAdapter.test.ts src\LightRAGNet.React\tests\unit\components\graph\graphologyAdapter.test.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\components\graph\PropertiesPanel.test.ts src\LightRAGNet.React\tests\unit\components\graph\PropertiesPanel.test.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\stores\graphStore.test.ts src\LightRAGNet.React\tests\unit\stores\graphStore.test.ts
```

Normalize imports to `@/components/graph/...` and `@/stores/...`.

- [ ] **Step 3: Add migration guard test**

Create `src/LightRAGNet.React/tests/integration/features/graph-workbench/GraphWorkbenchMigration.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readGraphSource(relativePath: string): string {
  return readFileSync(resolve(process.cwd(), relativePath), "utf8");
}

describe("Graph Workbench migration", () => {
  it("preserves existing graph controls instead of redesigning them", () => {
    const source = readGraphSource("src/features/graph-workbench/GraphWorkbench.tsx");
    const controls = readGraphSource("src/components/graph/GraphViewportControls.tsx");
    const layoutControls = readGraphSource("src/components/graph/GraphLayoutControls.tsx");

    expect(source).toContain("GraphQueryControls");
    expect(source).toContain("GraphSearchBox");
    expect(source).toContain("PropertiesPanel");
    expect(controls).toContain("Zoom");
    expect(controls).toContain("Fullscreen");
    expect(layoutControls).toContain("Force Atlas");
    expect(layoutControls).toContain("Circular");
  });
});
```

- [ ] **Step 4: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/components/graph/graphologyAdapter.test.ts tests/unit/stores/graphStore.test.ts tests/integration/features/graph-workbench/GraphWorkbenchMigration.test.tsx
```

Expected: tests fail until graph source is copied.

- [ ] **Step 5: Copy graph source directly**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\src\features\graph-workbench, src\LightRAGNet.React\src\components\graph, src\LightRAGNet.React\src\stores
Copy-Item src\LightRAGNet.Web\ClientApp\src\graph-workbench\GraphWorkbench.tsx src\LightRAGNet.React\src\features\graph-workbench\GraphWorkbench.tsx
Copy-Item src\LightRAGNet.Web\ClientApp\src\styles\graph-workbench.css src\LightRAGNet.React\src\features\graph-workbench\graph-workbench.css
Copy-Item src\LightRAGNet.Web\ClientApp\src\components\graph\*.tsx src\LightRAGNet.React\src\components\graph\
Copy-Item src\LightRAGNet.Web\ClientApp\src\components\graph\*.ts src\LightRAGNet.React\src\components\graph\
Copy-Item src\LightRAGNet.Web\ClientApp\src\stores\graphStore.ts src\LightRAGNet.React\src\stores\graphStore.ts
Copy-Item src\LightRAGNet.Web\ClientApp\src\stores\graphSettingsStore.ts src\LightRAGNet.React\src\stores\graphSettingsStore.ts
```

- [ ] **Step 6: Normalize imports without changing controls**

Update copied graph files only for paths:

```ts
import type { GraphNode } from "@/types/graph";
import { getGraphData } from "@/api/graphApi";
import "@/features/graph-workbench/graph-workbench.css";
```

Do not rename buttons, remove controls, merge panels, or change graph settings semantics.

- [ ] **Step 7: Wire route**

Update `App.tsx`:

```tsx
import { GraphWorkbench } from "@/features/graph-workbench/GraphWorkbench";

{route.id === "graph" ? <GraphWorkbench apiBase={apiBase} /> : null}
```

- [ ] **Step 8: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/components/graph/graphologyAdapter.test.ts tests/unit/stores/graphStore.test.ts tests/integration/features/graph-workbench/GraphWorkbenchMigration.test.tsx tests/unit/api/graphApi.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: migrate graph workbench to standalone react"
```

---

### Task 6: Directly Migrate System Status And Cache Management

**Files:**
- Copy/create: `src/LightRAGNet.React/src/features/system-status/*`
- Copy/create: `src/LightRAGNet.React/src/features/system-status/system-status.css`
- Copy/create: `src/LightRAGNet.React/src/features/cache-management/*`
- Copy/create: `src/LightRAGNet.React/src/features/cache-management/cache-management.css`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/cache-management/CacheManagementWorkbench.test.tsx`

- [ ] **Step 1: Copy existing tests**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\tests\integration\features\system-status, src\LightRAGNet.React\tests\integration\features\cache-management
Copy-Item src\LightRAGNet.Web\ClientApp\src\cache-management\CacheManagementWorkbench.test.tsx src\LightRAGNet.React\tests\integration\features\cache-management\CacheManagementWorkbench.test.tsx
```

Create `src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

describe("SystemStatusWorkbench migration", () => {
  it("keeps server-provided health aggregation fields", () => {
    const source = readFileSync(resolve(process.cwd(), "src/features/system-status/SystemStatusWorkbench.tsx"), "utf8");

    expect(source).toContain("health.status");
    expect(source).toContain("health.fixFirst");
    expect(source).toContain("health.featureImpacts");
    expect(source).not.toContain("fixFirst =");
    expect(source).not.toContain("overallStatus");
  });
});
```

- [ ] **Step 2: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/system-status/SystemStatusWorkbench.test.tsx tests/integration/features/cache-management/CacheManagementWorkbench.test.tsx
```

Expected: tests fail until source is copied.

- [ ] **Step 3: Copy System Status directly**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\src\features\system-status
Copy-Item src\LightRAGNet.Web\ClientApp\src\system-status\*.tsx src\LightRAGNet.React\src\features\system-status\
Copy-Item src\LightRAGNet.Web\ClientApp\src\styles\system-status.css src\LightRAGNet.React\src\features\system-status\system-status.css
```

Normalize imports to:

```ts
import { getSystemHealth } from "@/api/systemStatusApi";
import "@/features/system-status/system-status.css";
```

- [ ] **Step 4: Copy Cache Management directly**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.React\src\features\cache-management
Copy-Item src\LightRAGNet.Web\ClientApp\src\cache-management\*.tsx src\LightRAGNet.React\src\features\cache-management\
Copy-Item src\LightRAGNet.Web\ClientApp\src\cache-management\*.ts src\LightRAGNet.React\src\features\cache-management\
Copy-Item src\LightRAGNet.Web\ClientApp\src\styles\cache-management.css src\LightRAGNet.React\src\features\cache-management\cache-management.css
```

Normalize imports to:

```ts
import { clearCachePlan, getCacheManagementOverview } from "@/api/cacheManagementApi";
import type { CacheClearPlanDto, CacheOverviewResponse } from "@/types/cacheManagement";
import "@/features/cache-management/cache-management.css";
```

- [ ] **Step 5: Wire routes**

Update `App.tsx`:

```tsx
import { SystemStatusWorkbench } from "@/features/system-status/SystemStatusWorkbench";
import { CacheManagementWorkbench } from "@/features/cache-management/CacheManagementWorkbench";

{route.id === "system-status" ? <SystemStatusWorkbench apiBase={apiBase} /> : null}
{route.id === "cache-management" ? <CacheManagementWorkbench apiBase={apiBase} /> : null}
```

- [ ] **Step 6: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/system-status/SystemStatusWorkbench.test.tsx tests/integration/features/cache-management/CacheManagementWorkbench.test.tsx tests/unit/api/systemStatusApi.test.ts tests/unit/api/cacheManagementApi.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: migrate operations workbenches to standalone react"
```

---

### Task 7: Add Standalone Document Preview Route

**Files:**
- Create: `src/LightRAGNet.React/src/api/documentPreviewApi.ts`
- Create: `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`
- Create: `src/LightRAGNet.React/src/features/document-preview/document-preview.css`
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`
- Modify: `src/LightRAGNet.React/src/features/rag-chat/AssistantMessage.tsx`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Test: `src/LightRAGNet.React/tests/unit/api/documentPreviewApi.test.ts`
- Test: `src/LightRAGNet.React/tests/integration/features/document-preview/DocumentPreviewPage.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentPreviewDrawer.test.tsx`

- [ ] **Step 1: Write preview API test**

Create `src/LightRAGNet.React/tests/unit/api/documentPreviewApi.test.ts`:

```ts
import { afterEach, describe, expect, it, vi } from "vitest";
import { getDocumentPreviewContent } from "@/api/documentPreviewApi";

afterEach(() => vi.restoreAllMocks());

describe("documentPreviewApi", () => {
  it("loads safe document preview content", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ contentType: "text/markdown", content: "# Preview", fileName: "preview.md" }), {
        status: 200,
        headers: { "content-type": "application/json" }
      })
    );

    await getDocumentPreviewContent("http://localhost:5261", 42);

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5261/api/document-preview/42/content", { method: "GET" });
  });
});
```

- [ ] **Step 2: Write preview page test**

Create `src/LightRAGNet.React/tests/integration/features/document-preview/DocumentPreviewPage.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DocumentPreviewPage } from "@/features/document-preview/DocumentPreviewPage";

describe("DocumentPreviewPage", () => {
  it("renders a safe empty state when no document id is selected", () => {
    render(<DocumentPreviewPage apiBase="http://localhost:5261" />);

    expect(screen.getByRole("heading", { name: "Document Preview" })).toBeInTheDocument();
    expect(screen.getByText("Open a document from Documents or a RAG Chat reference.")).toBeInTheDocument();
  });

  it("renders markdown content from the safe preview API", async () => {
    render(
      <DocumentPreviewPage
        apiBase="http://localhost:5261"
        documentId={42}
        loadPreview={vi.fn().mockResolvedValue({ contentType: "text/markdown", content: "# Preview", fileName: "preview.md" })}
      />
    );

    expect(await screen.findByRole("heading", { name: "Document Preview" })).toBeInTheDocument();
    expect(screen.getByText("preview.md")).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run tests and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/documentPreviewApi.test.ts tests/integration/features/document-preview/DocumentPreviewPage.test.tsx
```

Expected: tests fail until preview API and page exist.

- [ ] **Step 4: Implement preview API**

Create `src/LightRAGNet.React/src/api/documentPreviewApi.ts`:

```ts
import { buildUrl, readJson } from "@/api/http";

export type DocumentPreviewContent = {
  contentType: string;
  content?: string | null;
  fileName: string;
  originalUrl?: string | null;
};

export async function getDocumentPreviewContent(apiBase: string, documentId: number): Promise<DocumentPreviewContent> {
  const response = await fetch(buildUrl(apiBase, `/api/document-preview/${documentId}/content`), { method: "GET" });
  return readJson<DocumentPreviewContent>(response);
}
```

- [ ] **Step 5: Implement preview page**

Create `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`:

```tsx
import { useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { getDocumentPreviewContent, type DocumentPreviewContent } from "@/api/documentPreviewApi";
import { PageHeader } from "@/shared/components/PageHeader";
import "@/features/document-preview/document-preview.css";

type DocumentPreviewPageProps = {
  apiBase: string;
  documentId?: number;
  loadPreview?: (apiBase: string, documentId: number) => Promise<DocumentPreviewContent>;
};

export function DocumentPreviewPage({ apiBase, documentId, loadPreview = getDocumentPreviewContent }: DocumentPreviewPageProps) {
  const [content, setContent] = useState<DocumentPreviewContent | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!documentId) {
      setContent(null);
      setErrorMessage(null);
      return;
    }

    let active = true;
    loadPreview(apiBase, documentId)
      .then((nextContent) => {
        if (active) {
          setContent(nextContent);
        }
      })
      .catch((error) => {
        if (active) {
          setErrorMessage(error instanceof Error ? error.message : "Failed to load document preview.");
        }
      });

    return () => {
      active = false;
    };
  }, [apiBase, documentId, loadPreview]);

  return (
    <section className="document-preview-page">
      <PageHeader title="Document Preview" chips={content ? [content.fileName, content.contentType] : documentId ? ["Loading"] : ["No document selected"]} />
      {errorMessage ? <div className="lrn-banner lrn-banner--error">{errorMessage}</div> : null}
      {!documentId ? (
        <div className="lrn-panel document-preview-page__state">Open a document from Documents or a RAG Chat reference.</div>
      ) : null}
      {documentId && !content && !errorMessage ? <div className="lrn-panel document-preview-page__state">Loading preview</div> : null}
      {content ? (
        <article className="lrn-panel document-preview-page__content">
          {content.content ? <ReactMarkdown remarkPlugins={[remarkGfm]}>{content.content}</ReactMarkdown> : <p>No preview content returned.</p>}
        </article>
      ) : null}
    </section>
  );
}
```

Create `src/LightRAGNet.React/src/features/document-preview/document-preview.css`:

```css
.document-preview-page__state,
.document-preview-page__content {
  padding: 18px;
}

.document-preview-page__content {
  min-height: 560px;
  background: #0a0f15;
  line-height: 1.62;
}

.document-preview-page__content > :first-child {
  margin-top: 0;
}
```

- [ ] **Step 6: Wire route**

Update `App.tsx`:

```tsx
import { DocumentPreviewPage } from "@/features/document-preview/DocumentPreviewPage";

function getPreviewDocumentId(pathname: string): number | undefined {
  const idText = pathname.split("/").filter(Boolean).at(1);
  const id = Number(idText);
  return Number.isFinite(id) && id > 0 ? id : undefined;
}

{route.id === "document-preview" ? <DocumentPreviewPage apiBase={apiBase} documentId={getPreviewDocumentId(window.location.pathname)} /> : null}
```

- [ ] **Step 7: Route RAG Chat document references through the React preview page**

In `AssistantMessage.tsx`, do not generate links from `filePath`. Convert only backend-provided safe DocumentPreview URLs into same-frontend React preview routes:

```ts
function getReactDocumentPreviewHref(reference: RagQueryReference): string | null {
  if (reference.openKind !== "DocumentPreview" || !reference.previewUrl) {
    return reference.previewUrl ?? null;
  }

  try {
    const url = new URL(reference.previewUrl, window.location.origin);
    const match = url.pathname.match(/\/document-preview\/(\d+)$/);
    return match ? `/document-preview/${match[1]}` : reference.previewUrl;
  } catch {
    return reference.previewUrl;
  }
}
```

Render links from that safe helper:

```tsx
{getReactDocumentPreviewHref(reference) ? (
  <a key={reference.referenceId} href={getReactDocumentPreviewHref(reference)!} target="_blank" rel="noopener noreferrer">
    {reference.fileName || reference.filePath}
  </a>
) : (
  <span key={reference.referenceId}>{reference.fileName || reference.filePath}</span>
)}
```

Do not generate preview URLs from `filePath`, file names, or local paths.

- [ ] **Step 8: Rewire Documents drawer to the same safe preview API**

Update `DocumentPreviewPanel.tsx` so the drawer content is loaded through `getDocumentPreviewContent(apiBase, document.id)` instead of relying on table row `content` or guessing from `fileUrl`. Keep the drawer entry behavior from Task 3:

```tsx
type DocumentPreviewPanelProps = {
  apiBase: string;
  document: MarkdownDocumentDto;
  onClose: () => void;
  loadPreview?: (apiBase: string, documentId: number) => Promise<DocumentPreviewContent>;
};

<aside className="lrn-drawer document-preview" role="dialog" aria-modal="true" aria-label={`Preview ${document.fileName}`}>
  <header className="document-preview__header">
    <div>
      <h2>{preview?.fileName ?? document.fileName}</h2>
      <p>{preview?.contentType ?? document.ragStatus ?? "Preview"}</p>
    </div>
    <div className="lrn-toolbar">
      <a className="lrn-button" href={`/document-preview/${document.id}`}>Open full preview</a>
      <button className="lrn-icon-button" type="button" onClick={onClose} aria-label="Close preview">Close</button>
    </div>
  </header>
  <div className="document-preview__content">...</div>
</aside>
```

Create `src/LightRAGNet.React/tests/integration/features/documents/DocumentPreviewDrawer.test.tsx` to assert:

```tsx
expect(screen.getByRole("dialog", { name: /Preview system-architecture.md/i })).toBeInTheDocument();
expect(screen.getByRole("link", { name: "Open full preview" })).toHaveAttribute("href", "/document-preview/42");
expect(loadPreview).toHaveBeenCalledWith("http://localhost:5261", 42);
```

- [ ] **Step 9: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/documentPreviewApi.test.ts tests/integration/features/document-preview/DocumentPreviewPage.test.tsx tests/integration/features/documents/DocumentPreviewDrawer.test.tsx tests/integration/features/rag-chat/RagChatWorkbench.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add standalone document preview route"
```

---

### Task 8: Integrate SignalR Status In Shell

**Files:**
- Modify: `src/LightRAGNet.React/src/shared/hooks/useRagTaskHub.ts`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Modify: `src/LightRAGNet.React/src/app/AppLayout.tsx`
- Test: `src/LightRAGNet.React/tests/unit/shared/hooks/useRagTaskHub.test.tsx`
- Test: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`

- [ ] **Step 1: Extend hook test for connection status**

Modify `tests/unit/shared/hooks/useRagTaskHub.test.tsx` to assert returned connection state:

```tsx
it("reports SignalR connection status for the shell", async () => {
  const { result } = renderHook(() => useRagTaskHub("http://localhost:5261", {}));

  expect(result.current.connectionStatus).toBe("Connecting");
});
```

- [ ] **Step 2: Run hook test and confirm failure**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/shared/hooks/useRagTaskHub.test.tsx
```

Expected: fails until hook returns `connectionStatus`.

- [ ] **Step 3: Update hook return value**

Update `useRagTaskHub` so it returns:

```ts
export type RagTaskHubConnectionStatus = "Connecting" | "Connected" | "Disconnected" | "Reconnecting" | "ServerNotStarted";

export type UseRagTaskHubResult = {
  connectionStatus: RagTaskHubConnectionStatus;
};
```

Set state transitions:

```ts
const [connectionStatus, setConnectionStatus] = useState<RagTaskHubConnectionStatus>("Connecting");

client.start()
  .then(() => setConnectionStatus("Connected"))
  .catch(() => setConnectionStatus("ServerNotStarted"));

client.onReconnecting(() => setConnectionStatus("Reconnecting"));
client.onReconnected(() => setConnectionStatus("Connected"));
client.onClose(() => setConnectionStatus("Disconnected"));

return { connectionStatus };
```

- [ ] **Step 4: Wire shell status**

In `App.tsx`, use the hook once at app level and pass to layout:

```tsx
const { connectionStatus } = useRagTaskHub(apiBase, {
  onTaskStatusUpdated(update) {
    taskUpdateSubscribersRef.current.forEach((handler) => handler(update));
  },
  onDataCleared() {
    dataClearedSubscribersRef.current.forEach((handler) => handler());
  }
});

return (
  <AppLayout currentPath={window.location.pathname} connectionStatus={connectionStatus}>
    ...
  </AppLayout>
);
```

Keep existing Documents subscriptions using the app-level subscriber refs.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/shared/hooks/useRagTaskHub.test.tsx tests/integration/app/AppLayout.test.tsx tests/integration/features/documents/DocumentTaskRefresh.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands pass.

Commit during execution:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: show rag task hub status in react shell"
```

---

### Task 9: Dev Startup, Build, And CORS Check

**Files:**
- Modify: `scripts/dev-start.ps1`
- Modify: `scripts/dev-start.sh`
- Modify: `README.md`
- Modify: `README.EN.md`
- Test: `tests/LightRAGNet.Server.Tests/ReactDevCorsSourceTests.cs`

- [ ] **Step 1: Confirm server CORS already supports React dev server**

Run:

```powershell
dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter ReactDevCorsSourceTests --no-restore --verbosity minimal
```

Expected: pass, because previous standalone React work added `http://localhost:5173` and `http://127.0.0.1:5173`.

- [ ] **Step 2: Update docs with new route list**

In `README.md` and `README.EN.md`, update the React UI section to list:

```text
React UI:
- http://127.0.0.1:5173/
- http://127.0.0.1:5173/documents
- http://127.0.0.1:5173/documents/upload
- http://127.0.0.1:5173/document-preview
- http://127.0.0.1:5173/graph-view
- http://127.0.0.1:5173/system-status
- http://127.0.0.1:5173/cache-management
```

- [ ] **Step 3: Verify startup script still targets React app**

Run:

```powershell
.\scripts\dev-start.ps1 -Target React -SkipNpmInstall
```

Expected: React starts at `http://127.0.0.1:5173`. If already running, script reports reuse.

- [ ] **Step 4: Stop dev services**

Run:

```powershell
.\scripts\dev-stop.ps1
```

Expected: local dev processes started by the script stop cleanly.

- [ ] **Step 5: Commit**

Commit during execution:

```powershell
git add scripts README.md README.EN.md tests/LightRAGNet.Server.Tests/ReactDevCorsSourceTests.cs
git commit -m "docs: update standalone react frontend routes"
```

---

### Task 10: Final Verification And Visual QA

**Files:**
- Create screenshots under: `output/playwright/`
- No production code changes unless verification finds defects.

- [ ] **Step 1: Run React test suite**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React
npm run typecheck --prefix src/LightRAGNet.React
npm run build --prefix src/LightRAGNet.React
```

Expected: all commands pass.

- [ ] **Step 2: Run targeted .NET tests**

Run:

```powershell
dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "ReactDevCorsSourceTests|DocumentPreviewControllerTests|RagQueryControllerTests|CacheManagement" --no-restore --verbosity minimal
dotnet test tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --verbosity minimal
```

Expected: targeted tests pass. Web tests may still verify Blazor host assets until the removal phase.

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
dotnet test LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: pass. If a failure appears unrelated to the React migration, record exact failing test name and error before deciding whether to fix or defer.

- [ ] **Step 4: Start local server and React app**

Run:

```powershell
.\scripts\dev-start.ps1 -Target All -SkipNpmInstall
```

Expected:

- Server ready at `http://localhost:5261`.
- React ready at `http://127.0.0.1:5173`.

- [ ] **Step 5: Browser visual checks**

Use Playwright CLI or browser plugin to capture screenshots:

```powershell
New-Item -ItemType Directory -Force output\playwright
```

Check routes:

```text
http://127.0.0.1:5173/
http://127.0.0.1:5173/documents
http://127.0.0.1:5173/documents/upload
http://127.0.0.1:5173/document-preview
http://127.0.0.1:5173/graph-view
http://127.0.0.1:5173/system-status
http://127.0.0.1:5173/cache-management
```

Expected visual evidence:

- Shell renders topbar, sidebar, main content, and bottom SignalR status.
- Documents table has no clipped button text or overlapping columns.
- Documents View opens an elevated preview drawer with scrim, shadow, close action, and Open full preview link.
- Upload page uses dark workbench layout.
- RAG Chat shows only mode and stream title chips.
- RAG Chat settings include all required parameters.
- RAG Chat reference links open a full React preview route in a new tab and use backend-provided preview metadata only.
- Cards, drawers, and modal surfaces have visible elevation instead of flat borders only.
- Graph canvas is nonblank and existing controls are visible.
- Graph buttons and panel layout match the migrated island, not a redesign.
- System Status and Cache Management retain dark operations contrast.

- [ ] **Step 6: Stop dev services**

Run:

```powershell
.\scripts\dev-stop.ps1
```

Expected: server and React dev processes stop cleanly.

- [ ] **Step 7: Hygiene checks**

Run:

```powershell
git diff --check
rg -n "^(<<<<<<<|=======|>>>>>>>)" src tests docs
Get-ChildItem -Path src\LightRAGNet.React\src -Recurse -File -Include *.test.ts,*.test.tsx,*.spec.ts,*.spec.tsx
```

Expected:

- `git diff --check` passes.
- conflict marker search returns no matches.
- no React test files are under `src/LightRAGNet.React/src`.

---

## Plan Self-Review

Spec coverage:

- Full standalone React routes are covered by Tasks 1, 4, 5, 6, and 7.
- Direct migration principle is covered by Tasks 2, 4, 5, and 6.
- Documents and Upload redesign are covered by Task 3.
- RAG Chat complete settings and title chip rules are covered by Task 4.
- Knowledge Graph no-redesign constraint is covered by Task 5.
- SignalR status shell behavior is covered by Task 8.
- Dev startup and documentation are covered by Task 9.
- Verification and visual QA are covered by Task 10.

Placeholder scan:

- This plan avoids unresolved implementation markers.
- Every task has concrete files, commands, and expected outcomes.

Type consistency:

- `RouteId`, `AppRoute`, and `resolveRoute` are introduced in Task 1 and used by later tasks.
- `RagTaskHubConnectionStatus` is introduced in Task 8 and passed into `AppLayout`.
- Existing RAG Chat, Graph, System Status, and Cache Management types remain copied from Blazor ClientApp to minimize migration drift.
