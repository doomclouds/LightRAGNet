# React Anthropic Light Shell Redesign Design

- Date: 2026-05-25
- Topic slug: `react-anthropic-light-shell-redesign`
- Scope: `Rebuild the standalone React application shell around the approved light prototype and redesign the three document-related pages first`
- Tags: `react`, `ui-shell`, `anthropic-light`, `documents`, `upload`, `preview`, `design-system`, `signalr`

## Context

The accepted visual baseline is:

- [app-frame-documents-drawer-prototype.html](../visuals/anthropic-light-workbench/app-frame-documents-drawer-prototype.html)
- [app-frame-documents-prototype.png](../visuals/anthropic-light-workbench/app-frame-documents-prototype.png)
- [app-frame-upload-prototype.png](../visuals/anthropic-light-workbench/app-frame-upload-prototype.png)
- [app-frame-preview-prototype.png](../visuals/anthropic-light-workbench/app-frame-preview-prototype.png)

This supersedes the earlier dark-only shell direction for the next UI redesign phase. The standalone React app should adopt the approved Anthropic-like light workbench style: warm ivory background, restrained terracotta accent, dense operational layout, real elevation, clear layer hierarchy, and standard iconography.

The current React app already contains routes and implementations for RAG Chat, Documents, Upload, Knowledge Graph, System Status, Cache Management, and Document Preview. This phase must not turn into a full-page rewrite. It is a shell and standards rebuild first, with the document workflow pages used as the first concrete page-level redesign.

## Goals

1. Rebuild the standalone React application shell to match the approved prototype.
2. Establish reusable UI standards before broad page implementation starts.
3. Use a standard React icon library rather than hand-drawn or placeholder icons.
4. Redesign only the three document-related pages in this phase:
   - `/documents`
   - `/documents/upload`
   - `/document-preview` and `/document-preview/:id`
5. Keep non-document feature pages on their existing content implementations for now.
6. Make navigation point to the existing route locations for untouched pages.
7. Preserve real current functionality; do not add decorative or fake controls.
8. Keep bottom status behavior meaningful, including SignalR connection state and app version.

## Non-Goals

- Do not redesign RAG Chat content in this phase.
- Do not redesign Knowledge Graph content in this phase.
- Do not redesign System Status content in this phase.
- Do not redesign Cache Management content in this phase.
- Do not change backend API contracts.
- Do not add theme switching.
- Do not add fake metrics, fake chips, fake filters, or fake commands just to fill space.
- Do not remove existing route coverage.

## Approved Visual Standard

The shell should feel like a quiet engineering workbench rather than a marketing page.

Use these visual tokens as the baseline:

```text
background          #fbfaf6
surface             #fffefa
surface-muted       #f7f3ea
surface-raised      #f0eadf
text-primary        #191817
text-secondary      #5f5a52
text-muted          #8f887d
border              #e5ded2
border-strong       #d7ccbd
accent              #c8552d
accent-strong       #a94221
accent-soft         #f3e2d8
success             #4d8a58
warning             #c6871d
danger              #ce4c34
panel-shadow        0 18px 46px rgba(64, 46, 24, .08)
card-shadow         0 10px 24px rgba(64, 46, 24, .06)
drawer-shadow       0 28px 80px rgba(36, 31, 26, .22)
scrim               rgba(36, 31, 26, .30)
```

Radii should stay restrained:

- Shell panels and large surfaces: `12px` to `14px`
- Cards and table containers: `8px` to `10px`
- Buttons and nav rows: `8px` to `9px`
- Pills: `999px`

Typography:

- Use `Inter`, `Segoe UI`, `Microsoft YaHei`, `Arial`, sans-serif.
- Do not scale font size with viewport width.
- Keep letter spacing at `0`.
- Main page titles should be strong but not hero-sized.
- Table and sidebar text should prioritize scan efficiency.

## Shell Architecture

The shell layout is fixed conceptually:

```text
app-frame
  sidebar
    brand
    grouped navigation
    bottom realtime status
  main-shell
    topbar
    page content
```

### Sidebar

The left sidebar should use grouped navigation:

```text
Workspace
  RAG Chat
  Documents
  Knowledge Graph

Document Flow
  Upload Document
  Document Preview

Operations
  System Status
  Cache Management
```

Rules:

- Active state uses the warm accent background from the prototype.
- Each nav item uses a real icon from the chosen icon set.
- The top-left brand mark must be a real LightRAGNet-style logo mark, not a dot.
- The sidebar bottom area shows SignalR state and software version.
- The bottom area must not duplicate a full system status summary.

### Bottom Realtime Status

The sidebar footer replaces the old generic system status block:

```text
SignalR Connected
LightRAGNet v1.0.0
```

Connection states:

- `Connected`: green dot and success text.
- `Connecting` or `Reconnecting`: amber dot and reconnecting text.
- `Disconnected`: danger dot and disconnected text.

The implementation should read from the existing SignalR connection state already passed to `AppLayout`. If app version is available from package metadata or build env, use it; otherwise use the same visible version shown in the prototype until a version source is wired.

### Topbar

The topbar keeps global shell controls only:

- Context breadcrumb or current route label.
- Search/filter affordance only if backed by current page behavior.
- `Clear All Data` remains a global destructive action.
- No page-specific fake controls in the shell.

Page-specific actions belong inside the page header or toolbar.

## Icon Standard

Use `lucide-react` as the standard icon library for shell, tables, and controls.

