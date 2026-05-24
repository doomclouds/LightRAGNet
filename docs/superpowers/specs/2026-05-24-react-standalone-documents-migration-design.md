# React Standalone Documents Migration Design

- Date: 2026-05-24
- Topic slug: `react-standalone-documents-migration`
- Scope: `Create standalone React frontend project and migrate document upload/list pages`
- Tags: `react`, `vite`, `documents`, `frontend-migration`, `blazor-coexistence`, `testing`

## Context

LightRAGNet currently has two frontend shapes:

- `src/LightRAGNet.Web`: Blazor Server app with MudBlazor shell, navigation, SignalR status bar, document upload/list pages, and React island hosts.
- `src/LightRAGNet.Web/ClientApp`: Vite/React code for existing React islands such as Graph Workbench, System Status, and Cache Management.

Another agent is already working on React style standardization and RAG Chat migration. That work should not be disrupted by this slice.

The next frontend direction is now clarified:

- `LightRAGNet.Server` remains the backend service.
- React is a separate frontend service.
- A new standalone React project should be created under `src/LightRAGNet.React`.
- The Blazor project should remain untouched in this phase.
- This phase migrates only document upload and document list functionality into the new React project.

## Goals

1. Create `src/LightRAGNet.React` as a standalone Vite/React frontend project.
2. Keep React production code and React tests in separate directory trees.
3. Migrate or prepare to consume the shared style/theme work produced by the parallel React standardization agent.
4. Build React routes for document upload and document list.
5. Preserve the current document workflow behavior from Blazor.
6. Keep `src/LightRAGNet.Web` unchanged and still usable during this phase.
7. Allow independent frontend/backend development startup.

## Non-Goals

- Do not delete `src/LightRAGNet.Web`.
- Do not remove Blazor routes, navigation, MudBlazor dependencies, or Blazor tests.
- Do not migrate Graph Workbench, System Status, Cache Management, or RAG Chat in this slice.
- Do not change existing backend API semantics unless the React migration exposes a required gap.
- Do not make `LightRAGNet.Server` host the React static build in this slice.
- Do not introduce theme switching UI.

## Project Shape

Create a pure frontend project:

```text
src/LightRAGNet.React/
  package.json
  package-lock.json
  vite.config.ts
  tsconfig.json
  index.html
  public/
  src/
    app/
      App.tsx
      AppLayout.tsx
      router.tsx
    api/
      http.ts
      documentsApi.ts
      ragTaskHubClient.ts
    features/
      documents/
        DocumentsPage.tsx
        UploadDocumentPage.tsx
        DocumentPreviewPanel.tsx
        documentActions.ts
        documentFormatters.ts
        documentStatus.ts
        documentTypes.ts
    shared/
      components/
      hooks/
      styles/
      utils/
  tests/
    unit/
      api/
      features/
      shared/
    integration/
      features/
    e2e/
    setup/
      vitest.setup.ts
```

`LightRAGNet.React` should not have a `.csproj`. It is not part of the .NET project graph.

## Startup Model

Backend:

```powershell
dotnet run --project src/LightRAGNet.Server
```

Frontend:

```powershell
npm run dev --prefix src/LightRAGNet.React
```

The React app reads the backend API base URL from Vite environment configuration. The default development value is:

```text
http://localhost:5261
```

SignalR also connects to the same backend base:

```text
http://localhost:5261/hubs/ragtask
```

## Routing

The standalone React frontend should use clean product routes rather than the old Blazor route names:

```text
/documents         Document list
/documents/upload  Upload document
```

The first slice may redirect `/` to `/documents` until RAG Chat is migrated into the standalone React app.

## Style Migration

Before implementing document pages, the new React project must check for style artifacts from the parallel React standardization work.

Priority order:

1. If shared theme tokens, common CSS, or common components already exist in the current worktree, migrate or copy them into `src/LightRAGNet.React/src/shared`.
2. If that work is still not present, create the expected receiving structure under `shared/styles` and use the accepted `dark-ops` theme direction from the existing React standardization spec.
3. Document upload and document list pages must use the shared style layer from the start.

The new project should not create a competing visual system.

## Testing Layout

React tests must not be colocated with production source files.

Rules:

- `src/` contains production code only.
- `tests/` contains all Vitest and Playwright test files.
- Unit tests mirror the source structure under `tests/unit`.
- Component and page integration tests live under `tests/integration`.
- End-to-end browser flows live under `tests/e2e`.
- Test setup lives under `tests/setup`.

Examples:

```text
src/features/documents/DocumentsPage.tsx
tests/integration/features/documents/DocumentsPage.test.tsx

src/api/documentsApi.ts
tests/unit/api/documentsApi.test.ts
```

Use an import alias such as `@/` for production imports to avoid fragile deep relative paths.

## API Client

Create `documentsApi.ts` around existing backend endpoints:

```http
GET    /api/MarkdownDocuments?page={page}&pageSize={pageSize}&status={status}
GET    /api/MarkdownDocuments/{id}
POST   /api/MarkdownDocuments/upload
POST   /api/MarkdownDocuments/{id}/add-to-rag
POST   /api/MarkdownDocuments/{id}/retry
POST   /api/MarkdownDocuments/{id}/cancel
DELETE /api/MarkdownDocuments/{id}?deleteLlmCache=false
```

