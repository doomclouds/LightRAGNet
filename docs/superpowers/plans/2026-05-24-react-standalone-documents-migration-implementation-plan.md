# React Standalone Documents Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a standalone `src/LightRAGNet.React` Vite/React frontend and migrate document upload/list workflows into it while leaving the Blazor project unchanged.

**Architecture:** `LightRAGNet.Server` stays the backend API and SignalR service. `LightRAGNet.React` becomes an independent frontend service with its own Vite dev server, app shell, API clients, SignalR client, document pages, shared styling layer, and separated `tests/` tree.

**Tech Stack:** React 19, TypeScript, Vite, Vitest, Testing Library, SignalR JavaScript client, CSS theme tokens, existing ASP.NET Core Server APIs.

---

## File Structure And Responsibilities

Create these files under the new standalone frontend:

- `src/LightRAGNet.React/package.json`: npm package metadata and scripts.
- `src/LightRAGNet.React/vite.config.ts`: Vite config, React plugin, `@/` alias, Vitest config.
- `src/LightRAGNet.React/tsconfig.json`: strict TypeScript config.
- `src/LightRAGNet.React/index.html`: Vite entry HTML.
- `src/LightRAGNet.React/src/main.tsx`: React root bootstrap.
- `src/LightRAGNet.React/src/app/App.tsx`: top-level app composition.
- `src/LightRAGNet.React/src/app/AppLayout.tsx`: common shell and navigation.
- `src/LightRAGNet.React/src/app/router.tsx`: small route switch for `/documents` and `/documents/upload`.
- `src/LightRAGNet.React/src/api/http.ts`: base URL, URL builder, JSON/error helpers.
- `src/LightRAGNet.React/src/api/documentsApi.ts`: typed document API client.
- `src/LightRAGNet.React/src/api/ragTaskHubClient.ts`: reusable SignalR client wrapper.
- `src/LightRAGNet.React/src/features/documents/documentTypes.ts`: React-side DTO types matching `LightRAGNet.Share`.
- `src/LightRAGNet.React/src/features/documents/documentStatus.ts`: status helpers, action eligibility, labels.
- `src/LightRAGNet.React/src/features/documents/documentFormatters.ts`: file size/date/error formatters.
- `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`: batch upload page.
- `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`: paged document list and status refresh.
- `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`: document detail preview.
- `src/LightRAGNet.React/src/shared/styles/theme.css`: migrated/receiving shared theme tokens.
- `src/LightRAGNet.React/src/shared/styles/app.css`: global layout and shared component classes.
- `src/LightRAGNet.React/tests/setup/vitest.setup.ts`: Vitest DOM setup.
- `src/LightRAGNet.React/tests/unit/...`: unit tests for API and helpers.
- `src/LightRAGNet.React/tests/integration/...`: React page tests.

Modify backend only for React dev-server CORS:

- `src/LightRAGNet.Server/Program.cs`: add `http://localhost:5173` and `http://127.0.0.1:5173` to allowed origins.
- `tests/LightRAGNet.Server.Tests/ServerHostSmokeTests.cs` or a new source test: assert the Vite origins are present in server source.

Do not modify these files in this phase:

- `src/LightRAGNet.Web/**`
- `tests/LightRAGNet.Web.Tests/**`
- `tests/LightRAGNet.Tests/Web/**`

---

### Task 1: Scaffold `LightRAGNet.React` And Test Harness

**Files:**
- Create: `src/LightRAGNet.React/package.json`
- Create: `src/LightRAGNet.React/tsconfig.json`
- Create: `src/LightRAGNet.React/vite.config.ts`
- Create: `src/LightRAGNet.React/index.html`
- Create: `src/LightRAGNet.React/src/main.tsx`
- Create: `src/LightRAGNet.React/src/app/App.tsx`
- Create: `src/LightRAGNet.React/src/app/AppLayout.tsx`
- Create: `src/LightRAGNet.React/src/app/router.tsx`
- Create: `src/LightRAGNet.React/src/shared/styles/theme.css`
- Create: `src/LightRAGNet.React/src/shared/styles/app.css`
- Create: `src/LightRAGNet.React/tests/setup/vitest.setup.ts`
- Test: `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`

- [ ] **Step 1: Create project folders**

Run:

```powershell
New-Item -ItemType Directory -Force `
  src\LightRAGNet.React\src\app, `
  src\LightRAGNet.React\src\api, `
  src\LightRAGNet.React\src\features\documents, `
  src\LightRAGNet.React\src\shared\components, `
  src\LightRAGNet.React\src\shared\hooks, `
  src\LightRAGNet.React\src\shared\styles, `
  src\LightRAGNet.React\src\shared\utils, `
  src\LightRAGNet.React\tests\unit\api, `
  src\LightRAGNet.React\tests\unit\features\documents, `
  src\LightRAGNet.React\tests\integration\app, `
  src\LightRAGNet.React\tests\integration\features\documents, `
  src\LightRAGNet.React\tests\e2e, `
  src\LightRAGNet.React\tests\setup
