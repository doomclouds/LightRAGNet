# React UI Standardization and RAG Chat Workbench Design

- Date: 2026-05-24
- Topic slug: `react-ui-standardization-rag-chat-workbench`
- Scope: `React ClientApp design system + RAG Chat React migration + document reference preview contract`
- Tags: `react-island`, `rag-chat`, `design-system`, `dark-ops`, `references`, `diagnostics`, `python-parity`

## Context

LightRAGNet already has several React islands under `src/LightRAGNet.Web/ClientApp`:

- Knowledge Graph workbench
- System Status
- Cache Management

These pages currently do not share a visual system. Cache Management is the strongest existing style: dark, restrained, operations-focused, and readable in the user's dark-mode environment. Knowledge Graph and System Status still use lighter styles, so the React product surface feels inconsistent.

The current RAG Chat page is still Blazor + MudBlazor. It supports query modes, streaming/cacheable answers, references, diagnostics, and message-level retrieval data, but the page should now move into React while preserving its lightweight conversation model.

This phase should not create a heavy three-column query cockpit. The accepted direction is a two-column React chat workbench:

- left: conversation
- right: query settings

Detailed diagnostics stay attached to each assistant reply through a button/dialog, matching the current interaction pattern.

## Goals

1. Establish a shared React design standard based on Cache Management.
2. Treat the Cache Management visual language as the default theme skin, named `dark-ops`.
3. Refactor existing React pages to use the shared theme tokens and common visual rules.
4. Replace the Blazor RAG Chat page with a React RAG Chat Workbench.
5. Preserve current chat capabilities: streaming, query modes, references, diagnostics, retrieval data, errors, clear history, and debug output.
6. Make assistant message references clickable and open a browser preview in a new tab.
7. Move reference rendering responsibility from the LLM prompt into structured backend metadata and UI.
8. Handle PDF/DOCX artifact references explicitly, not as ordinary `/uploads` files.

## Non-Goals

- Do not build full query-run persistence.
- Do not build multi-run comparison or evaluation dashboards.
- Do not implement a theme-switching UI in this phase.
- Do not migrate the whole Blazor shell to React.
- Do not expose arbitrary server file paths to the browser.
- Do not rely on the LLM to generate final `### References` markdown sections.

## Theme And Design System

Create a shared React theme layer for `ClientApp`.

The first skin is `dark-ops`, derived from Cache Management:

| Token | Default |
| --- | --- |
| `--app-bg` | `#0d1117` |
| `--panel-bg` | `#151b23` |
| `--panel-border` | `#303946` |
| `--text-primary` | `#edf2f7` |
| `--text-secondary` | `#a9b4c2` |
| `--accent` | `#4cc9f0` |
| `--danger` | `#ff6b6b` |
| `--warning` | `#f6c85f` |
| `--success` | `#7bd88f` |
| `--control-bg` | `#151b23` |
| `--control-border` | `#303946` |
| `--shadow-panel` | `0 18px 42px rgba(0, 0, 0, .24)` |

Implementation should prefer semantic tokens over hard-coded page-local colors. Page CSS may define layout-specific dimensions, but common colors, borders, shadows, text tones, and control surfaces should come from the shared theme.

The design system should include shared primitives or CSS classes for:

- app shell
- page header
- panel
- panel header
- toolbar
- button
- icon button
- text input
- textarea
- select
- checkbox/toggle
- segmented control
- chip/badge
- table/list
- dialog/drawer surface
- loading, empty, and error states
- raw JSON/code block surface

This phase only needs one active skin, but the token structure must allow future theme switching without rewriting each page.

## React Page Standardization

All React pages should align to `dark-ops`.

### Cache Management

Cache Management is the baseline. It can keep its current layout, but should gradually move from page-local colors to shared tokens and common classes.

### Knowledge Graph

The graph canvas may keep a distinct immersive feel, but its surrounding UI must align:

- query controls
- search box
- settings panel
- layout controls
- viewport controls
- legend
- properties panel
- dialogs
- error/loading states

These surfaces should use dark panels, shared borders, shared button styles, and readable dark-mode contrast. The graph itself must not become hard to read; node/edge colors may remain graph-specific where needed.

### System Status

System Status should move from the current light diagnostics page to the shared dark operations style while preserving:

