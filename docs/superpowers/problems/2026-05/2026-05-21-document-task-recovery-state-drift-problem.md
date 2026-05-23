# Document Task Recovery State Drift

- Date: `2026-05-21`
- Topic slug: `document-task-recovery-state-drift`
- Status: `Captured`
- Scope: `Feature`
- Tags: `task-queue`, `document-intake`, `recovery`, `terminal-state`, `tdd`

## Symptom

文档 intake pipeline 的恢复路径看起来通过了主流程测试，但 code review 发现多个状态漂移风险：host shutdown 取消正在处理的任务时可能把任务写回 `Pending`，和启动恢复“Processing -> Failed，需要显式 retry”的语义冲突；terminal snapshot 可能被 document-id active lookup 当成仍活跃的任务，导致重启后文档状态查询或取消/重试判断拿到过期终态；completed conversion handoff 在 `converted.md` artifact 丢失时也可能绕过已有 active `IndexDocument` task 对账，直接把文档标成 RAG failed。ManagedCode conversion 后续审查又发现 conversion-only cancel 与 worker `ExecuteUpdate` claim 的竞态：API 已返回 accepted cancel 后，worker 仍可能用旧 tracked entity 完成转换并 enqueue RAG。用户实测又暴露一个同族问题：PDF/DOCX 转换成功后本应自动进入 RAG 切片，但状态短暂进入 queued 后变成 `Cancelled`，需要用户手动 retry 才继续索引。

## Trigger / Context

- RAG task queue 同时维护内存任务表、持久化状态文件、terminal snapshot fallback 和 document-id 查询。
- `RagTaskProcessorService` 在 host shutdown、startup recovery、进度 drain、终态发布之间有多条异常/取消路径。
- Document intake pipeline 依赖 `GetTaskByDocumentIdAsync` / `GetTasksByDocumentIdsAsync` 判断当前文档是否存在 active task。
- Terminal snapshot 仍需要保留给 retry-by-task-id，但不能污染“按 document id 查当前活跃任务”的语义。
- Document conversion worker 可能在保存 converted artifact、入队 RAG task、回写 `ActiveRagTaskId` 之间中断；恢复 handoff 时 artifact 和 queue 状态可能不一致。
- Conversion-only 文档取消时没有 active RAG task，API cancel、conversion worker claim、converter 执行和 RAG handoff 之间存在多个可交错窗口。
- `IRagTaskQueueService.EnqueueTaskAsync` 会同步发布 `Pending` 状态事件，`RagTaskStatusChangedHandler` 可能先于 conversion handoff 的后续落库写入 `ActiveRagTaskId`。

## Root Cause

恢复语义没有在所有入口保持同一个 source of truth。startup recovery 已经把中断在 `Processing` 的任务标记为 `Failed`，但 host shutdown 的 `OperationCanceledException` 分支仍沿用旧的取消/重排思路，可能把正在处理的任务重新暴露为 `Pending`。另一边，terminal snapshot 是为了保存删除失败后的终态证据，但 document-id lookup 没过滤 terminal status，于是“历史终态快照”和“当前活跃任务”被同一查询混在一起。conversion handoff 的 artifact read failure 分支也先判定本地 artifact，不先查 queue active task，导致“已成功入队但文档行没来得及保存 task id”的恢复窗口被误标为失败。conversion-only cancel 直接修改 tracked entity，而 worker claim 使用条件 `ExecuteUpdate`; cancel 如果夹在 claim 和 handoff 之间，worker 后续仍可用 stale entity 把 `Cancelled` 覆盖回 `Queued/Indexing` 并写入 `ActiveRagTaskId`。转换成功后的 RAG handoff 又假定 `EnqueueTaskAsync` 返回前数据库行仍保持 `RagStatus=Processing` 且 `ActiveRagTaskId=null`，但真实队列会先发布 `Pending` 事件并由 handler 写入同一个 task id；后续条件更新匹配不到行，processor 误判为 pipeline 已取消，于是调用 `CancelTaskAsync` 取消刚接受的索引任务。

## Fix

