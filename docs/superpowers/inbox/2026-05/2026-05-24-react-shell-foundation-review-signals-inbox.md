# React Shell Foundation Review Signals

- Date: `2026-05-24`
- Topic slug: `react-shell-foundation-review-signals`
- Status: `Inbox`
- Lifecycle: `Open`
- Revisit trigger: `When continuing React shell/theme/SignalR route integration or when a similar review finding recurs.`
- Scope: `UI`
- Confidence: `Medium`
- Route candidate: `update-existing`

## Signal

Task 1 of the React full UI migration passed spec review, but code-quality review surfaced foundation-level risks before later routes build on it:

- Dark-theme CTA styles cannot reuse light-era white-on-primary assumptions after mapping primary to bright cyan.
- App-level SignalR subscriber fan-out should isolate per-subscriber exceptions and still log a diagnostic.
- Visual status tabs, URL query state, select controls, and API query parameters need one canonical normalization path, otherwise deep links and visible filters can diverge.
- Drawer/modal surfaces using `aria-modal="true"` need keyboard focus transfer, Escape close, return-focus, and Tab/Shift+Tab containment; initial focus alone is not enough.
- Lazy routes around browser-heavy chunks such as Sigma/WebGL graph workbenches need visible loading, route-local error/retry fallback, and injectable loaders for tests; `fallback={null}` turns slow or failed chunks into blank pages.
- Standalone feature CSS migrated from isolated islands must scope global selectors such as `*` to the feature root before entering the shared React shell.
- Destructive operation confirmations must bind to the data snapshot that produced the plan, not only live route controls that can change while refresh is pending.
- Safe document preview links should be recognized from backend-provided `/document-preview/{id}` URLs, not from `openKind` alone, because converted/original artifact references can still use the same preview route.
- Preview API clients should branch on `content-type` before reading the body so plain-text preview responses and non-JSON error bodies are not consumed or masked by JSON parsing fallback.
- Subagents working in an isolated worktree should verify path and branch before edits; file editing tools should use absolute paths when there is any risk of defaulting to the parent session cwd.

## Why It Might Matter

These are small foundation mistakes that can propagate across migrated pages. If left implicit, future agents may copy unreadable CTA styles, reintroduce silent SignalR event failures, ship tabs whose URL semantics do not match API state, declare modal dialogs that still let keyboard focus escape, leave lazy route chunks as blank pages, leak isolated island CSS into the app shell, execute destructive actions against the wrong live controls, route safe preview links through the wrong frontend base path, lose preview error messages by double-reading response bodies, or write to the wrong checkout during subagent-driven work.

## What Is Missing

- Repeated occurrence across later migrated routes.
- Evidence that the worktree editing risk is specific to one tool path rather than a one-off subagent workflow issue.
- Whether later migrated pages reuse the same status/filter URL pattern or need a shared helper.
- Whether later drawers/modals need a shared focus-management utility rather than local handlers.
- Whether later browser-heavy routes should share a route shell helper for lazy loading, error fallback, and retry behavior.
- Whether later migrated islands contain global CSS selectors that need feature-root scoping.
- Whether later destructive operations need a shared snapshot/confirmation pattern.
- Whether path-base aware preview URL normalization needs a shared helper instead of feature-local parsing.
- Whether mixed JSON/text API clients need a shared response reader for content negotiation and error preservation.
- A completed requirement archive for the full React UI migration that can absorb these as implementation lessons.

## Likely Next Route

If the same patterns recur during Tasks 2-10, update this inbox note or promote the stable parts into an existing problem/archive. If the final React full UI migration archive captures these guardrails fully, mark this note `Promoted` or `Partially promoted`.

## Related Assets

- Spec: [React full UI migration design](../../specs/2026-05-24-react-full-ui-migration-design.md)
- Plan: [React full UI migration implementation plan](../../plans/2026-05-24-react-full-ui-migration-implementation-plan.md)
- Archive: `None yet.`
- Problems:
  - `None yet.`