- evidence
- remediation
- fix-first priorities
- feature impact
- raw JSON copy/export behavior

### RAG Chat

The new React chat page should use the shared system from the start.

## RAG Chat Workbench UX

The React RAG Chat Workbench keeps a lightweight two-column layout.

### Left Column: Chat Pane

The chat pane contains:

- user messages
- assistant messages
- streaming answer content
- markdown rendering
- references under assistant messages
- mode/status chips
- error display
- `View query details` action per assistant message
- bottom composer
- clear history action

The composer should support:

- Enter to send
- Shift+Enter for newline
- disabled state while a response is active
- visible error state for invalid input

Python WebUI ideas may be borrowed selectively:

- mode prefix such as `/mix question`
- user prompt
- history turns
- thinking block rendering
- better markdown/code/math/mermaid rendering

These are allowed only if they fit the lightweight chat model. They should not turn the page into a heavy notebook in the first slice.

### Right Column: Query Settings

The settings panel contains:

- mode
- response type
- streaming/cacheable
- references
- rerank
- TopK
- ChunkTopK
- high-level keywords
- low-level keywords
- output mode: Answer, Context only, Prompt only

The right panel should not switch into a permanent run inspector. Diagnostics stay message-scoped.

### Message-Level Details

Each assistant reply should keep a dedicated details action. The current behavior of opening a detailed diagnostics dialog must be preserved.

The dialog should include:

- request snapshot
- mode
- stream/cacheable state
- cache policy
- response type
- high-level keywords
- low-level keywords
- references
- entities
- relationships
- chunks
- metadata diagnostics
- raw retrieval data JSON
- raw request/metadata JSON where useful

This dialog is the main place for detailed diagnostics. The chat stream should stay readable and not permanently display large raw data.

## Reference Preview Contract

Assistant message references should be clickable when a safe preview is available.

Do not let the React frontend guess links from `filePath`. The backend must resolve references and return preview metadata.

Extend `RagQueryReferenceDto` from:

```csharp
ReferenceId
FilePath
```

to:

```csharp
ReferenceId
FilePath
FileName
PreviewUrl
OpenKind
```

`PreviewUrl` is optional. If it is null, the frontend renders a non-clickable source label.

Suggested `OpenKind` values:

- `UploadedFile`
- `DocumentPreview`
- `ConvertedMarkdown`
- `OriginalArtifact`
- `ExternalOrUnresolved`

The frontend renders references as links only when `PreviewUrl` is present:

```tsx
<a href={reference.previewUrl} target="_blank" rel="noreferrer">
  {reference.fileName}
</a>
```

## Document Preview Rules

Opening a reference should open a new browser tab.

The preview contract uses a browser page route plus controlled content routes:

```http
GET /document-preview/{documentId}
GET /api/document-preview/{documentId}/content
GET /api/document-preview/{documentId}/original
```

`PreviewUrl` in `RagQueryReferenceDto` should point to `/document-preview/{documentId}` when a matching document is found. The preview page can then call the API routes to load converted Markdown, text content, or original artifacts. The safety rules are fixed:

- Never expose arbitrary physical server paths.
- Resolve references through known `MarkdownDocuments` records or trusted artifact metadata.
- Validate that upload paths stay inside the configured upload folder.
- Validate that artifact paths stay inside `DocumentArtifactStoreOptions.RootPath`.
- Return no preview URL if the reference cannot be matched safely.

Source differences matter:

- Legacy Markdown upload may have `/uploads/{fileName}`.
- Text submissions may have `text://{trackId}/{fileName}`.
- New uploaded PDF/DOCX records may have `upload://{trackId}/{fileName}` as logical source URI.
- PDF/DOCX artifacts live under the document artifact store, for example `documents/{id}/original.pdf`, `documents/{id}/original.docx`, and `documents/{id}/converted.md`.

Preview behavior:

- Markdown/text documents: render a browser preview page with document content.
- PDF artifacts: the preview page displays an embedded PDF using `/api/document-preview/{documentId}/original`.
- DOCX artifacts: the preview page displays converted Markdown first; it may include a secondary original download/open action backed by `/api/document-preview/{documentId}/original`.
- Converted documents: show converted Markdown clearly and identify the original file name.
- Unresolved references: render as plain text with no fake link.

