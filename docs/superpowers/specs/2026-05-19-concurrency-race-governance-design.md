# Concurrency Race Governance Design

- Date: `2026-05-19`
- Topic slug: `concurrency-race-governance`
- Status: `Draft`
- Scope: `LightRAGNet`

## Problem

最近连续出现两类运行时竞态：

- Blazor 文档列表页收到多个任务状态通知后，手写 debounce 的共享 `CancellationTokenSource` 在 `await` 后被其它回调替换或释放。
- 任务状态文件 `tasks.json` 在 Windows 上被短暂占用时，`File.Move(..., overwrite: true)` 直接失败，导致状态持久化报错。

这说明项目缺的不是某个 `?.` 或某个局部 `lock`，而是一组可复用的并发边界抽象。继续在每个页面、Store、后台服务里手写取消、事件分发和文件替换，会让同类问题反复出现。

## Goals

- 建立一组小而稳定的并发基建，覆盖当前最容易出问题的模式。
- 把高风险链路从 fire-and-forget、共享 CTS、直接文件写入迁移到可测试 helper。
- 保留现有业务行为，不在本轮改变 RAG pipeline、查询语义或 UI 交互。
- 每个治理点都要有并发回归测试，能复现旧问题或锁住新边界。
- 明确单实例短锁修复和多实例一致性之间的边界，避免把局部修复误说成分布式协调。

## Non-Goals

- 不实现跨多 Server 实例的强一致任务状态合并。
- 不把 JSON KV 存储一次性替换成数据库。
- 不重写整个任务队列或 SignalR 通知协议。
- 不为所有服务统一引入重量级 actor 框架。
- 不改变用户可见的查询、上传、删除、任务状态文案。

## Risk Map

### High Risk: Task Progress Pipeline

`RagTaskProcessorService` 在 `TaskStateChanged` 事件里用 fire-and-forget 调用 `UpdateTaskProgressAsync`。这会让进度保存和状态完成/失败交错，异常也不会自然回到处理链。

Target:

- 用串行事件队列承接任务进度事件。
- 每个 taskId 保持顺序处理。
- 任务完成、失败或取消后丢弃迟到进度。
- 事件处理异常必须记录 taskId、stage、progress，并且不能破坏后台 worker 主循环。

### High Risk: JSON File Persistence

`JsonKVStore` 目前读取未加锁，返回内部字典引用，保存时直接 `File.WriteAllText`。当多个服务方法共享同一个 singleton Store 时，读写交错和外部短锁都可能导致状态不一致或文件写入失败。

Target:

- 所有读写都通过同一个异步锁或快照机制。
- 对外返回 copy，不泄露内部 dictionary。
- 所有 JSON 文件写入都走统一 atomic writer。
- 持久化失败要可观察，不能只 log 后继续让调用方以为成功。

### Medium Risk: SignalR/UI Event Dispatch

`RagTaskNotificationService` 收到 SignalR 事件后 fire-and-forget 分发给页面。事件到达快于 UI 处理时，页面共享状态容易出现重入。

Target:

- 用 UI notification dispatcher 统一处理事件分发。
- 至少支持串行分发或按事件类型 coalesce。
- 页面 handler 不需要自己承受并发 flood。
- 页面 dispose 后 pending handler 应安全退出。

### Medium Risk: Page-Level Operation Cancellation

`RagChat` 和其它页面仍手写 `CancellationTokenSource` 生命周期。当前没有明确复现，但模式和刚修过的 debounce 问题相似。

Target:

- 用 `AsyncOperationSlot` 管理当前操作的 cancel/replace/dispose。
- 在 dispose 后拒绝新操作或安全 no-op。
- 捕获 token 后不再访问共享 CTS 字段。

### Medium Risk: LightRAG Singleton Event Pump

`LightRAG` 是 singleton，内部有全局 `BufferBlock<TaskState>` 和 `TaskStateChanged` 事件。多个任务同时处理时依赖订阅者按 `DocId` 过滤，模型可用但脆。

Target:

- 为每次 Insert/Delete 建立 operation-scoped progress sink。
- 逐步替代全局 event pump 或至少给 event pump 加启动锁、完成边界和订阅生命周期测试。
- 避免两个并发操作共享同一事件通道后靠字符串过滤兜底。

## Proposed Building Blocks

### AtomicJsonFileWriter

职责：

- 接收目标 path 和内容生成器。
- 写唯一临时文件。
- 用有界重试替换目标文件。
- 清理临时文件。
- 对 `IOException` / `UnauthorizedAccessException` 做短锁重试。