The React client should preserve backend error messages by reading `message`, `error`, or `title` JSON fields before falling back to status text.

## SignalR Client

The document list page needs a React-side SignalR client because the Blazor `RagTaskNotificationService` will not be available.

The client must handle:

- connection state
- reconnecting state
- server not reachable
- `TaskStatusUpdated`
- `DataCleared`

The first implementation can scope subscriptions to the document pages, but the API should be reusable by future React pages.

## Upload Page

Route:

```text
/documents/upload
```

Behavior to preserve:

- Accept `.md`, `.markdown`, `.pdf`, and `.docx`.
- Maximum 10 files per batch.
- Maximum 10 MB per file.
- Reject unsupported extensions before upload.
- Reject oversized files before upload.
- Avoid duplicate selected file names within the local selection.
- Submit all selected files in one `multipart/form-data` request to `/api/MarkdownDocuments/upload`.
- Use form field name `files`.
- Show batch upload progress/state.
- On success, show the uploaded count and allow navigation to `/documents`.
- Do not automatically add uploaded documents to RAG.
- Copy should make clear that `Add to RAG` starts conversion/indexing later.

## Document List Page

Route:

```text
/documents
```

Behavior to preserve:

- Paged server-side document list.
- Status filter.
- Empty state.
- Loading state.
- Network/server unavailable state.
- File name, file size, upload time, and RAG status.
- Progress bar for active processing stages.
- Error summary for failed/deletion-failed states.
- Added time when available.

Actions:

- View document details.
- Download when `FileUrl` is a safe downloadable URL.
- Add to RAG when the document is not already in RAG.
- Retry when status is `Failed` or `Cancelled`.
- Cancel when status is `Queued`, `Processing`, or `Pending`.
- Delete when document is not busy.

Delete behavior:

- Ask for confirmation before deleting.
- Optimistically show deleting state only for the affected row.
- Restore previous state if the API returns conflict or failure.
- Remove immediately deleted rows from the current page.
- Keep queued deletion rows visible as `Deleting`.

Refresh behavior:

- Apply SignalR task updates locally when the document is on the current page.
- Refresh the current page when a task crosses the active status filter boundary.
- Refresh after `DataCleared`.
- Debounce refreshes to avoid reload storms during task progress events.

## Document Details And Preview

This slice should provide a React replacement for the current Blazor document view dialog.

Minimum behavior:

- Fetch `GET /api/MarkdownDocuments/{id}`.
- Show file metadata.
- Render Markdown/text content when present.
- Show a clear empty-content state.
- Keep the implementation inside the React frontend; do not depend on MudBlazor.

PDF/DOCX artifact preview can remain limited in this slice unless the current API already exposes the required content. The broader safe document preview contract belongs to the existing RAG Chat/reference preview design.

## Compatibility With Existing Blazor App

`src/LightRAGNet.Web` remains unchanged during this phase.

This means:

- Existing Blazor document pages continue to work.
- Existing `src/LightRAGNet.Web/ClientApp` remains in place.
- Existing Blazor host source tests remain in place.
- New React project introduces new tests under `src/LightRAGNet.React/tests`.

There may temporarily be duplicated document UI logic between Blazor and React. That duplication is intentional for this transition slice.

## Build And Verification

New frontend scripts:

```json
{
  "dev": "vite",
  "build": "tsc --noEmit --pretty false && vite build",
  "typecheck": "tsc --noEmit",
  "test": "vitest run"
}
```

Verification commands:

```powershell
npm run typecheck --prefix src/LightRAGNet.React
npm run test --prefix src/LightRAGNet.React
npm run build --prefix src/LightRAGNet.React
dotnet test LightRAGNet.slnx
```

Visual/manual checks:

- Start `LightRAGNet.Server`.
- Start `LightRAGNet.React`.
- Open `/documents/upload`.
- Upload supported files.
- Open `/documents`.
- Confirm upload results appear.
- Add a document to RAG and observe status updates.
- Retry/cancel/delete eligible documents.
- Confirm Blazor app still starts separately.

## Acceptance Criteria

- `src/LightRAGNet.React` exists as an independent Vite/React project.
- React source and tests are separated into `src/` and `tests/`.
- The new project has a receiving layer for shared theme/style artifacts.
- `/documents/upload` supports batch upload through the existing backend API.
- `/documents` supports paged list, filtering, document actions, and task status refresh.
- React SignalR client receives task updates and data-cleared notifications.
- The Blazor project is not deleted or modified as part of this phase.
- New React typecheck, tests, and build pass.
- Existing .NET tests still pass.

## Open Implementation Notes

- If the parallel style standardization work lands before implementation starts, migrate its concrete files first.
- If it lands after this design but before implementation finishes, reconcile document page styles with that style layer before completion.
- If backend CORS does not include the Vite dev port for `LightRAGNet.React`, add the minimal allowed origin needed for local development.