Rules:

- Use icons for common commands: view, upload, download, refresh, retry, cancel, delete, search, filter, settings, external open, close.
- Prefer icon buttons with accessible labels for compact table actions.
- Do not hand-draw one-off SVG icons unless the icon library lacks a necessary symbol or the mark is the product logo.
- Keep icon stroke width consistent, defaulting to `1.8` or `2`.
- Icons should inherit text color unless a semantic state requires a token color.

## Elevation And Overlays

The new UI must not be completely flat.

Layering rules:

- Page surface: no shadow or extremely subtle background separation.
- Panels/cards/table containers: light border plus `card-shadow`.
- Floating menus/popovers: stronger border and shadow.
- Drawers/modals: scrim plus `drawer-shadow`.
- Destructive confirmation dialogs: modal layer with scrim and explicit danger action.

Drawer and modal overlays should keep the background recognizable, not blacked out.

## Shared Components To Establish

Create or refactor toward a small shared layer before redesigning more pages:

- `AppLayout`
- `AppSidebar`
- `AppTopbar`
- `AppStatusFooter`
- `PageHeader`
- `PageTabs`
- `Panel`
- `Toolbar`
- `Button`
- `IconButton`
- `StatusPill`
- `DataTable`
- `EmptyState`
- `ErrorState`
- `ConfirmDialog`
- `Drawer`

This is a standardization step, not an excuse to over-abstract every page. Components should be extracted only when at least two current pages need the pattern or when shell consistency depends on it.

## Document Pages

### Documents `/documents`

The document list page should be rebuilt against the approved shell style.

Must preserve current behavior:

- Server-side paged list.
- Status filter.
- Loading state.
- Empty state.
- Network error state.
- File name, file size, upload time, RAG status, progress, error summary, added time.
- View, Download, Add to RAG, Retry, Cancel, Delete.
- SignalR-driven refresh on task updates and data clearing.
- Delete confirmation and row-level pending state.

Design rules:

- Use summary cards only for real document state counts already available or derivable from current list response.
- Use a dense table with stable row height and compact icon actions.
- Use status pills matching the shared tone system.
- Table header, pagination footer, and action columns must not shift layout during loading or pending states.
- The page should not include a global preview drawer by default while the shell style is still being validated.

### Upload `/documents/upload`

Must preserve current behavior:

- Accepted extensions: `.md`, `.markdown`, `.pdf`, `.docx`.
- Maximum 10 files.
- Maximum 10 MB per file.
- Reject unsupported, duplicate, and oversized files locally.
- Upload as multipart field `files`.
- Upload success points users back to Documents for Add to RAG.
- Upload does not automatically Add to RAG.

Design rules:

- Dropzone should be a workbench control, not a hero card.
- Selected file list is visible and dense.
- Validation and upload errors use shared banner/row status patterns.
- The primary action is upload; secondary action is clear selection.

### Document Preview `/document-preview` And `/document-preview/:id`

Must preserve current behavior:

- Uses safe backend preview API.
- Renders returned `fileName`, `contentType`, and `content`.
- Shows an empty state when no document id is selected.
- Does not derive preview content from local file paths.

Design rules:

- Full preview page is a reading workspace.
- Header shows document identity and content type.
- Content surface uses readable line height and fixed-width code/markdown treatment where appropriate.
- Download/open actions appear only when backed by existing API behavior.

## Untouched Page Policy

For this phase, untouched pages should still route to their existing implementations:

```text
/                  existing RAG Chat
/rag-chat          existing RAG Chat
/graph-view        existing Knowledge Graph
/system-status     existing System Status
/cache-management  existing Cache Management
```

Allowed changes for untouched pages:

- They render inside the new shell.
- Their route labels and nav active states are corrected.
- Obvious shell padding conflicts may be adapted.

Disallowed changes for untouched pages:

- Rebuilding the page layout.
- Changing feature controls.
- Removing existing parameters.
- Changing graph buttons, graph panel semantics, or graph interactions.
- Re-skinning every nested component before the shell standard is accepted.

## Implementation Boundaries

Recommended implementation order after this spec is approved:

1. Add/replace design tokens in `shared/styles/theme.css`.
2. Rebuild `AppLayout` around the approved shell structure.
3. Update grouped navigation and route active logic.
4. Add shared shell/status/icon components.
5. Redesign Documents page with current functionality intact.
6. Redesign Upload page with current functionality intact.
7. Redesign Document Preview page with current functionality intact.
8. Run React unit/integration tests.
9. Run visual browser checks for the three document pages and one untouched page.

## Verification Requirements

At minimum, verify:

- `npm test --prefix src/LightRAGNet.React -- --run`
- `npm run build --prefix src/LightRAGNet.React`
- Browser screenshots at desktop width for:
  - `/documents`
  - `/documents/upload`
  - `/document-preview`
  - one untouched page, preferably `/graph-view`

Manual visual checks:

- Sidebar grouping matches the prototype.
- Brand mark is not a placeholder dot.
- SignalR footer shows state and version.
- Table actions use standard icons and accessible labels.
- Cards, dialogs, and overlays have visible elevation.
- Untouched pages remain functionally recognizable.

## Open Decisions

No blocking design decision remains before writing the implementation plan. The accepted prototype is the source of truth for the shell style. Any later page-level redesign, especially RAG Chat, Knowledge Graph, System Status, and Cache Management, should get its own focused visual pass before production changes.