- 在 host shutdown 打断 processing 时明确写入 `Failed`，错误信息说明任务被 shutdown/restart 中断，需要用户显式 retry。
- 保留 startup recovery 的 `Processing -> Failed` 规则，让中断恢复和运行中关闭语义一致。
- 让 `GetTaskByDocumentIdAsync` 和 `GetTasksByDocumentIdsAsync` 过滤 `Completed` / `Failed` / `Cancelled` terminal snapshots，只返回 `Pending` / `Processing` active task。
- 保留 `GetTaskAsync(taskId)` 对 terminal snapshot 的读取能力，确保 failed/cancelled task 仍可按 task id retry。
- 增加回归测试覆盖 host shutdown interrupted processing 不再发布 `Pending`，以及 terminal snapshot + pending task coexist 时 document-id lookup 只返回 active task。
- 在 completed conversion handoff 的 converted artifact 读取失败分支里先调用 active task 对账；存在 active `IndexDocument` task 时回写 `ActiveRagTaskId`、状态、stage 和 progress，不 reconvert、不重新 enqueue，也不标记 RAG failed。
- conversion-only cancel 改为条件 `ExecuteUpdateAsync`：仅在 `ActiveRagTaskId == null`、RAG 状态仍可取消、conversion 状态为 `Queued/Processing` 时原子写入 `Cancelled`，并把 conversion 状态重置为 `Queued` 避免 `Cancelled + Processing` 半状态。
- `DocumentConversionProcessor` 在 claim 后、转换异常后、转换成功后、RAG handoff 前后重新读取 DB 状态；发现 `RagStatus=Cancelled` 时停止，handoff 落库也改为条件 `ExecuteUpdateAsync`，只允许 `Processing + Completed conversion + no active task` 进入 `Indexing`，否则取消刚入队的 index task。
- 补上真实 SQLite + fake converter/queue 的交错测试：converter 被 worker claim 后阻塞，API cancel 返回 accepted，释放 converter 后断言不 enqueue RAG 且文档保持 `Cancelled`。
- RAG handoff 落库条件放宽为允许 `ActiveRagTaskId == null` 或已经等于刚入队的 task id，并排除 `RagStatus=Cancelled`；这样 status handler 先写入同一个 active task 时不会被误判为取消。
- 补上 `ProcessNextBatchAsync_WhenQueuePublishesPendingBeforeHandoffPersists_DoesNotCancelAcceptedIndexTask`，模拟队列接受任务后先写入 pending 状态，断言 conversion processor 不会取消该 task。

## Why This Fix

这比自动重排中断任务更保守：索引 pipeline 可能已经写入部分 chunk、graph 或 doc status，自动 requeue 会放大重复索引风险。把中断统一标记为 `Failed`，并要求显式 retry，能让 UI/API 给出可解释状态，也让用户或上层流程决定是否重新处理。active lookup 过滤 terminal snapshot 则保留了恢复证据，同时避免把历史终态误当成当前任务。

## Recognition Clues

- 测试或日志里出现 shutdown/restart 中断后任务又回到 `Pending`。
- 文档状态显示仍有 active task，但对应 task status 已经是 `Completed`、`Failed` 或 `Cancelled`。
- `GetTaskAsync(taskId)` 能查到 terminal snapshot，`GetTaskByDocumentIdAsync(documentId)` 也返回同一个终态任务。
- retry/cancel、track status 或 Web 状态筛选在重启后表现得像有幽灵任务，尤其是 state file 删除失败后出现 terminal snapshot fallback。
- PDF/DOCX 已完成 conversion 且 `RagStatus=Processing`、`ActiveRagTaskId=null`，但 queue 已有 active `IndexDocument` task；若 `converted.md` 丢失或路径不可读，恢复 worker 不应优先标 Failed。
- API cancel 对 conversion-only 文档返回 `202 Accepted`，但之后 `DocumentConversionProcessorTests` 或日志还能看到 `EnqueueTaskAsync` 被调用、`ActiveRagTaskId` 被写回、状态从 `Cancelled` 回到 `Queued/Indexing`。
- PDF/DOCX 转换完成后能看到 `converted.md`，但文档随后显示 `Cancelled`，任务日志出现刚创建的 index task 立即被 `CancelTaskAsync` 取消。
- 代码里 conversion handoff 的条件更新要求 `RagStatus == Processing && ActiveRagTaskId == null`，但队列入队方法会在返回前发布 `Pending` 状态事件。

## Applicability / Non-Applicability

### Applies When

- 系统同时保存 active task、terminal snapshot 和 document-id lookup。
- 后台 worker 的 shutdown/restart 恢复策略要求用户显式 retry。
- 文档、队列或 UI 逻辑需要区分“历史终态证据”和“当前活跃任务”。

### Does Not Apply When

- 任务处理是完全幂等且业务明确要求中断自动 requeue。
- 查询入口本来就是按 task id 查看历史任务详情，而不是按 document id 查 active task。
- Terminal snapshot 只用于审计展示，不参与任何重试、取消、状态聚合或 active task 判断。

## Related Artifacts

- Spec: [document intake pipeline parity design](../../specs/2026-05-21-document-intake-pipeline-parity-design.md)
- Plan: [document intake pipeline parity implementation plan](../../plans/2026-05-21-document-intake-pipeline-parity-implementation-plan.md)
- Archive: [document intake pipeline parity archive](../../archives/2026-05/2026-05-21-document-intake-pipeline-parity-archives.md)
- Related Problems:
  - [task state file replace lock](./2026-05-19-task-state-file-replace-lock-problem.md)
  - [document deletion review gaps](./2026-05-18-document-deletion-review-gaps-problem.md)
- Code or Test:
  - [RagTaskProcessorService.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs)
  - [RagTaskQueueService.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskQueueService.cs)
  - [DocumentIntakeService.cs](../../../../src/LightRAGNet.Server/Services/DocumentIntakeService.cs)
  - [DocumentConversionProcessor.cs](../../../../src/LightRAGNet.Server/Services/DocumentConversion/DocumentConversionProcessor.cs)
  - [RagTaskProcessorServiceTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs)
  - [RagTaskQueueServiceTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs)
  - [DocumentConversionProcessorTests.cs](../../../../tests/LightRAGNet.Server.Tests/DocumentConversionProcessorTests.cs)
