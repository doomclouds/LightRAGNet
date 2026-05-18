# Document Deletion Review Gaps

- Date: `2026-05-18`
- Topic slug: `document-deletion-review-gaps`
- Status: `Captured`
- Scope: `Repo`
- Tags: `document-deletion`, `clear-all`, `graph-retention`, `security`, `task-cancellation`

## Symptom

`document-deletion-parity` 的全量测试和初次最终审查前置验证都通过后，最终代码审查仍发现三类高风险缺口：跨文档 retained relation 可能被 Neo4j `DETACH DELETE` 连带删除，`clear-all` 仍保留手拼 `FileUrl` 的路径穿越风险，后台任务只被标记 `Failed` 但没有真实取消。

## Trigger / Context

这个问题出现在把 Python LightRAG 的单文档删除语义搬到 .NET 后。单文档 API、任务流、UI 和多数存储清理都已补齐，但同一删除能力还存在 bulk clear-all 路径、跨文档图关系、后台处理器并发这几条旁路。

## Root Cause

实现和测试过早围绕“当前文档自己的索引”收敛：实体保护只看当前 `full_relations`，没有继续从 graph edges / `relation_chunks` 识别其它文档仍保留的关系。文件删除安全也只修了单文档 delete 路径，`clear-all` 仍有第二套字符串拼接逻辑。任务停止只更新队列状态，没有给正在执行的 processor 一个可取消 token。

## Fix

- `DocumentDeletionService` 在删除实体前额外扫描该实体的 graph edges，并用 `relation_chunks` 或 edge `source_id` 保护外部 retained relation。
- `clear-all` 调整为先 `StopAllTasksAsync`，再删除 Markdown rows 和存储。
- `clear-all` 删除行对应文件时复用 `MarkdownDocumentDeletionService.CreateTrustedUploadReference` 与 `DeleteUploadedFileIfPresent`。
- 新增 `RagTaskCancellationRegistry`，让 processor 为每个 processing task 注册 linked token；`StopAllTasksAsync` 标记失败时同时取消 active token。
- 补上跨文档 retained relation、clear-all traversal、stop-before-delete、processing token cancellation 回归测试。

## Why This Fix

这些修复把删除语义从“主路径可用”提升到“所有删除入口共享同一安全和一致性边界”。只在 `clear-all` 里再加一次字符串过滤会继续制造双规则；只把任务状态改成 Failed 也挡不住正在执行的后台写入。共享安全服务、外部关系扫描和真实 cancellation token 是更稳定的边界。

## Recognition Clues

- 单文档 delete 安全测试通过，但 bulk clear-all 里还能看到 `FileUrl.Replace("/uploads/", "")` 或 `Path.Combine(uploadsFolder, fileName)`。
- 删除实体前只读取当前文档的 `full_relations`，没有调用 `GetNodeEdgesAsync` 或检查外部 `relation_chunks`。
- `StopAllTasksAsync` 只更新 `RagTaskStatus.Failed`，没有任何 token registry、linked token 或 cancel 调用。
- 代码评审发现“测试都绿，但旁路没有复用主路径安全/一致性合同”。

## Applicability / Non-Applicability

### Applies When

- 一个能力有单条主路径和 bulk/admin/后台处理等旁路。
- 删除图节点使用会级联删除关系的 `DETACH DELETE`。
- API 状态机和后台 processor 通过共享队列协作，但停止操作需要中断正在执行的工作。
- 文件删除安全已经在一个入口修复，另一个入口仍自己解析路径或 URL。

### Does Not Apply When

- 被删除实体没有任何跨文档或跨 chunk 的图关系。
- bulk 操作只清理内存状态，不触碰文件系统、图数据库或向量库。
- 后台任务本身不接收 `CancellationToken`，需要先做更大的可取消性改造。

## Related Artifacts

- Spec: [document deletion parity design](../../specs/2026-05-18-document-deletion-parity-design.md)
- Plan: [document deletion parity implementation plan](../../plans/2026-05-18-document-deletion-parity-implementation-plan.md)
- Archive: [document deletion parity archive](../../archives/2026-05/2026-05-18-document-deletion-parity-archives.md)
- Related Problems:
  - [testability refactor completion gaps](./2026-05-18-testability-refactor-completion-gaps-problem.md)
- Code or Test:
  - [DocumentDeletionService.cs](../../../../src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionService.cs)
  - [MarkdownDocumentsController.cs](../../../../src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs)
  - [RagTaskCancellationRegistry.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskCancellationRegistry.cs)
  - [DocumentDeletionServiceTests.cs](../../../../tests/LightRAGNet.Tests/DocumentDeletion/DocumentDeletionServiceTests.cs)
  - [MarkdownDocumentsControllerTests.cs](../../../../tests/LightRAGNet.Server.Tests/MarkdownDocumentsControllerTests.cs)