## Prompt Cleanup

The RAG answer prompt currently asks the model to generate a final `### References` section and Markdown links. This becomes redundant once references are structured and rendered by the UI.

Update the prompt strategy:

- Keep grounding rules: answer only from the provided context.
- Keep formatting and language rules.
- Remove the strict final `### References` section requirement.
- Remove the instruction asking the model to create Markdown reference links.
- Keep or add a short rule that the assistant should not invent citations, file paths, or links.
- References are displayed by the system UI from structured metadata.

Expected result: assistant answers are cleaner, and reference rendering is deterministic.

## API And Data Flow

### Query Flow

1. React builds a `RagQueryRequest` from current settings and input.
2. React posts to `/api/RagQuery/query`.
3. Server streams `RagQueryEvent` values.
4. React appends text chunks to the assistant message.
5. Server sends metadata with enriched references.
6. React attaches metadata, references, diagnostics, and request snapshot to the assistant message.
7. User can click reference links or open message details.

### Retrieval Data Flow

1. User clicks `View query details`.
2. React uses the stored request snapshot.
3. React calls `/api/RagQuery/data`.
4. Dialog renders grouped entities, relationships, chunks, references, metadata, and raw JSON.

### Preview Flow

1. User clicks a reference with `previewUrl`.
2. Browser opens a new tab.
3. Preview endpoint resolves a safe document or artifact.
4. The preview page renders Markdown/text/PDF or converted Markdown for DOCX.

## Testing Strategy

### Backend Tests

Add or update tests for:

- `RagQueryReferenceDto` metadata mapping includes `fileName`, `previewUrl`, and `openKind`.
- reference resolver handles `/uploads/{fileName}` safely.
- reference resolver handles `upload://` PDF/DOCX logical source URIs.
- reference resolver handles converted Markdown artifacts.
- unresolved or unsafe paths do not produce preview URLs.
- prompt no longer contains the strict `### References` contract.
- preview routes reject traversal and unknown files.

### Frontend Tests

Add Vitest/source tests for:

- React RAG Chat mounts and accepts `apiBase`.
- query request body matches settings.
- SSE text chunks update the assistant message.
- metadata updates references and diagnostics.
- reference links open with `target="_blank"` and `rel="noreferrer"`.
- non-previewable references render as text.
- `View query details` opens the diagnostics dialog.
- details dialog groups request, references, retrieval data, diagnostics, and raw JSON.
- shared theme tokens are imported by React page styles.

### Web Host Tests

Update Blazor host source tests:

- RAG Chat host imports `rag-chat/assets/rag-chat.js`.
- RAG Chat host includes `rag-chat/assets/rag-chat.css`.
- navigation still routes `RAG Chat` to `/`.

### Visual Verification

Use Playwright or equivalent browser checks before completion:

- desktop: graph, system status, cache management, and RAG chat are readable in dark style.
- narrow viewport: controls do not overlap or overflow.
- chat message links are visible and clickable.
- diagnostics dialog/raw JSON are readable.
- graph controls remain usable over the graph canvas.

## Migration Plan Shape

This design should be implemented in staged commits:

1. Shared theme tokens and common styling primitives.
2. Standardize existing React pages to `dark-ops`.
3. Add backend reference preview contract and prompt cleanup.
4. Add React RAG Chat island and Blazor host.
5. Add message-level details dialog and retrieval-data flow.
6. Run visual verification and archive the completed requirement.

## Acceptance Criteria

- All React pages use the `dark-ops` theme standard.
- Cache Management remains visually stable.
- Knowledge Graph controls and panels align to the shared dark style.
- System Status aligns to the shared dark style.
- RAG Chat runs as a React island hosted by Blazor.
- RAG Chat keeps current user-facing query capabilities.
- Assistant references are clickable when a safe preview exists.
- Clicking a reference opens a new tab preview.
- PDF/DOCX artifact sources are handled through artifact-aware preview resolution.
- Query details remain available from each assistant message.
- The RAG prompt no longer requires model-generated `### References` sections.
- No React page has unreadable dark-mode text, overlapping controls, or obvious layout breakage at desktop and narrow widths.
- Tests cover API contracts, host wiring, frontend behavior, and prompt cleanup.
