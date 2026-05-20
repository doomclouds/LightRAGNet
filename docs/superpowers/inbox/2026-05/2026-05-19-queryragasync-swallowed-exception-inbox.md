# QueryRagAsync Swallowed Exception

- Date: `2026-05-19`
- Topic slug: `queryragasync-swallowed-exception`
- Status: `Inbox`
- Lifecycle: `Open`
- Revisit trigger: `When touching RagChat error handling, ApiClient streaming/SSE behavior, or query failure user feedback.`
- Scope: `UI`
- Confidence: `Medium`
- Route candidate: `new-problem`

## Signal

`ApiClient.QueryRagAsync` catches broad `Exception` and ignores it. During concurrency governance review this made some `RagChat` error snackbar paths unreachable for real network, HTTP, deserialization, or SSE stream failures.

## Why It Might Matter

Users may see a query silently stop without an error message, while the page-level `RagChat` exception handling appears to exist and may look covered in code review. This can hide actual backend, network, or streaming parser failures.

## What Is Missing

- A focused reproduction that confirms the visible UI behavior for HTTP failure, connection loss, malformed SSE, and server-side `ErrorEvent`.
- A decision on whether `QueryRagAsync` should throw, return a typed result, or expose a separate error callback for streaming failures.
- Regression tests around `RagChat` user feedback after query stream failures.

## Likely Next Route

If the signal recurs or `RagChat` query failure handling is changed, promote this into a formal problem asset unless it is fully handled by an existing API-client error-contract archive. The likely fix should make `QueryRagAsync` failure semantics explicit instead of swallowing all exceptions.

## Related Assets

- Spec: [concurrency race governance design](../../specs/2026-05-19-concurrency-race-governance-design.md)
- Plan: [concurrency race governance implementation plan](../../plans/2026-05-19-concurrency-race-governance-implementation-plan.md)
- Archive: [concurrency race governance archive](../../archives/2026-05/2026-05-19-concurrency-race-governance-archives.md)
- Problems:
  - [markdown documents debounce race](../../problems/2026-05/2026-05-19-markdown-documents-debounce-race-problem.md)
