# Concurrency Race Governance

- Date: `2026-05-19`
- Topic slug: `concurrency-race-governance`
- Status: `Archived`
- Scope: `Reliability`
- Tags: `concurrency`, `race-condition`, `file-persistence`, `task-progress`, `blazor`, `signalr`, `cancellation`

## Summary

本轮交付把最近暴露出的竞态问题从局部补丁提升为可复用并发边界：统一 JSON 文件原子写入，串行化任务进度和前端通知，集中页面级操作取消生命周期，提供按 key 串行化基础设施，并给 LightRAG 状态泵加启动保护和 subscriber 隔离。

## Delivered Scope

- Added `AtomicFileWriter` under `LightRAGNet.Core` with unique temp files, bounded retry, cancellation cleanup, and replace failure tests.
- Migrated `RagTaskStateStore` and `JsonKVStore` persistence to `AtomicFileWriter`.
- Added locked snapshot reads and persistence failure rollback behavior to `JsonKVStore`.
- Added `AsyncEventDispatcher<T>` with serialized dispatch, keyed coalescing, drain semantics, cancellation-aware disposal, and handler exception isolation.
- Migrated `RagTaskProcessorService` progress updates away from unmanaged fire-and-forget calls.
- Hardened `RagTaskQueueService` terminal/progress publication ordering, cloned event snapshots, stale progress barriers, publish gates, and terminal tombstones.
- Serialized frontend task/data notifications in `RagTaskNotificationService`, including SignalR lifecycle callback disposal boundaries and per-subscriber exception isolation.
- Added `AsyncOperationSlot` and migrated `AsyncDebouncer` / `RagChat` cancellation ownership to local operation leases.
- Added `PerKeyAsyncLock<TKey>` for keyed task/document/workspace serialization.
- Guarded `LightRAG` state processor startup and isolated `TaskStateChanged` subscriber failures.

## Out of Scope

- `ApiClient.QueryRagAsync` still catches and swallows broad exceptions, so some `RagChat` error snackbar paths are not reachable for real network/SSE failures. This was identified during Task 6 review and deliberately deferred because it is outside the operation-slot migration.
- No owner-level disposal contract was added to `PerKeyAsyncLock<TKey>`; current entries are cleaned up through lease release and waiter cancellation.
- `LightRAG` still has no explicit shutdown path for the background state processor. The task addressed startup duplication and subscriber isolation only.

## Verification Snapshot

- `dotnet test .\LightRAGNet.slnx`: passed, `LightRAGNet.Tests 246/246`, `LightRAGNet.Server.Tests 25/25`, total `271/271`.
- `dotnet build .\LightRAGNet.slnx`: succeeded with `0` warning / `0` error.
- Race pattern scan:
  `rg -n "_ = taskQueue\.UpdateTaskProgressAsync|_ = NotifyTaskStatusHandlersAsync|_ = NotifyDataClearedHandlersAsync|File\.WriteAllText\(|File\.Move\(.*overwrite: true|CancellationTokenSource\?" src/LightRAGNet src/LightRAGNet.Storage src/LightRAGNet.Web`
  returned no matches.
- Completion gate before archive creation:
  `check_completion_gate.py . --completed-topic "concurrency race governance" --json`
  reported `missing_requirement_archive`, which this archive resolves.

## Task-Level Evidence

- `AtomicFileWriterTests`: unique temp cleanup, retry success, retry exhaustion, and cancellation cleanup passed.
- `JsonKVStoreConcurrencyTests`: snapshot reads, concurrent mutation behavior, clone coverage, and persistence failure handling passed.
- `AsyncEventDispatcherTests`: ordering, keyed coalescing, drain, handler exception isolation, and disposal behavior passed.
- Task queue tests plus `AsyncDebouncerTests`: progress ordering, no stale progress after terminal status, queue publish gates, terminal tombstones, and debounce latest-only behavior passed.
- `RagTaskNotificationServiceSourceTests`: dispatcher enqueue, accepted-status snapshot drain, handler isolation, SignalR lifecycle disposal cancellation, reconnected join token, and dispose/init guard ordering passed.
- `AsyncOperationSlotTests` plus `AsyncDebouncerTests`: operation replacement, lease-owned CTS lifetime, dispose cancellation, idempotent completion, and latest-only debounce behavior passed.
- `PerKeyAsyncLockTests`: same-key serialization, different-key parallelism, cancellation release, lease idempotency, and key cleanup passed.
- `LightRAGStateProcessorTests`: concurrent insert state publishing remains serial, and throwing subscribers do not block later subscribers.

## Source Documents

- Spec: [concurrency race governance design](../../specs/2026-05-19-concurrency-race-governance-design.md)
- Plan: [concurrency race governance implementation plan](../../plans/2026-05-19-concurrency-race-governance-implementation-plan.md)
- Problem: [markdown documents debounce race](../../problems/2026-05/2026-05-19-markdown-documents-debounce-race-problem.md)
- Problem: [task state file replace lock](../../problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md)

## Related Problems

- [markdown documents debounce race](../../problems/2026-05/2026-05-19-markdown-documents-debounce-race-problem.md)
- [task state file replace lock](../../problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md)

## Related Commits

- `997d8cf feat: add atomic file writer`
- `9ea8fc8 fix: centralize json file persistence`
- `9756325 feat: add async event dispatcher`
- `15c21da fix: serialize rag task progress updates`
- `0408d12 fix: serialize frontend task notifications`
- `c611b02 fix: centralize page operation cancellation`
- `97884b4 feat: add per-key async lock`
- `0f79e1f fix: guard lightrag state processor startup`
- `5bc4d9a fix: clean task queue publication gates`

## Notes

- Task 5 needed multiple review passes because SignalR lifecycle callbacks can leak service-disposal cancellation if the callback boundary awaits a helper that throws `OperationCanceledException`.
- Task 6 review caught a subtle CTS lifetime issue: operation replacement should cancel an old lease but must not dispose its CTS while old code may still register the token. The final slot contract makes CTS disposal lease-owned.
- Task 8 review strengthened the regression test from eventual completed delivery to direct callback concurrency detection, so it now asserts the single-processor invariant rather than only successful completion.
- Final review caught a resource lifecycle issue in task queue publication gates: per-task terminal tombstones and publish gates now clean up after terminal publication, exception paths, and any already-started stale progress operations drain. If terminal state deletion fails, the queue persists a terminal snapshot before tombstone cleanup so later progress cannot reload stale `Processing` state.