```

Expected: all directories are created; no `src/LightRAGNet.React/*.csproj` exists.

- [ ] **Step 2: Initialize npm package and dependencies**

Run:

```powershell
Set-Location src\LightRAGNet.React
npm init -y
npm install react react-dom lucide-react @microsoft/signalr react-markdown remark-gfm
npm install -D typescript vite @vitejs/plugin-react vitest jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event @types/react @types/react-dom
Set-Location ..\..
```

Expected: `src/LightRAGNet.React/package.json` and `package-lock.json` exist.

- [ ] **Step 3: Replace `package.json` scripts and package name**

Set `src/LightRAGNet.React/package.json` to contain these scripts:

```json
{
  "name": "lightragnet-react",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc --noEmit --pretty false && vite build",
    "typecheck": "tsc --noEmit",
    "test": "vitest run"
  }
}
```

Keep the `dependencies` and `devDependencies` sections generated by `npm install`.

- [ ] **Step 4: Write TypeScript, Vite, and test setup**

Create `src/LightRAGNet.React/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "useDefineForClassFields": true,
    "lib": ["DOM", "DOM.Iterable", "ES2022"],
    "allowJs": false,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "allowSyntheticDefaultImports": true,
    "strict": true,
    "forceConsistentCasingInFileNames": true,
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"]
    }
  },
  "include": ["src", "tests", "vite.config.ts"]
}
```

Create `src/LightRAGNet.React/vite.config.ts`:

```ts
import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src")
    }
  },
  server: {
    port: 5173,
    strictPort: true
  },
  test: {
    environment: "jsdom",
    setupFiles: ["tests/setup/vitest.setup.ts"],
    globals: true
  }
});
```

Create `src/LightRAGNet.React/tests/setup/vitest.setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
```

- [ ] **Step 5: Write the failing layout test**

Create `src/LightRAGNet.React/tests/integration/app/AppLayout.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "@/app/App";

describe("App layout", () => {
  it("renders the standalone React shell with document navigation", () => {
    render(<App />);

    expect(screen.getByRole("banner")).toHaveTextContent("LightRAGNet");
    expect(screen.getByRole("link", { name: /Documents/i })).toHaveAttribute("href", "/documents");
    expect(screen.getByRole("link", { name: /Upload/i })).toHaveAttribute("href", "/documents/upload");
  });
});
```

- [ ] **Step 6: Run the failing test**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx
```

Expected: FAIL because `@/app/App` does not exist.

- [ ] **Step 7: Implement the minimal shell**

Create `src/LightRAGNet.React/index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>LightRAGNet</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

Create `src/LightRAGNet.React/src/shared/styles/theme.css`:

```css
:root {
  --app-bg: #0d1117;
  --panel-bg: #151b23;
  --panel-border: #303946;
  --text-primary: #edf2f7;
  --text-secondary: #a9b4c2;
  --accent: #4cc9f0;
  --danger: #ff6b6b;
  --warning: #f6c85f;
  --success: #7bd88f;
  --control-bg: #151b23;
  --control-border: #303946;
  --shadow-panel: 0 18px 42px rgba(0, 0, 0, .24);
}
```

Create `src/LightRAGNet.React/src/shared/styles/app.css`:

```css
@import "./theme.css";

body {
  margin: 0;
  min-width: 320px;
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: Inter, "Segoe UI", system-ui, sans-serif;
}

a {
  color: inherit;
  text-decoration: none;
}

.app-shell {
  min-height: 100vh;
  display: grid;
  grid-template-columns: 240px 1fr;
}

.app-sidebar {
  border-right: 1px solid var(--panel-border);
  background: var(--panel-bg);
  padding: 20px;
}

.app-brand {
  font-size: 18px;
  font-weight: 700;
  margin-bottom: 28px;
}

.app-nav {
  display: grid;
  gap: 8px;
}

.app-nav a {
  border: 1px solid transparent;
  border-radius: 8px;
  padding: 10px 12px;
  color: var(--text-secondary);
}

.app-nav a[aria-current="page"],
.app-nav a:hover {
  border-color: var(--panel-border);
  color: var(--text-primary);
  background: rgba(76, 201, 240, .08);
}

.app-main {
  padding: 24px;
}

.panel {
  border: 1px solid var(--panel-border);
  border-radius: 8px;
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
}
```

Create `src/LightRAGNet.React/src/app/router.tsx`:

```tsx
export type AppRoute = "/documents" | "/documents/upload";

export function getCurrentRoute(pathname = window.location.pathname): AppRoute {
  if (pathname === "/documents/upload") {
    return "/documents/upload";
  }

  return "/documents";
}
```

Create `src/LightRAGNet.React/src/app/AppLayout.tsx`:

```tsx
import type { ReactNode } from "react";
import type { AppRoute } from "./router";

type AppLayoutProps = {
  currentRoute: AppRoute;
  children: ReactNode;
};

export function AppLayout({ currentRoute, children }: AppLayoutProps) {
  return (
    <div className="app-shell">
      <aside className="app-sidebar">
        <header role="banner" className="app-brand">LightRAGNet</header>
        <nav className="app-nav" aria-label="Primary">
          <a href="/documents" aria-current={currentRoute === "/documents" ? "page" : undefined}>Documents</a>
          <a href="/documents/upload" aria-current={currentRoute === "/documents/upload" ? "page" : undefined}>Upload</a>
        </nav>
      </aside>
      <main className="app-main">{children}</main>
    </div>
  );
}
```

Create `src/LightRAGNet.React/src/app/App.tsx`:

```tsx
import { AppLayout } from "./AppLayout";
import { getCurrentRoute } from "./router";
import "@/shared/styles/app.css";

export function App() {
  const currentRoute = getCurrentRoute();

  return (
    <AppLayout currentRoute={currentRoute}>
      <section className="panel" style={{ padding: 24 }}>
        <h1>{currentRoute === "/documents/upload" ? "Upload Document" : "Documents"}</h1>
      </section>
    </AppLayout>
  );
}
```

Create `src/LightRAGNet.React/src/main.tsx`:

```tsx
import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "@/app/App";

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element #root was not found.");
}

createRoot(rootElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
```

- [ ] **Step 8: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/app/AppLayout.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: scaffold standalone react frontend"
```

---

### Task 2: Add Document Types, Formatters, And API Client

**Files:**
- Create: `src/LightRAGNet.React/src/api/http.ts`
- Create: `src/LightRAGNet.React/src/api/documentsApi.ts`
- Create: `src/LightRAGNet.React/src/features/documents/documentTypes.ts`
- Create: `src/LightRAGNet.React/src/features/documents/documentStatus.ts`
- Create: `src/LightRAGNet.React/src/features/documents/documentFormatters.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/documentsApi.test.ts`
- Test: `src/LightRAGNet.React/tests/unit/features/documents/documentStatus.test.ts`

- [ ] **Step 1: Write helper tests**

Create `src/LightRAGNet.React/tests/unit/features/documents/documentStatus.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { canCancelDocumentPipeline, canRetryDocument, isDocumentBusy, normalizeFilterStatus } from "@/features/documents/documentStatus";

describe("document status helpers", () => {
  it("normalizes Pending to Queued for filters", () => {
    expect(normalizeFilterStatus("Pending")).toBe("Queued");
    expect(normalizeFilterStatus("Processing")).toBe("Processing");
  });

  it("detects busy documents", () => {
    expect(isDocumentBusy({ ragStatus: "Queued" })).toBe(true);
    expect(isDocumentBusy({ ragStatus: "Processing" })).toBe(true);
    expect(isDocumentBusy({ ragStatus: "Deleting" })).toBe(true);
    expect(isDocumentBusy({ ragStatus: "Completed" })).toBe(false);
  });

  it("exposes retry and cancel eligibility", () => {
    expect(canRetryDocument({ ragStatus: "Failed" })).toBe(true);
    expect(canRetryDocument({ ragStatus: "Cancelled" })).toBe(true);
    expect(canRetryDocument({ ragStatus: "Processing" })).toBe(false);
    expect(canCancelDocumentPipeline({ ragStatus: "Queued" })).toBe(true);
    expect(canCancelDocumentPipeline({ ragStatus: "Pending" })).toBe(true);
    expect(canCancelDocumentPipeline({ ragStatus: "Completed" })).toBe(false);
  });
});
```

Create `src/LightRAGNet.React/tests/unit/api/documentsApi.test.ts`:

```ts
import { afterEach, describe, expect, it, vi } from "vitest";
import { getMarkdownDocuments, uploadDocuments } from "@/api/documentsApi";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("documentsApi", () => {
  it("loads paged documents with status filter", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0 }), {
        status: 200,
        headers: { "content-type": "application/json" }
      })
    );

    await getMarkdownDocuments("http://localhost:5261", { page: 1, pageSize: 10, status: "Queued" });

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5261/api/MarkdownDocuments?page=1&pageSize=10&status=Queued", { method: "GET" });
  });

  it("uploads files through the batch upload endpoint using the files field", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ trackId: "track-1", documents: [] }), {
        status: 202,
        headers: { "content-type": "application/json" }
      })
    );
    const file = new File(["# hello"], "hello.md", { type: "text/markdown" });

    await uploadDocuments("http://localhost:5261", [file]);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://localhost:5261/api/MarkdownDocuments/upload");
    expect(init?.method).toBe("POST");
    expect(init?.body).toBeInstanceOf(FormData);
    expect((init?.body as FormData).getAll("files")).toHaveLength(1);
  });

  it("throws backend error messages when requests fail", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ error: "Document has active RAG task" }), {
        status: 409,
        headers: { "content-type": "application/json" }
      })
    );

    await expect(getMarkdownDocuments("http://localhost:5261", { page: 1, pageSize: 10 })).rejects.toThrow("Document has active RAG task");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/documentsApi.test.ts tests/unit/features/documents/documentStatus.test.ts
```

Expected: FAIL because the API and helper modules do not exist.

- [ ] **Step 3: Implement document types and helpers**

Create `src/LightRAGNet.React/src/features/documents/documentTypes.ts`:

```ts
export type MarkdownDocumentDto = {
  id: number;
  fileName: string;
  content?: string | null;
  fileSize: number;
  uploadTime: string;
  lastModified?: string | null;
  isInRagSystem: boolean;
  ragAddedTime?: string | null;
  ragStatus?: string | null;
  trackId?: string | null;
  ragProgress: number;
  ragCurrentStage?: string | null;
  activeRagTaskId?: string | null;
  ragRetryCount: number;
  ragErrorMessage?: string | null;
  ragDocumentId?: string | null;
  fileUrl?: string | null;
  originalFileName?: string | null;
  originalContentType?: string | null;
  conversionStatus?: string | null;
  conversionErrorMessage?: string | null;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export type DocumentSubmissionResponse = {
  trackId: string;
  documents: MarkdownDocumentDto[];
};

export type DocumentPipelineActionResult = {
  accepted: boolean;
  documentId: number;
  status: string;
  message?: string | null;
};

export type MarkdownDocumentDeleteClientResult = {
  succeeded?: boolean;
  deletedImmediately?: boolean;
  accepted?: boolean;
  conflict?: boolean;
  taskId?: string | null;
  errorMessage?: string | null;
};

export type TaskStatusUpdate = {
  documentId: number;
  status: string;
  operationType?: string | null;
  currentStage?: string | null;
  progress: number;
  errorMessage?: string | null;
  completedAt?: string | null;
};
```

Create `src/LightRAGNet.React/src/features/documents/documentStatus.ts`:

```ts
type StatusLike = {
  ragStatus?: string | null;
};

export function normalizeFilterStatus(status?: string | null): string | undefined {
  if (!status) {
    return undefined;
  }

  return status === "Pending" ? "Queued" : status;
}

export function isDocumentBusy(document: StatusLike): boolean {
  return document.ragStatus === "Pending" ||
    document.ragStatus === "Queued" ||
    document.ragStatus === "Processing" ||
    document.ragStatus === "Deleting";
}

export function canRetryDocument(document: StatusLike): boolean {
  return document.ragStatus === "Failed" || document.ragStatus === "Cancelled";
}

export function canCancelDocumentPipeline(document: StatusLike): boolean {
  return document.ragStatus === "Queued" ||
    document.ragStatus === "Processing" ||
    document.ragStatus === "Pending";
}

export function getShortErrorMessage(errorMessage?: string | null): string {
  if (!errorMessage || errorMessage.trim().length === 0) {
    return "Unknown error";
  }

  return errorMessage.length <= 120 ? errorMessage : `${errorMessage.slice(0, 120)}...`;
}
```

Create `src/LightRAGNet.React/src/features/documents/documentFormatters.ts`:

```ts
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}

export function formatDateTime(value?: string | null): string {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString();
}
```

- [ ] **Step 4: Implement API helpers**

Create `src/LightRAGNet.React/src/api/http.ts`:

```ts
type ErrorLikeResponse = {
  message?: string;
  error?: string;
  title?: string;
};

export function getApiBase(): string {
  return import.meta.env.VITE_LIGHTRAG_API_BASE ?? "http://localhost:5261";
}

export function buildUrl(apiBase: string, path: string): string {
  return `${apiBase.replace(/\/+$/, "")}${path}`;
}

export async function readJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  const statusMessage = response.statusText || `Request failed with status ${response.status}`;

  if (text.trim().length === 0) {
    if (!response.ok) {
      throw new Error(statusMessage);
    }

    return undefined as T;
  }

  let body: T & ErrorLikeResponse;

  try {
    body = JSON.parse(text) as T & ErrorLikeResponse;
  } catch {
    throw new Error(response.ok ? "Invalid JSON response" : statusMessage);
  }

  if (!response.ok) {
    throw new Error(body.message ?? body.error ?? body.title ?? statusMessage);
  }

  return body;
}
```

Create `src/LightRAGNet.React/src/api/documentsApi.ts`:

```ts
import { buildUrl, readJson } from "./http";
import type {
  DocumentPipelineActionResult,
  DocumentSubmissionResponse,
  MarkdownDocumentDeleteClientResult,
  MarkdownDocumentDto,
  PagedResult
} from "@/features/documents/documentTypes";

type DocumentsQuery = {
  page: number;
  pageSize: number;
  status?: string | null;
  trackId?: string | null;
};

export async function getMarkdownDocuments(apiBase: string, query: DocumentsQuery): Promise<PagedResult<MarkdownDocumentDto>> {
  const search = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize)
  });

  if (query.status) {
    search.set("status", query.status);
  }

  if (query.trackId) {
    search.set("trackId", query.trackId);
  }

  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments?${search.toString()}`), { method: "GET" });
  return readJson<PagedResult<MarkdownDocumentDto>>(response);
}

export async function getMarkdownDocument(apiBase: string, id: number): Promise<MarkdownDocumentDto> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}`), { method: "GET" });
  return readJson<MarkdownDocumentDto>(response);
}

export async function uploadDocuments(apiBase: string, files: File[]): Promise<DocumentSubmissionResponse> {
  const form = new FormData();
  for (const file of files) {
    form.append("files", file, file.name);
  }

  const response = await fetch(buildUrl(apiBase, "/api/MarkdownDocuments/upload"), {
    method: "POST",
    body: form
  });
  return readJson<DocumentSubmissionResponse>(response);
}

export async function addToRagSystem(apiBase: string, id: number): Promise<MarkdownDocumentDto> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/add-to-rag`), { method: "POST" });
  return readJson<MarkdownDocumentDto>(response);
}

export async function retryDocument(apiBase: string, id: number): Promise<DocumentPipelineActionResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/retry`), { method: "POST" });
  return readJson<DocumentPipelineActionResult>(response);
}

export async function cancelDocumentPipeline(apiBase: string, id: number): Promise<DocumentPipelineActionResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/cancel`), { method: "POST" });
  return readJson<DocumentPipelineActionResult>(response);
}

export async function deleteMarkdownDocument(apiBase: string, id: number): Promise<MarkdownDocumentDeleteClientResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}?deleteLlmCache=false`), { method: "DELETE" });

  if (response.status === 204) {
    return { succeeded: true, deletedImmediately: true };
  }

  if (response.status === 202) {
    const body = await readJson<{ taskId?: string | null }>(response);
    return { succeeded: true, accepted: true, taskId: body.taskId };
  }

  if (response.status === 409) {
    try {
      await readJson<unknown>(response);
    } catch (error) {
      return { conflict: true, errorMessage: error instanceof Error ? error.message : "Conflict" };
    }
  }

  try {
    await readJson<unknown>(response);
  } catch (error) {
    return { errorMessage: error instanceof Error ? error.message : "Request failed" };
  }

  return { errorMessage: "Request failed" };
}
```

- [ ] **Step 5: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/documentsApi.test.ts tests/unit/features/documents/documentStatus.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add react document api client"
```

---

### Task 3: Add React SignalR Task Notification Client

**Files:**
- Create: `src/LightRAGNet.React/src/api/ragTaskHubClient.ts`
- Create: `src/LightRAGNet.React/src/shared/hooks/useRagTaskHub.ts`
- Test: `src/LightRAGNet.React/tests/unit/api/ragTaskHubClient.test.ts`

- [ ] **Step 1: Write SignalR wrapper tests**

Create `src/LightRAGNet.React/tests/unit/api/ragTaskHubClient.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { createRagTaskHubClient } from "@/api/ragTaskHubClient";

describe("ragTaskHubClient", () => {
  it("subscribes to task updates and data cleared events", async () => {
    const on = vi.fn();
    const start = vi.fn().mockResolvedValue(undefined);
    const stop = vi.fn().mockResolvedValue(undefined);
    const connection = { on, start, stop, onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn() };
    const factory = vi.fn().mockReturnValue(connection);

    const client = createRagTaskHubClient("http://localhost:5261", factory);
    await client.start();

    expect(factory).toHaveBeenCalledWith("http://localhost:5261/hubs/ragtask");
    expect(on).toHaveBeenCalledWith("TaskStatusUpdated", expect.any(Function));
    expect(on).toHaveBeenCalledWith("DataCleared", expect.any(Function));

    await client.stop();
    expect(stop).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/ragTaskHubClient.test.ts
```

Expected: FAIL because `ragTaskHubClient.ts` does not exist.

- [ ] **Step 3: Implement SignalR client with injectable factory**

Create `src/LightRAGNet.React/src/api/ragTaskHubClient.ts`:

```ts
import * as signalR from "@microsoft/signalr";
import type { TaskStatusUpdate } from "@/features/documents/documentTypes";

export type RagTaskHubConnection = {
  on(eventName: "TaskStatusUpdated", callback: (update: TaskStatusUpdate) => void): void;
  on(eventName: "DataCleared", callback: () => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
};

export type RagTaskHubConnectionFactory = (hubUrl: string) => RagTaskHubConnection;

export type RagTaskHubHandlers = {
  onTaskStatusUpdated?: (update: TaskStatusUpdate) => void;
  onDataCleared?: () => void;
  onConnectionStateChanged?: (state: "Connected" | "Disconnected" | "Reconnecting" | "ServerNotStarted") => void;
};

function defaultFactory(hubUrl: string): RagTaskHubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .build();
}

export function createRagTaskHubClient(apiBase: string, factory: RagTaskHubConnectionFactory = defaultFactory) {
  const hubUrl = `${apiBase.replace(/\/+$/, "")}/hubs/ragtask`;
  const connection = factory(hubUrl);

  return {
    configure(handlers: RagTaskHubHandlers) {
      connection.on("TaskStatusUpdated", update => handlers.onTaskStatusUpdated?.(update));
      connection.on("DataCleared", () => handlers.onDataCleared?.());
      connection.onreconnecting(() => handlers.onConnectionStateChanged?.("Reconnecting"));
      connection.onreconnected(() => handlers.onConnectionStateChanged?.("Connected"));
      connection.onclose(() => handlers.onConnectionStateChanged?.("Disconnected"));
    },
    async start(handlers: RagTaskHubHandlers = {}) {
      this.configure(handlers);
      try {
        await connection.start();
        handlers.onConnectionStateChanged?.("Connected");
      } catch {
        handlers.onConnectionStateChanged?.("ServerNotStarted");
      }
    },
    async stop() {
      await connection.stop();
    }
  };
}
```

Create `src/LightRAGNet.React/src/shared/hooks/useRagTaskHub.ts`:

```ts
import { useEffect, useMemo, useState } from "react";
import { createRagTaskHubClient, type RagTaskHubHandlers } from "@/api/ragTaskHubClient";

export function useRagTaskHub(apiBase: string, handlers: Omit<RagTaskHubHandlers, "onConnectionStateChanged">) {
  const [connectionState, setConnectionState] = useState("Disconnected");
  const client = useMemo(() => createRagTaskHubClient(apiBase), [apiBase]);

  useEffect(() => {
    void client.start({
      ...handlers,
      onConnectionStateChanged: setConnectionState
    });

    return () => {
      void client.stop();
    };
  }, [client, handlers]);

  return { connectionState };
}
```

- [ ] **Step 4: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/api/ragTaskHubClient.test.ts
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add react rag task hub client"
```

---

### Task 4: Implement Document Upload Page

**Files:**
- Create: `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`

- [ ] **Step 1: Write upload page integration tests**

Create `src/LightRAGNet.React/tests/integration/features/documents/UploadDocumentPage.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { UploadDocumentPage } from "@/features/documents/UploadDocumentPage";

describe("UploadDocumentPage", () => {
  it("rejects unsupported and oversized files before upload", async () => {
    const uploadDocuments = vi.fn();
    render(<UploadDocumentPage apiBase="http://localhost:5261" uploadDocuments={uploadDocuments} />);

    const input = screen.getByLabelText(/Select files/i);
    const unsupported = new File(["bad"], "bad.exe", { type: "application/octet-stream" });
    const oversized = new File([new Uint8Array(10 * 1024 * 1024 + 1)], "large.pdf", { type: "application/pdf" });

    await userEvent.upload(input, [unsupported, oversized]);

    expect(await screen.findByText(/unsupported file type/i)).toBeInTheDocument();
    expect(screen.getByText(/exceeds 10MB/i)).toBeInTheDocument();
    expect(uploadDocuments).not.toHaveBeenCalled();
  });

  it("submits valid files as one batch", async () => {
    const uploadDocuments = vi.fn().mockResolvedValue({ trackId: "track-1", documents: [{ id: 1, fileName: "a.md" }] });
    render(<UploadDocumentPage apiBase="http://localhost:5261" uploadDocuments={uploadDocuments} />);

    const input = screen.getByLabelText(/Select files/i);
    await userEvent.upload(input, [
      new File(["# A"], "a.md", { type: "text/markdown" }),
      new File(["PDF"], "b.pdf", { type: "application/pdf" })
    ]);
    await userEvent.click(screen.getByRole("button", { name: /Upload 2/i }));

    expect(uploadDocuments).toHaveBeenCalledWith("http://localhost:5261", expect.arrayContaining([expect.any(File), expect.any(File)]));
    expect(await screen.findByText(/Successfully uploaded 1 file/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/UploadDocumentPage.test.tsx
```

Expected: FAIL because `UploadDocumentPage.tsx` does not exist.

- [ ] **Step 3: Implement upload page**

Create `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`:

```tsx
import { useMemo, useState } from "react";
import { Upload } from "lucide-react";
import { uploadDocuments as defaultUploadDocuments } from "@/api/documentsApi";
import { formatFileSize } from "./documentFormatters";

const maxFiles = 10;
const maxFileSize = 10 * 1024 * 1024;
const supportedExtensions = new Set([".md", ".markdown", ".pdf", ".docx"]);

type UploadDocumentPageProps = {
  apiBase: string;
  uploadDocuments?: typeof defaultUploadDocuments;
};

function getExtension(fileName: string): string {
  const index = fileName.lastIndexOf(".");
  return index >= 0 ? fileName.slice(index).toLowerCase() : "";
}

export function UploadDocumentPage({ apiBase, uploadDocuments = defaultUploadDocuments }: UploadDocumentPageProps) {
  const [files, setFiles] = useState<File[]>([]);
  const [messages, setMessages] = useState<string[]>([]);
  const [uploading, setUploading] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const totalSize = useMemo(() => files.reduce((sum, file) => sum + file.size, 0), [files]);

  function addFiles(selectedFiles: FileList | null) {
    if (!selectedFiles) {
      return;
    }

    const nextFiles = [...files];
    const nextMessages: string[] = [];

    for (const file of Array.from(selectedFiles)) {
      if (nextFiles.length >= maxFiles) {
        nextMessages.push(`${file.name} cannot be added because the batch already has 10 files`);
        continue;
      }

      if (!supportedExtensions.has(getExtension(file.name))) {
        nextMessages.push(`${file.name} unsupported file type`);
        continue;
      }

      if (file.size > maxFileSize) {
        nextMessages.push(`${file.name} exceeds 10MB`);
        continue;
      }

      if (nextFiles.some(existing => existing.name === file.name)) {
        nextMessages.push(`${file.name} already selected`);
        continue;
      }

      nextFiles.push(file);
    }

    setFiles(nextFiles);
    setMessages(nextMessages);
    setSuccessMessage(null);
  }

  async function submit() {
    if (files.length === 0) {
      setMessages(["Please select files first"]);
      return;
    }

    setUploading(true);
    setMessages([]);
    setSuccessMessage(null);

    try {
      const result = await uploadDocuments(apiBase, files);
      setFiles([]);
      setSuccessMessage(`Successfully uploaded ${result.documents.length} file(s). Use Add to RAG from the document list when ready.`);
    } catch (error) {
      setMessages([error instanceof Error ? error.message : "Upload failed"]);
    } finally {
      setUploading(false);
    }
  }

  return (
    <section className="page-stack">
      <header className="page-header">
        <div>
          <h1>Upload Document</h1>
          <p>Upload Markdown, PDF, or DOCX files. Add to RAG is started later from the document list.</p>
        </div>
      </header>

      <div className="panel document-upload-panel">
        <label className="file-picker">
          <Upload size={18} />
          <span>Select files</span>
          <input aria-label="Select files" type="file" multiple accept=".md,.markdown,.pdf,.docx" onChange={event => addFiles(event.target.files)} />
        </label>

        {files.length > 0 && (
          <div className="selected-files">
            <div className="muted">Selected {files.length} file(s), total size {formatFileSize(totalSize)}</div>
            {files.map(file => (
              <div className="file-row" key={file.name}>
                <span>{file.name}</span>
                <span>{formatFileSize(file.size)}</span>
              </div>
            ))}
          </div>
        )}

        {messages.map(message => <div className="alert error" key={message}>{message}</div>)}
        {successMessage && <div className="alert success">{successMessage}</div>}

        <button className="primary-button" type="button" disabled={files.length === 0 || uploading} onClick={() => void submit()}>
          {uploading ? "Uploading..." : `Upload ${files.length}`}
        </button>
      </div>
    </section>
  );
}
```

Append to `src/LightRAGNet.React/src/shared/styles/app.css`:

```css
.page-stack {
  display: grid;
  gap: 18px;
}

.page-header h1 {
  margin: 0 0 6px;
  font-size: 28px;
}

.page-header p,
.muted {
  color: var(--text-secondary);
}

.document-upload-panel {
  padding: 20px;
  display: grid;
  gap: 16px;
}

.file-picker {
  width: fit-content;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--control-border);
  border-radius: 8px;
  padding: 10px 14px;
  cursor: pointer;
  color: var(--text-primary);
  background: var(--control-bg);
}

.file-picker input {
  display: none;
}

.selected-files {
  display: grid;
  gap: 8px;
}

.file-row {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  border: 1px solid var(--panel-border);
  border-radius: 8px;
  padding: 10px 12px;
}

.alert {
  border-radius: 8px;
  padding: 10px 12px;
}

.alert.error {
  color: var(--danger);
  background: rgba(255, 107, 107, .12);
}

.alert.success {
  color: var(--success);
  background: rgba(123, 216, 143, .12);
}

.primary-button {
  width: fit-content;
  border: 1px solid var(--accent);
  border-radius: 8px;
  padding: 10px 14px;
  color: #061018;
  background: var(--accent);
  font-weight: 700;
}

.primary-button:disabled {
  cursor: not-allowed;
  opacity: .55;
}
```

Modify `src/LightRAGNet.React/src/app/App.tsx`:

```tsx
import { getApiBase } from "@/api/http";
import { UploadDocumentPage } from "@/features/documents/UploadDocumentPage";
import { AppLayout } from "./AppLayout";
import { getCurrentRoute } from "./router";
import "@/shared/styles/app.css";

export function App() {
  const currentRoute = getCurrentRoute();
  const apiBase = getApiBase();

  return (
    <AppLayout currentRoute={currentRoute}>
      {currentRoute === "/documents/upload"
        ? <UploadDocumentPage apiBase={apiBase} />
        : <section className="panel" style={{ padding: 24 }}><h1>Documents</h1></section>}
    </AppLayout>
  );
}
```

- [ ] **Step 4: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/UploadDocumentPage.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add react document upload page"
```

---

### Task 5: Implement Document List Rendering And Filtering

**Files:**
- Create: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Modify: `src/LightRAGNet.React/src/app/App.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`

- [ ] **Step 1: Write list rendering tests**

Create `src/LightRAGNet.React/tests/integration/features/documents/DocumentsPage.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DocumentsPage } from "@/features/documents/DocumentsPage";

const firstPage = {
  items: [{
    id: 1,
    fileName: "guide.pdf",
    fileSize: 2048,
    uploadTime: "2026-05-24T10:00:00Z",
    isInRagSystem: false,
    ragStatus: null,
    ragProgress: 0,
    ragRetryCount: 0,
    fileUrl: null
  }],
  totalCount: 1,
  page: 1,
  pageSize: 10,
  totalPages: 1
};

describe("DocumentsPage", () => {
  it("loads and renders documents", async () => {
    const loadDocuments = vi.fn().mockResolvedValue(firstPage);
    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={loadDocuments} />);

    expect(await screen.findByText("guide.pdf")).toBeInTheDocument();
    expect(screen.getByText(/Not Added/i)).toBeInTheDocument();
    expect(loadDocuments).toHaveBeenCalledWith("http://localhost:5261", { page: 1, pageSize: 10, status: undefined });
  });

  it("reloads when the status filter changes", async () => {
    const loadDocuments = vi.fn().mockResolvedValue(firstPage);
    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={loadDocuments} />);

    await screen.findByText("guide.pdf");
    await userEvent.selectOptions(screen.getByLabelText(/Status/i), "Queued");

    expect(loadDocuments).toHaveBeenLastCalledWith("http://localhost:5261", { page: 1, pageSize: 10, status: "Queued" });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx
```

Expected: FAIL because `DocumentsPage.tsx` does not exist.

- [ ] **Step 3: Implement document list page**

Create `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`:

```tsx
import { useCallback, useEffect, useState } from "react";
import { getMarkdownDocuments as defaultLoadDocuments } from "@/api/documentsApi";
import type { MarkdownDocumentDto, PagedResult } from "./documentTypes";
import { formatDateTime, formatFileSize } from "./documentFormatters";

type DocumentsPageProps = {
  apiBase: string;
  loadDocuments?: typeof defaultLoadDocuments;
};

const pageSize = 10;
const statuses = ["Queued", "Processing", "Completed", "Failed", "Cancelled"];

function statusLabel(document: MarkdownDocumentDto): string {
  if (!document.ragStatus) {
    return "Not Added";
  }

  return document.ragCurrentStage ? `${document.ragStatus} / ${document.ragCurrentStage}` : document.ragStatus;
}

export function DocumentsPage({ apiBase, loadDocuments = defaultLoadDocuments }: DocumentsPageProps) {
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [data, setData] = useState<PagedResult<MarkdownDocumentDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async (nextPage = page, nextStatus = status) => {
    setLoading(true);
    setError(null);
    try {
      const result = await loadDocuments(apiBase, { page: nextPage, pageSize, status: nextStatus });
      setData(result);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Failed to load document list");
      setData({ items: [], totalCount: 0, page: nextPage, pageSize, totalPages: 0 });
    } finally {
      setLoading(false);
    }
  }, [apiBase, loadDocuments, page, status]);

  useEffect(() => {
    void reload(1, status);
  }, [reload, status]);

  function changeStatus(value: string) {
    const nextStatus = value.length === 0 ? undefined : value;
    setStatus(nextStatus);
    setPage(1);
  }

  return (
    <section className="page-stack">
      <header className="page-header document-header">
        <div>
          <h1>Documents</h1>
          <p>Review uploaded files, start RAG indexing, and track processing state.</p>
        </div>
        <a className="secondary-button" href="/documents/upload">Upload</a>
      </header>

      <div className="toolbar panel">
        <label>
          <span>Status</span>
          <select aria-label="Status" value={status ?? ""} onChange={event => changeStatus(event.target.value)}>
            <option value="">All</option>
            {statuses.map(option => <option key={option} value={option}>{option}</option>)}
          </select>
        </label>
      </div>

      {error && <div className="alert error">{error}</div>}
      {loading && <div className="panel empty-state">Loading documents...</div>}
      {!loading && data && data.items.length === 0 && <div className="panel empty-state">No documents yet.</div>}

      {!loading && data && data.items.length > 0 && (
        <div className="panel table-panel">
          <table className="documents-table">
            <thead>
              <tr>
                <th>File Name</th>
                <th>File Size</th>
                <th>Upload Time</th>
                <th>RAG Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map(document => (
                <tr key={document.id}>
                  <td>{document.fileName}</td>
                  <td>{formatFileSize(document.fileSize)}</td>
                  <td>{formatDateTime(document.uploadTime)}</td>
                  <td>
                    <span className={`status-chip status-${document.ragStatus ?? "not-added"}`}>{statusLabel(document)}</span>
                    {document.ragStatus === "Processing" && (
                      <div className="progress-line" aria-label={`Progress ${document.ragProgress}%`}>
                        <span style={{ width: `${document.ragProgress}%` }} />
                      </div>
                    )}
                  </td>
                  <td className="action-cell">
                    <button type="button">View</button>
                    <button type="button">Add to RAG</button>
                    <button type="button">Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <footer className="table-footer">
            <span>Page {data.page} of {Math.max(1, data.totalPages)}</span>
            <button type="button" disabled={page <= 1} onClick={() => { setPage(page - 1); void reload(page - 1, status); }}>Previous</button>
            <button type="button" disabled={page >= data.totalPages} onClick={() => { setPage(page + 1); void reload(page + 1, status); }}>Next</button>
          </footer>
        </div>
      )}
    </section>
  );
}
```

Modify `src/LightRAGNet.React/src/app/App.tsx`:

```tsx
import { getApiBase } from "@/api/http";
import { DocumentsPage } from "@/features/documents/DocumentsPage";
import { UploadDocumentPage } from "@/features/documents/UploadDocumentPage";
import { AppLayout } from "./AppLayout";
import { getCurrentRoute } from "./router";
import "@/shared/styles/app.css";

export function App() {
  const currentRoute = getCurrentRoute();
  const apiBase = getApiBase();

  return (
    <AppLayout currentRoute={currentRoute}>
      {currentRoute === "/documents/upload"
        ? <UploadDocumentPage apiBase={apiBase} />
        : <DocumentsPage apiBase={apiBase} />}
    </AppLayout>
  );
}
```

Append table styles to `src/LightRAGNet.React/src/shared/styles/app.css`:

```css
.document-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.toolbar {
  padding: 14px;
}

.toolbar label {
  display: inline-grid;
  gap: 6px;
  color: var(--text-secondary);
}

select,
button {
  border: 1px solid var(--control-border);
  border-radius: 8px;
  color: var(--text-primary);
  background: var(--control-bg);
  padding: 8px 10px;
}

.secondary-button {
  border: 1px solid var(--control-border);
  border-radius: 8px;
  padding: 9px 12px;
  color: var(--text-primary);
  background: var(--control-bg);
}

.empty-state,
.table-panel {
  padding: 16px;
}

.documents-table {
  width: 100%;
  border-collapse: collapse;
}

.documents-table th,
.documents-table td {
  border-bottom: 1px solid var(--panel-border);
  padding: 12px;
  text-align: left;
  vertical-align: top;
}

.documents-table th {
  color: var(--text-secondary);
  font-size: 12px;
  text-transform: uppercase;
}

.status-chip {
  display: inline-flex;
  border: 1px solid var(--panel-border);
  border-radius: 999px;
  padding: 4px 8px;
  color: var(--text-secondary);
}

.status-Completed {
  color: var(--success);
}

.status-Failed,
.status-DeletionFailed {
  color: var(--danger);
}

.status-Queued,
.status-Pending,
.status-Processing {
  color: var(--warning);
}

.progress-line {
  height: 6px;
  margin-top: 8px;
  overflow: hidden;
  border-radius: 999px;
  background: rgba(255, 255, 255, .08);
}

.progress-line span {
  display: block;
  height: 100%;
  background: var(--accent);
}

.action-cell {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.table-footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  padding-top: 14px;
}
```

- [ ] **Step 4: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsPage.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add react documents list"
```

---

### Task 6: Add Document Actions And Preview Panel

**Files:**
- Create: `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentActions.test.tsx`

- [ ] **Step 1: Write action tests**

Create `src/LightRAGNet.React/tests/integration/features/documents/DocumentActions.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DocumentsPage } from "@/features/documents/DocumentsPage";

const page = {
  items: [{
    id: 1,
    fileName: "guide.md",
    fileSize: 1024,
    uploadTime: "2026-05-24T10:00:00Z",
    isInRagSystem: false,
    ragStatus: null,
    ragProgress: 0,
    ragRetryCount: 0,
    fileUrl: "/uploads/guide.md"
  }],
  totalCount: 1,
  page: 1,
  pageSize: 10,
  totalPages: 1
};

describe("Document actions", () => {
  it("adds a document to RAG and updates the row", async () => {
    const loadDocuments = vi.fn().mockResolvedValue(page);
    const addToRag = vi.fn().mockResolvedValue({ ...page.items[0], ragStatus: "Pending", ragProgress: 0 });

    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={loadDocuments} addToRag={addToRag} />);

    await screen.findByText("guide.md");
    await userEvent.click(screen.getByRole("button", { name: /Add to RAG/i }));

    expect(addToRag).toHaveBeenCalledWith("http://localhost:5261", 1);
    expect(await screen.findByText(/Pending/i)).toBeInTheDocument();
  });

  it("opens document preview content", async () => {
    const loadDocuments = vi.fn().mockResolvedValue(page);
    const loadDocument = vi.fn().mockResolvedValue({ ...page.items[0], content: "# Guide" });

    render(<DocumentsPage apiBase="http://localhost:5261" loadDocuments={loadDocuments} loadDocument={loadDocument} />);

    await screen.findByText("guide.md");
    await userEvent.click(screen.getByRole("button", { name: /View guide.md/i }));

    expect(await screen.findByText("# Guide")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentActions.test.tsx
```

Expected: FAIL because `DocumentsPage` does not yet expose injectable actions or preview behavior.

- [ ] **Step 3: Implement preview panel**

Create `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`:

```tsx
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { MarkdownDocumentDto } from "./documentTypes";
import { formatDateTime, formatFileSize } from "./documentFormatters";

type DocumentPreviewPanelProps = {
  document: MarkdownDocumentDto;
  onClose: () => void;
};

export function DocumentPreviewPanel({ document, onClose }: DocumentPreviewPanelProps) {
  return (
    <aside className="preview-panel panel" aria-label="Document preview">
      <header className="preview-header">
        <div>
          <h2>{document.fileName}</h2>
          <p>{formatFileSize(document.fileSize)} · {formatDateTime(document.uploadTime)}</p>
        </div>
        <button type="button" onClick={onClose}>Close</button>
      </header>
      {document.content && document.content.trim().length > 0
        ? <ReactMarkdown remarkPlugins={[remarkGfm]}>{document.content}</ReactMarkdown>
        : <div className="empty-state">Document content is empty.</div>}
    </aside>
  );
}
```

- [ ] **Step 4: Extend `DocumentsPage` actions**

Modify `DocumentsPage` props to accept action functions:

```tsx
type DocumentsPageProps = {
  apiBase: string;
  loadDocuments?: typeof defaultLoadDocuments;
  loadDocument?: typeof getMarkdownDocument;
  addToRag?: typeof addToRagSystem;
  retry?: typeof retryDocument;
  cancelPipeline?: typeof cancelDocumentPipeline;
  removeDocument?: typeof deleteMarkdownDocument;
};
```

Add imports:

```tsx
import {
  addToRagSystem,
  cancelDocumentPipeline,
  deleteMarkdownDocument,
  getMarkdownDocument,
  getMarkdownDocuments as defaultLoadDocuments,
  retryDocument
} from "@/api/documentsApi";
import { DocumentPreviewPanel } from "./DocumentPreviewPanel";
import { canCancelDocumentPipeline, canRetryDocument, isDocumentBusy } from "./documentStatus";
```

Add state:

```tsx
const [previewDocument, setPreviewDocument] = useState<MarkdownDocumentDto | null>(null);
```

Add row update helper:

```tsx
function updateDocument(id: number, update: Partial<MarkdownDocumentDto>) {
  setData(current => current
    ? { ...current, items: current.items.map(item => item.id === id ? { ...item, ...update } : item) }
    : current);
}
```

Add action handlers:

```tsx
async function viewDocument(document: MarkdownDocumentDto) {
  const loaded = await loadDocument(apiBase, document.id);
  setPreviewDocument(loaded);
}

async function addDocumentToRag(document: MarkdownDocumentDto) {
  const updated = await addToRag(apiBase, document.id);
  updateDocument(document.id, updated);
}

async function retryDocumentPipeline(document: MarkdownDocumentDto) {
  const result = await retry(apiBase, document.id);
  if (result.accepted) {
    updateDocument(document.id, { ragStatus: result.status, ragErrorMessage: null, ragCurrentStage: result.status, ragProgress: result.status === "Queued" ? 0 : document.ragProgress });
  }
}

async function cancelPipelineForDocument(document: MarkdownDocumentDto) {
  const result = await cancelPipeline(apiBase, document.id);
  if (result.accepted) {
    updateDocument(document.id, { ragStatus: result.status, ragErrorMessage: null, ragCurrentStage: result.status });
  }
}

async function deleteDocument(document: MarkdownDocumentDto) {
  if (!window.confirm("Delete this document?")) {
    return;
  }

  const previous = document;
  updateDocument(document.id, { ragStatus: "Deleting", ragErrorMessage: null });
  const result = await removeDocument(apiBase, document.id);

  if (result.deletedImmediately) {
    setData(current => current
      ? { ...current, totalCount: Math.max(0, current.totalCount - 1), items: current.items.filter(item => item.id !== document.id) }
      : current);
    return;
  }

  if (result.accepted) {
    updateDocument(document.id, { ragStatus: "Deleting" });
    return;
  }

  updateDocument(document.id, previous);
}
```

Replace action buttons inside the row:

```tsx
<button type="button" aria-label={`View ${document.fileName}`} onClick={() => void viewDocument(document)}>View</button>
{document.fileUrl?.startsWith("/uploads/") && <a className="secondary-button" href={`${apiBase.replace(/\/+$/, "")}${document.fileUrl}`} target="_blank" rel="noreferrer">Download</a>}
{!document.isInRagSystem && <button type="button" disabled={isDocumentBusy(document)} onClick={() => void addDocumentToRag(document)}>Add to RAG</button>}
{canRetryDocument(document) && <button type="button" onClick={() => void retryDocumentPipeline(document)}>Retry</button>}
{canCancelDocumentPipeline(document) && <button type="button" onClick={() => void cancelPipelineForDocument(document)}>Cancel</button>}
<button type="button" disabled={isDocumentBusy(document)} onClick={() => void deleteDocument(document)}>Delete</button>
```

Render preview after the table section:

```tsx
{previewDocument && <DocumentPreviewPanel document={previewDocument} onClose={() => setPreviewDocument(null)} />}
```

- [ ] **Step 5: Add preview styles**

Append to `src/LightRAGNet.React/src/shared/styles/app.css`:

```css
.preview-panel {
  padding: 18px;
}

.preview-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  border-bottom: 1px solid var(--panel-border);
  padding-bottom: 12px;
  margin-bottom: 12px;
}

.preview-header h2 {
  margin: 0 0 4px;
}

.preview-header p {
  margin: 0;
  color: var(--text-secondary);
}
```

- [ ] **Step 6: Verify and commit**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentActions.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: both commands PASS.

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: add react document actions"
```

---

### Task 7: Add Document List Lifecycle Refresh Parity

**Files:**
- Modify: `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
- Modify: `src/LightRAGNet.React/src/features/documents/documentStatus.ts`
- Test: `src/LightRAGNet.React/tests/unit/features/documents/documentRefreshPolicy.test.ts`
- Test: `src/LightRAGNet.React/tests/integration/features/documents/DocumentTaskRefresh.test.tsx`

This task closes the highest-risk parity gap. The Blazor document list is not a simple CRUD table. It is a lifecycle console with local task mutation, debounced server reloads, active-status filter boundary logic, data-cleared refresh, and row-level rollback behavior. Implement this task before considering the document list migrated.

- [ ] **Step 1: Write refresh policy unit tests**

Create `src/LightRAGNet.React/tests/unit/features/documents/documentRefreshPolicy.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import {
  shouldRefreshForMissingTaskStatus,
  shouldRefreshForTaskStatus
} from "@/features/documents/documentStatus";

describe("document refresh policy", () => {
  it("refreshes when a visible task crosses the selected status filter boundary", () => {
    expect(shouldRefreshForTaskStatus({ status: "Completed" }, "Processing", "Completed")).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: "Processing" }, "Processing", "Processing")).toBe(false);
  });

  it("refreshes when a missing task update now matches the selected filter", () => {
    expect(shouldRefreshForMissingTaskStatus({ status: "Queued" }, "Queued")).toBe(true);
    expect(shouldRefreshForMissingTaskStatus({ status: "Completed" }, "Queued")).toBe(false);
  });

  it("refreshes final active statuses even when no filter is selected", () => {
    expect(shouldRefreshForTaskStatus({ status: "Completed" }, "Processing", undefined)).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: "Failed" }, "Pending", undefined)).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: "Queued" }, "Pending", undefined)).toBe(false);
  });
});
```

- [ ] **Step 2: Write integration tests for SignalR-style updates**

Create `src/LightRAGNet.React/tests/integration/features/documents/DocumentTaskRefresh.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DocumentsPage } from "@/features/documents/DocumentsPage";
import type { TaskStatusUpdate } from "@/features/documents/documentTypes";

const processingPage = {
  items: [{
    id: 7,
    fileName: "pipeline.pdf",
    fileSize: 4096,
    uploadTime: "2026-05-24T10:00:00Z",
    isInRagSystem: false,
    ragStatus: "Processing",
    ragProgress: 25,
    ragCurrentStage: "ProcessingChunks",
    ragRetryCount: 0,
    fileUrl: null
  }],
  totalCount: 1,
  page: 1,
  pageSize: 10,
  totalPages: 1
};

describe("Document task refresh", () => {
  it("applies task progress updates to the visible row", async () => {
    let taskHandler: ((update: TaskStatusUpdate) => void) | undefined;
    const loadDocuments = vi.fn().mockResolvedValue(processingPage);

    render(
      <DocumentsPage
        apiBase="http://localhost:5261"
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={(handler) => {
          taskHandler = handler;
          return () => undefined;
        }}
      />
    );

    await screen.findByText("pipeline.pdf");
    taskHandler?.({ documentId: 7, status: "Processing", currentStage: "MergingEntities", progress: 60 });

    expect(await screen.findByText(/Processing \/ MergingEntities/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Progress 60%")).toBeInTheDocument();
  });

  it("reloads when data cleared is received", async () => {
    let dataClearedHandler: (() => void) | undefined;
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(processingPage)
      .mockResolvedValueOnce({ ...processingPage, items: [], totalCount: 0, totalPages: 0 });

    render(
      <DocumentsPage
        apiBase="http://localhost:5261"
        loadDocuments={loadDocuments}
        subscribeToDataCleared={(handler) => {
          dataClearedHandler = handler;
          return () => undefined;
        }}
      />
    );

    await screen.findByText("pipeline.pdf");
    dataClearedHandler?.();

    expect(await screen.findByText(/No documents yet/i)).toBeInTheDocument();
    expect(loadDocuments).toHaveBeenCalledTimes(2);
  });
});
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/features/documents/documentRefreshPolicy.test.ts tests/integration/features/documents/DocumentTaskRefresh.test.tsx
```

Expected: FAIL because refresh policy helpers and subscription props do not exist.

- [ ] **Step 4: Implement refresh policy helpers**

Extend `src/LightRAGNet.React/src/features/documents/documentStatus.ts`:

```ts
type TaskStatusLike = {
  status?: string | null;
};

export function shouldRefreshForTaskStatus(update: TaskStatusLike, oldStatus?: string | null, selectedStatusFilter?: string | null): boolean {
  if (selectedStatusFilter && normalizeFilterStatus(oldStatus) !== normalizeFilterStatus(update.status)) {
    return true;
  }

  return (update.status === "Completed" || update.status === "Failed") &&
    (oldStatus === "Processing" || oldStatus === "Pending");
}

export function shouldRefreshForMissingTaskStatus(update: TaskStatusLike, selectedStatusFilter?: string | null): boolean {
  return Boolean(selectedStatusFilter) &&
    normalizeFilterStatus(update.status) === normalizeFilterStatus(selectedStatusFilter);
}
```

- [ ] **Step 5: Add injectable subscriptions and debounced reload to `DocumentsPage`**

Extend `DocumentsPage` props:

```tsx
subscribeToTaskUpdates?: (handler: (update: TaskStatusUpdate) => void) => () => void;
subscribeToDataCleared?: (handler: () => void) => () => void;
```

Import helpers:

```tsx
import {
  shouldRefreshForMissingTaskStatus,
  shouldRefreshForTaskStatus
} from "./documentStatus";
import type { TaskStatusUpdate } from "./documentTypes";
```

Add a debounced refresh helper:

```tsx
const refreshTimerRef = useRef<number | undefined>();

const scheduleRefresh = useCallback(() => {
  window.clearTimeout(refreshTimerRef.current);
  refreshTimerRef.current = window.setTimeout(() => {
    void reload(page, status);
  }, 240);
}, [page, reload, status]);
```

Add local update application:

```tsx
function applyTaskStatusUpdate(update: TaskStatusUpdate): string | undefined {
  let oldStatus: string | undefined;

  setData(current => {
    if (!current) {
      return current;
    }

    let found = false;
    const items = current.items.map(document => {
      if (document.id !== update.documentId) {
        return document;
      }

      found = true;
      oldStatus = document.ragStatus ?? undefined;

      if (update.operationType === "DeleteDocument") {
        return {
          ...document,
          ragStatus: update.status === "Failed" ? "DeletionFailed" : "Deleting",
          ragErrorMessage: update.errorMessage,
          ragCurrentStage: update.currentStage
        };
      }

      return {
        ...document,
        ragStatus: update.status,
        ragCurrentStage: update.currentStage,
        ragProgress: update.currentStage === "ProcessingChunks" ||
          update.currentStage === "MergingEntities" ||
          update.currentStage === "MergingRelations"
          ? update.progress
          : document.ragProgress,
        isInRagSystem: update.status === "Completed" ? true : document.isInRagSystem,
        ragAddedTime: update.status === "Completed" ? update.completedAt ?? new Date().toISOString() : document.ragAddedTime
      };
    });

    return found ? { ...current, items } : current;
  });

  return oldStatus;
}
```

Add subscription effects:

```tsx
useEffect(() => {
  if (!subscribeToTaskUpdates) {
    return undefined;
  }

  return subscribeToTaskUpdates(update => {
    const oldStatus = applyTaskStatusUpdate(update);

    if (oldStatus === undefined) {
      if (shouldRefreshForMissingTaskStatus(update, status)) {
        scheduleRefresh();
      }
      return;
    }

    if (update.operationType === "DeleteDocument" && update.status === "Completed") {
      setData(current => current
        ? { ...current, totalCount: Math.max(0, current.totalCount - 1), items: current.items.filter(item => item.id !== update.documentId) }
        : current);
      scheduleRefresh();
      return;
    }

    if (shouldRefreshForTaskStatus(update, oldStatus, status)) {
      scheduleRefresh();
    }
  });
}, [scheduleRefresh, status, subscribeToTaskUpdates]);

useEffect(() => {
  if (!subscribeToDataCleared) {
    return undefined;
  }

  return subscribeToDataCleared(() => {
    setData(current => current ? { ...current, items: [], totalCount: 0, totalPages: 0 } : current);
    scheduleRefresh();
  });
}, [scheduleRefresh, subscribeToDataCleared]);
```

Clean up timer:

```tsx
useEffect(() => {
  return () => window.clearTimeout(refreshTimerRef.current);
}, []);
```

- [ ] **Step 6: Verify refresh parity**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/unit/features/documents/documentRefreshPolicy.test.ts tests/integration/features/documents/DocumentTaskRefresh.test.tsx
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: all commands PASS.

- [ ] **Step 7: Commit**

Commit:

```powershell
git add src/LightRAGNet.React
git commit -m "feat: preserve react document refresh parity"
```

---

### Task 8: Add Document List Parity Audit Test

**Files:**
- Create: `src/LightRAGNet.React/tests/integration/features/documents/DocumentsParityChecklist.test.ts`

This test is intentionally source-oriented. It prevents the migration from silently dropping actions that exist in the Blazor document list.

- [ ] **Step 1: Write source parity test**

Create `src/LightRAGNet.React/tests/integration/features/documents/DocumentsParityChecklist.test.ts`:

```ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readDocumentsPageSource(): string {
  return readFileSync(resolve(process.cwd(), "src/features/documents/DocumentsPage.tsx"), "utf8");
}

describe("DocumentsPage parity checklist", () => {
  it("keeps the document list lifecycle controls from the Blazor page", () => {
    const source = readDocumentsPageSource();

    expect(source).toContain("Status");
    expect(source).toContain("View");
    expect(source).toContain("Download");
    expect(source).toContain("Add to RAG");
    expect(source).toContain("Retry");
    expect(source).toContain("Cancel");
    expect(source).toContain("Delete");
    expect(source).toContain("Progress");
    expect(source).toContain("DeletionFailed");
    expect(source).toContain("subscribeToTaskUpdates");
    expect(source).toContain("subscribeToDataCleared");
    expect(source).toContain("shouldRefreshForTaskStatus");
    expect(source).toContain("shouldRefreshForMissingTaskStatus");
  });
});
```

- [ ] **Step 2: Run parity test**

Run:

```powershell
npm run test --prefix src/LightRAGNet.React -- tests/integration/features/documents/DocumentsParityChecklist.test.ts
```

Expected: PASS. If this fails, the missing string points to a parity item that must be implemented before moving on.

- [ ] **Step 3: Commit**

Commit:

```powershell
git add src/LightRAGNet.React/tests/integration/features/documents/DocumentsParityChecklist.test.ts
git commit -m "test: guard react document list parity"
```

---

### Task 9: Add CORS For React Dev Server And Final Verification

**Files:**
- Modify: `src/LightRAGNet.Server/Program.cs`
- Create: `tests/LightRAGNet.Server.Tests/ReactDevCorsSourceTests.cs`

- [ ] **Step 1: Write source test for React dev origins**

Create `tests/LightRAGNet.Server.Tests/ReactDevCorsSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class ReactDevCorsSourceTests
{
    [Fact]
    public void ServerCors_AllowsStandaloneReactDevServer()
    {
        var source = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Server/Program.cs"));

        source.Should().Contain("\"http://localhost:5173\"");
        source.Should().Contain("\"http://127.0.0.1:5173\"");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter ReactDevCorsSourceTests
```

Expected: FAIL because the Vite dev origins are not present.

- [ ] **Step 3: Add Vite origins to CORS**

Modify the `WithOrigins` call in `src/LightRAGNet.Server/Program.cs` to include the standalone React dev server:

```csharp
policy.WithOrigins(
        "https://localhost:7190",
        "http://localhost:5241",
        "https://localhost:7291",
        "http://localhost:5261",
        "http://localhost:5173",
        "http://127.0.0.1:5173")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials();
```

- [ ] **Step 4: Run targeted verification**

Run:

```powershell
dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter ReactDevCorsSourceTests
npm run test --prefix src/LightRAGNet.React
npm run typecheck --prefix src/LightRAGNet.React
npm run build --prefix src/LightRAGNet.React
```

Expected: all commands PASS.

- [ ] **Step 5: Run full .NET verification**

Run:

```powershell
dotnet test LightRAGNet.slnx
```

Expected: PASS. If unrelated long-running integration dependencies block the full run, record the exact failing tests and still run the targeted Server and React commands above.

- [ ] **Step 6: Manual browser check**

Run backend:

```powershell
dotnet run --project src\LightRAGNet.Server
```

Run frontend in another terminal:

```powershell
npm run dev --prefix src\LightRAGNet.React
```

Open:

```text
http://localhost:5173/documents/upload
http://localhost:5173/documents
```

Expected:

- Upload page renders with dark shared styling.
- Upload accepts `.md`, `.markdown`, `.pdf`, `.docx`.
- Upload rejects `.exe` and files larger than 10 MB.
- Document list loads from `LightRAGNet.Server`.
- Status filter calls the backend.
- View/Add to RAG/Retry/Cancel/Delete controls render according to document state.
- `src/LightRAGNet.Web` remains untouched and can still run separately.

- [ ] **Step 7: Commit**

Commit:

```powershell
git add src/LightRAGNet.Server/Program.cs tests/LightRAGNet.Server.Tests/ReactDevCorsSourceTests.cs src/LightRAGNet.React
git commit -m "feat: enable standalone react document workflow"
```

---

## Final Closeout

- [ ] Run:

```powershell
git status --short
```

Expected: clean working tree except intentionally uncommitted runtime files.

- [ ] Confirm no test files exist under React production source:

```powershell
Get-ChildItem -Path src\LightRAGNet.React\src -Recurse -File -Include *.test.ts,*.test.tsx,*.spec.ts,*.spec.tsx
```

Expected: no output.

- [ ] Confirm Blazor project files were not modified:

```powershell
git diff --name-only HEAD~9..HEAD -- src/LightRAGNet.Web tests/LightRAGNet.Web.Tests tests/LightRAGNet.Tests/Web
```

Expected: no output.

- [ ] Run the asset-compounding gate before final response because this is meaningful development work:

```powershell
python <compound-development-asset>/scripts/asset_closeout.py . --topic "react-standalone-documents-migration" --json
```

Expected: route decision is recorded in the final handoff. If implementation is only partially complete, defer archive creation and report the remaining task boundary.
