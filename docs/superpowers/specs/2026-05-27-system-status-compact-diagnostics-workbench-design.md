# System Status Compact Diagnostics Workbench Design

- Date: `2026-05-27`
- Topic slug: `system-status-compact-diagnostics-workbench`
- Status: `Ready for review`
- Scope: `LightRAGNet.React /system-status page visual and component refactor`
- Tags: `react`, `system-status`, `diagnostics`, `dashboard`, `anthropic-light`, `compact-ui`

## Background

The React design system already has the right global direction: `anthropic-light`, warm surfaces, restrained borders, Lucide icons, dense workbench layouts, and shared primitives such as `PageHeader`, `Panel`, `Button`, `StatusPill`, `DataTableSurface`, `Banner`, and `DiagnosticTable`.

The current `/system-status` page has useful data but weak information architecture. It renders overall health, fix-first items, checks, evidence, and feature impact as similarly weighted cards. That makes the page feel like a stack of panels rather than an operational diagnostics dashboard.

The approved visual reference for this slice is:

- `docs/superpowers/visuals/anthropic-light-workbench/04-system-cache-table-pages.png`

The target feeling is compact, refined, table-first, and operational. It should look like the right-side React application page inside the existing shell, not like a standalone big-screen dashboard.

## Goal

Redesign `/system-status` as a compact diagnostics workbench:

1. Preserve every user-visible behavior and the existing `/api/system/health` contract.
2. Make the page visually align with the refined workbench style in `04-system-cache-table-pages.png`.
3. Replace the heavy card-stack layout with a dense dashboard structure: summary tiles, evidence table, remediation priorities, feature impact, and raw JSON.
4. Use shared design-system primitives where they fit, and keep only page-specific layout and diagnostic composition in local components.
5. Introduce small page-local components that can later be promoted to shared primitives only if another page proves the same need.

## Non-Goals

- Do not change `/api/system/health` response shape or backend health-check behavior.
- Do not add fake metrics, fake checks, or new backend data just for visual polish.
- Do not redesign the app shell, sidebar, topbar, routing, or navigation.
- Do not migrate Cache Management, RAG Chat, Knowledge Graph, or Document Preview in this slice.
- Do not introduce a charting library.
- Do not use a large health ring as the main visual. The accepted direction is summary tile plus table-first diagnostics.

## Visual Direction

The page should be compact and precise:

- Thin borders, low or no shadow on inner panels.
- Small Lucide line icons, generally `16px` to `18px`.
- Summary tiles around `88px` high, not oversized metric cards.
- Tables and side panels carry the main diagnostic information.
- Raw JSON is visible as a secondary diagnostics panel, not the main reading path.
- Accent color is used for active tab, primary refresh action, and selected/priority cues only.
- Semantic colors remain restricted to status, priority, and health signals.

Avoid:

- Thick icon outlines or character-based icon placeholders.
- Large circular gauges dominating the page.
- Oversized cards with heavy shadow.
- Marketing-style hero composition.
- Card nesting that makes the page feel bulky.

## Information Architecture

The page content should be organized inside the existing React app shell as the right-side content only.

```text
PageHeader
StatusSummaryTiles
Main diagnostics grid:
  EvidenceTable
  Side stack:
    RemediationPriorities
    FeatureImpact
  RawJsonPanel
```

### Page Header

Use shared `PageHeader`.

Content:

- Title: `System Status`
- Description: `Real-time diagnostics and system operation overview`
- Actions:
  - `Export Report` is allowed only if backed by existing behavior. If no export behavior exists, do not add it.
  - `Copy JSON` keeps the current copy behavior.
  - `Refresh Now` keeps the current reload behavior and uses a Lucide refresh icon.

If only copy and refresh exist, the page should not invent export.

### Status Summary Tiles

Replace the current heavy summary panel with compact tiles.

Suggested tiles from existing data:

- `Overall Health`: `health.status`
- `Healthy`: `health.summary.healthy`
- `Degraded`: `health.summary.degraded`
- `Unhealthy`: `health.summary.unhealthy`
- `Not measured`: `health.summary.notMeasured`
- `Last checked`: derived from `health.generatedAt`
- `Duration`: `health.durationMs`

Implementation can choose the exact tile count, but must show all summary counts and preserve generated time plus duration somewhere near the top.

Use `lucide-react` icons such as:

- `CheckCircle2`
- `Activity`
- `AlertTriangle` or `TriangleAlert`
- `Clock3`
- `Server`

Rules:

- No emoji or character icon placeholders.
- Tile icon containers must be light and thin, not heavy visual badges.
- Counts must not rely on color alone.

### Evidence Table

The checks list should become the main content surface.

Rows come from `health.checks`.

Columns:

- Component: `check.name`
- Category: `check.category`
- Status: `check.status` rendered with `StatusPill`
- Evidence: compact summary derived from `check.evidence`
- Last checked or duration: use existing `check.durationMs`; if no per-check checked time exists, label it as duration instead of inventing time
- Action or expand affordance: optional, for evidence details

Evidence details:

- Expanded evidence should use `DiagnosticTable` when practical.
- Long values must wrap.
- Monospace is allowed only for diagnostic values.

Rules:

