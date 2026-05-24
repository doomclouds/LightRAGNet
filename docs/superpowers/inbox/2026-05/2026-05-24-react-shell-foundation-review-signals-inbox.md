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
- Subagents working in an isolated worktree should verify path and branch before edits; file editing tools should use absolute paths when there is any risk of defaulting to the parent session cwd.

## Why It Might Matter

These are small foundation mistakes that can propagate across migrated pages. If left implicit, future agents may copy unreadable CTA styles, reintroduce silent SignalR event failures, or write to the wrong checkout during subagent-driven work.

## What Is Missing

- Repeated occurrence across later migrated routes.
- Evidence that the worktree editing risk is specific to one tool path rather than a one-off subagent workflow issue.
- A completed requirement archive for the full React UI migration that can absorb these as implementation lessons.

## Likely Next Route

If the same patterns recur during Tasks 2-10, update this inbox note or promote the stable parts into an existing problem/archive. If the final React full UI migration archive captures these guardrails fully, mark this note `Promoted` or `Partially promoted`.

## Related Assets

- Spec: [React full UI migration design](../../specs/2026-05-24-react-full-ui-migration-design.md)
- Plan: [React full UI migration implementation plan](../../plans/2026-05-24-react-full-ui-migration-implementation-plan.md)
- Archive: `None yet.`
- Problems:
  - `None yet.`