适用位置：

- `RagTaskStateStore`
- `JsonKVStore`
- 未来其它 JSON 文件状态。

不负责：

- 多实例状态合并。
- 目录 ACL 修复。
- JSON schema 迁移。

### AsyncKeyedEventQueue

职责：

- 接收带 key 的异步事件。
- 同一 key 内顺序处理。
- 不同 key 可并行处理。
- 支持最后值 coalesce，用于高频进度更新。
- handler 异常集中记录。

适用位置：

- 任务进度更新：key = taskId。
- UI 任务通知：key = taskId 或 event type。

不负责：

- 业务状态机判断。
- 持久化格式。

### AsyncOperationSlot

职责：

- 管理一个“当前操作”的 CTS。
- 支持 replace：新操作取消旧操作。
- 支持 dispose：取消当前操作并拒绝后续操作。
- 在锁内捕获 token，后续只用本地 token。

适用位置：

- `AsyncDebouncer` 内部。
- `RagChat` 查询取消。
- 其它页面级长操作。

### PerKeyAsyncLock

职责：

- 为 documentId、taskId、workspace 等 key 提供细粒度串行化。
- 防止同一资源的业务状态变更交错。
- 自动释放空闲 key lock，避免字典无限增长。

适用位置：

- 同一文档上传/删除互斥。
- 同一 taskId 状态更新。
- 同一 workspace query revision bump。

## Migration Plan

### Phase 1: File Persistence Foundation

- 提取 `AtomicJsonFileWriter`。
- 让 `RagTaskStateStore` 使用该 helper，保留现有测试。
- 为 `JsonKVStore` 增加读写锁、快照返回和 atomic save。
- 补 JSON KV 并发读写与短锁测试。

### Phase 2: Task Progress Serialization

- 增加 `AsyncKeyedEventQueue`。
- 在 `RagTaskProcessorService` 中把 fire-and-forget 进度更新改为 keyed queue。
- 对 completed/failed/cancelled task 增加迟到进度丢弃测试。
- 验证高频 `MergingRelations` 进度不会重叠保存，也不会把完成状态改回处理中。

### Phase 3: UI Notification Serialization

- 在 `RagTaskNotificationService` 中引入 dispatcher。
- 明确任务状态事件和数据清空事件的顺序策略。
- 保留页面级 debounce，但让页面 handler 不再并发进入同一个关键刷新路径。
- 补 SignalR 多事件快速到达时页面 handler 顺序/合并测试。

### Phase 4: Operation Lifetime Cleanup

- 提取或扩展 `AsyncOperationSlot`。
- 让 `AsyncDebouncer` 基于该 slot 或保持同等语义。
- 迁移 `RagChat` 查询 CTS 管理。
- 补 dispose 与 streaming callback 交错测试。

### Phase 5: LightRAG Progress Scope Review

- 评估是否能把 `TaskStateChanged` 改成 operation-scoped progress sink。
- 如果不能立即替换，至少给 singleton event pump 补并发启动、订阅/退订、跨 doc 过滤测试。

## Testing Strategy

- Red tests must reproduce the failure class before implementation.
- File tests must include target file locked, temp file cleanup, retry exhausted, and cancellation.
- Event queue tests must include per-key ordering, cross-key parallelism, coalescing, handler exception logging, and dispose.
- UI tests may be markup/unit tests first；如果后续引入 bUnit，再补组件级交互测试。
- Regression tests should avoid fixed sleeps except when intentionally testing retry windows; prefer gates, TCS, locks, or deterministic fakes.

## Acceptance Criteria

- `RagTaskStateStore` and `JsonKVStore` no longer directly implement ad hoc JSON file replacement.
- Task progress updates no longer use unmanaged fire-and-forget in `RagTaskProcessorService`.
- SignalR task/data notifications have an explicit dispatch policy.
- Page-level CTS ownership follows `AsyncOperationSlot`/`AsyncDebouncer` semantics.
- New helper APIs have focused tests independent of Blazor rendering.
- Existing behavior tests continue to pass.
- Problem assets for the two observed race classes remain linked from the implementation plan.

## Open Questions

- Should multi-instance same `WorkingDir` be explicitly unsupported, or should we add a named mutex/file lock later?
- Should `JsonKVStore` persist failures throw to callers, or should some call sites tolerate best-effort cache persistence?
- Should task progress updates be fully serialized, or should high-frequency progress be coalesced to reduce disk writes?
- Should `LightRAG` stay singleton after progress sink cleanup, or should insert/delete operations get scoped orchestration objects?