- The evidence table should be the primary scan path.
- Keep table header and row density close to the reference image.
- Preserve all existing check information: id, name, category, status, message, remediation, affects, duration, evidence.

### Remediation Priorities

`health.fixFirst` should be a compact side panel, not a full-width card stack.

Each item should show:

- Rank or priority marker.
- Title.
- Status.
- Remediation.
- Affected features.
- Optional action affordance if it maps to an existing page link or expansion.

Rules:

- Remediation text should be visible without requiring expansion.
- Destructive or external actions must not be invented.
- Empty state should be calm: `No action required.`

### Feature Impact

`health.featureImpacts` should be a compact side panel below or near remediation.

Each item should show:

- Feature name.
- Status pill.
- Reason.
- Affected-by list.
- Existing links when provided by API.

Rules:

- This section answers "what user-facing workflow is affected?"
- It should not compete visually with the evidence table.
- Empty state should be explicit.

### Raw JSON Panel

Keep raw data available for diagnostics.

Content:

- JSON preview of the current `health` object.
- Copy action using current clipboard behavior.
- Download action only if implemented for real; otherwise omit it.

Rules:

- Raw JSON sits in a secondary panel, preferably right column on desktop.
- It should not replace structured evidence.
- It must be readable and scrollable.
- Do not persist or expose secrets. The current system health payload should not include secrets.

## Component Strategy

Use existing shared components first:

- `PageHeader`
- `Button`
- `IconButton` when only an icon is shown
- `Panel`
- `StatusPill`
- `DataTableSurface`
- `DiagnosticTable`
- `Banner` or `ErrorState`

Add page-local components:

- `SystemStatusSummaryTiles`
- `SystemStatusEvidenceTable`
- `SystemStatusRemediationPanel`
- `SystemStatusFeatureImpactPanel`
- `SystemStatusRawJsonPanel`

Do not promote these to shared components in this slice. Promotion can happen later if Cache Management, RAG Chat, or another diagnostics page proves the same structure is reusable.

## Token And Style Guidance

The current `theme.css` colors are sufficient. This slice should not create a new palette.

Local CSS may define System Status layout classes for:

- dashboard grid
- summary tile grid
- side stack
- raw JSON panel sizing
- evidence table layout
- compact row and column behavior

Local CSS should not define:

- a replacement body or page font stack
- new primary colors
- hard-coded status colors when existing semantic tokens work
- heavy panel shadows inconsistent with the reference

If a token gap appears during implementation, prefer adding a narrow reusable token or class only when it benefits multiple pages. Otherwise keep it page-local and documented.

Recommended compact style values:

- panel radius: existing `var(--radius-panel)`
- control radius: existing `var(--radius-control)`
- table cell font: `12px` to `13px`
- tile label font: `11px` to `12px`
- tile value font: `20px` to `22px`
- tile height: about `88px`
- icon size: `16px` to `18px`, summary tile container may use `40px` to `42px`

## States

The redesign must cover:

- Initial loading with no `health`.
- Refresh pending with existing `health` visible.
- API error.
- Healthy.
- Degraded.
- Unhealthy.
- NotMeasured.
- Empty `fixFirst`.
- Empty `featureImpacts`.
- Checks with empty `affects`.
- Evidence with long values.

## Accessibility

- Use real text labels; icons are decorative unless they are the only visible control content.
- Icon-only controls need accessible labels.
- Status must include text, not just color.
- Tables must use semantic table markup.
- Expandable evidence must be keyboard reachable.
- Focus states must remain visible.

## Responsive Behavior

Desktop:

- Summary tiles span the page top.
- Evidence table is the largest region.
- Remediation and feature impact sit in a compact side stack.
- Raw JSON can sit in the right column if width allows.

Tablet:

- Summary tiles wrap.
- Evidence table remains first.
- Side panels stack below or beside based on available width.

Mobile:

- One-column layout.
- Actions wrap to full width when needed.
- Table may use horizontal scroll inside `DataTableSurface`.
- Raw JSON remains scrollable and secondary.

## Testing And Verification

Required commands:

```powershell
npm test --prefix src/LightRAGNet.React -- --run
npm run build --prefix src/LightRAGNet.React
```

Expected test updates:

- Component/unit coverage for summary tile rendering, status mapping, evidence expansion, empty remediation, and raw JSON copy availability.
- Guardrail tests should still pass, including page CSS font, hex, and local UI debt registration.

Visual QA:

- Desktop around `1440px`.
- Tablet around `768px`.
- Mobile around `375px`.

Screenshots should cover:

- Normal healthy/degraded data.
- Error state.
- Loading or refresh pending state.
- Expanded evidence with long values.

## Acceptance Criteria

- `/system-status` visually matches the compact refined workbench direction from `04-system-cache-table-pages.png`.
- Current behavior is preserved: loading, refresh, copy JSON, error handling, and all health data sections.
- The page uses `lucide-react` icons, not character icon placeholders.
- The heavy card-stack layout is replaced by summary tiles plus table-first diagnostics.
- No API, shell, route, or backend behavior changes.
- New page-local components are small, named, and testable.
- React tests and build pass.
- Browser visual checks at desktop/tablet/mobile show no incoherent overlap or horizontal page scroll except table/raw JSON internal scroll.
