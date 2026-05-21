# Document Task Recovery State Drift

- Date: `2026-05-21`
- Topic slug: `document-task-recovery-state-drift`
- Status: `Captured`
- Scope: `Feature`
- Tags: `task-queue`, `document-intake`, `recovery`, `terminal-state`, `tdd`

## Symptom

文档 intake pipeline 的恢复路径看起来通过了主流程测试，但 code review 发现两个状态漂移风险：host shutdown 取消正在处理的任务时可能把任务写回 `Pending`，和启动恢复“Processing -> Failed，需要显式 retry”的语义冲突；同时 terminal snapshot 可能被 document-id active lookup 当成仍活跃的任务，导致重启后文档状态查询或取消/重试判断拿到过期终态。

## Trigger / Context

- RAG task queue 同时维护内存任务表、持久化状态文件、terminal snapshot fallback 和 document-id 查询。
- `RagTaskProcessorService` 在 host shutdown、startup recovery、进度 drain、终态发布之间有多条异常/取消路径。
- Document intake pipeline 依赖 `GetTaskByDocumentIdAsync` / `GetTasksByDocumentIdsAsync` 判断当前文档是否存在 active task。
- Terminal snapshot 仍需要保留给 retry-by-task-id，但不能污染“按 document id 查当前活跃任务”的语义。

## Root Cause

恢复语义没有在所有入口保持同一个 source of truth。startup recovery 已经把中断在 `Processing` 的任务标记为 `Failed`，但 host shutdown 的 `OperationCanceledException` 分支仍沿用旧的取消/重排思路，可能把正在处理的任务重新暴露为 `Pending`。另一边，terminal snapshot 是为了保存删除失败后的终态证据，但 document-id lookup 没过滤 terminal status，于是“历史终态快照”和“当前活跃任务”被同一查询混在一起。

## Fix

- 在 host shutdown 打断 processing 时明确写入 `Failed`，错误信息说明任务被 shutdown/restart 中断，需要用户显式 retry。
- 保留 startup recovery 的 `Processing -> Failed` 规则，让中断恢复和运行中关闭语义一致。
- 让 `GetTaskByDocumentIdAsync` 和 `GetTasksByDocumentIdsAsync` 过滤 `Completed` / `Failed` / `Cancelled` terminal snapshots，只返回 `Pending` / `Processing` active task。
- 保留 `GetTaskAsync(taskId)` 对 terminal snapshot 的读取能力，确保 failed/cancelled task 仍可按 task id retry。
- 增加回归测试覆盖 host shutdown interrupted processing 不再发布 `Pending`，以及 terminal snapshot + pending task coexist 时 document-id lookup 只返回 active task。

## Why This Fix

这比自动重排中断任务更保守：索引 pipeline 可能已经写入部分 chunk、graph 或 doc status，自动 requeue 会放大重复索引风险。把中断统一标记为 `Failed`，并要求显式 retry，能让 UI/API 给出可解释状态，也让用户或上层流程决定是否重新处理。active lookup 过滤 terminal snapshot 则保留了恢复证据，同时避免把历史终态误当成当前任务。

## Recognition Clues

- 测试或日志里出现 shutdown/restart 中断后任务又回到 `Pending`。
- 文档状态显示仍有 active task，但对应 task status 已经是 `Completed`、`Failed` 或 `Cancelled`。
- `GetTaskAsync(taskId)` 能查到 terminal snapshot，`GetTaskByDocumentIdAsync(documentId)` 也返回同一个终态任务。
- retry/cancel、track status 或 Web 状态筛选在重启后表现得像有幽灵任务，尤其是 state file 删除失败后出现 terminal snapshot fallback。

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
  - [RagTaskProcessorServiceTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs)
  - [RagTaskQueueServiceTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs)
