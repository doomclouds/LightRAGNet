# Document Deletion Review Gaps

- Date: `2026-05-18`
- Topic slug: `document-deletion-review-gaps`
- Status: `Captured`
- Scope: `Repo`
- Tags: `document-deletion`, `clear-all`, `graph-retention`, `security`, `task-cancellation`

## Symptom

`document-deletion-parity` 的全量测试和初次最终审查前置验证都通过后，最终代码审查仍发现三类高风险缺口：跨文档 retained relation 可能被 Neo4j `DETACH DELETE` 连带删除，`clear-all` 仍保留手拼 `FileUrl` 的路径穿越风险，后台任务只被标记 `Failed` 但没有真实取消。后续运行还暴露出第四类旁路：RAG 侧 `doc_status` 已不存在时，后台删除任务把 `Document not found` 当失败，或者把任务当完成但跳过真实 RAG 清理，导致 `full_docs`、chunk KV 和 Neo4j 节点关系残留。ManagedCode document intake 后续审查又暴露同类 server cleanup 旁路：artifact 目录清理异常会阻断 local-only delete 或 delete-task completed handler 的 legacy uploads cleanup 与 DB row 删除，让文档卡在 `Deleting`；`clear-all` 与 active conversion worker 不协调时，还可能在 DB rows 清空后由 worker 重新写出 `converted.md` 孤儿 artifact。

## Trigger / Context

这个问题出现在把 Python LightRAG 的单文档删除语义搬到 .NET 后。单文档 API、任务流、UI 和多数存储清理都已补齐，但同一删除能力还存在 bulk clear-all 路径、跨文档图关系、后台处理器并发、以及“生命周期状态缺失但其它 RAG 元数据仍存在”的恢复清理旁路。

## Root Cause

实现和测试过早围绕“当前文档自己的索引”收敛：实体保护只看当前 `full_relations`，没有继续从 graph edges / `relation_chunks` 识别其它文档仍保留的关系。文件删除安全也只修了单文档 delete 路径，`clear-all` 仍有第二套字符串拼接逻辑。任务停止只更新队列状态，没有给正在执行的 processor 一个可取消 token。删除入口还把 `doc_status` 当成唯一真相；当 `doc_status` 为空但 `full_docs`、`text_chunks`、`full_entities`、`full_relations` 仍存在时，删除计划直接返回 not found，无法清理残留图谱和 KV 数据。后续 artifact cleanup 接入时，异常边界又默认沿用“清理失败即抛出”的强一致语义，没有区分可重试的 artifact 垃圾清理与必须继续完成的本地 row / legacy upload 删除链路。conversion worker 进入系统后，`clear-all` 只停 RAG task，不知道 conversion worker 仍可先写文件后保存 DB，于是 bulk cleanup 和后台转换缺少共享互斥边界。

## Fix

- `DocumentDeletionService` 在删除实体前额外扫描该实体的 graph edges，并用 `relation_chunks` 或 edge `source_id` 保护外部 retained relation。
- `clear-all` 调整为先 `StopAllTasksAsync`，再删除 Markdown rows 和存储。
- `clear-all` 删除行对应文件时复用 `MarkdownDocumentDeletionService.CreateTrustedUploadReference` 与 `DeleteUploadedFileIfPresent`。
- 新增 `RagTaskCancellationRegistry`，让 processor 为每个 processing task 注册 linked token；`StopAllTasksAsync` 标记失败时同时取消 active token。
- 补上跨文档 retained relation、clear-all traversal、stop-before-delete、processing token cancellation 回归测试。
- `RagTaskProcessorService.ProcessDeleteTaskAsync` 对 `DocumentDeletionResult.Found == false` 的删除任务按完成处理，并记录 warning；完成事件继续交给 Server handler 删除 Markdown row 和上传文件。
- 补上 `RagTaskProcessorServiceTests.ProcessDeleteTaskAsync_MissingRagDocument_CompletesTask`，覆盖 RAG 侧目标已缺失时的队列幂等删除语义。
- `LightRAG.DeleteDocumentAsync` 在 `doc_status` 缺失时回退读取 `full_docs[docId].chunks_list`，只要 `full_docs` 仍存在就继续走 `DocumentDeletionService` 清理 chunk vectors、text chunks、full document metadata、entity/relation metadata 和图谱引用。
- 补上 `LightRAGLifecycleIntegrationTests.DeleteDocumentAsync_MissingLifecycleStatusButFullDocExists_DeletesStorage`，覆盖 `doc_status` 丢失但 RAG 元数据仍存在的恢复删除场景。
- `MarkdownDocumentDeletionService` 增加 best-effort artifact cleanup 包装，local-only delete、delete-task completed handler 和 clear-all 复用同一 warning-and-continue 语义；legacy uploads cleanup 与 DB row 删除不再被 artifact cleanup 异常阻断。
- 补上 artifact cleanup 抛异常时 direct delete 仍返回 `204 NoContent` 并移除 row、delete-task completed 仍删除 legacy upload 并移除 row 的回归测试。
- 增加 `DocumentConversionCoordinator`，让 `DocumentConversionProcessor` 与 `clear-all` 共享同一个 conversion gate；`clear-all` 会等待 active conversion 批次完成，再删除 artifacts 和 rows，避免清库后 worker 重新写出 `converted.md`。
- 补上 `ClearAllData_WhenConversionIsRunning_WaitsBeforeRemovingRowsAndArtifacts`，覆盖 conversion worker 被阻塞时 clear-all 不会提前完成，释放 worker 后 artifact 目录和 DB row 都被清理。

## Why This Fix

这些修复把删除语义从“主路径可用”提升到“所有删除入口共享同一安全和一致性边界”。只在 `clear-all` 里再加一次字符串过滤会继续制造双规则；只把任务状态改成 Failed 也挡不住正在执行的后台写入；只把 RAG 侧 not found 当完成，又会在 `doc_status` 丢失但 `full_docs` 仍存在时制造静默残留。共享安全服务、外部关系扫描、真实 cancellation token、full_docs fallback，以及删除任务幂等完成策略，是更稳定的边界。

## Recognition Clues

- 单文档 delete 安全测试通过，但 bulk clear-all 里还能看到 `FileUrl.Replace("/uploads/", "")` 或 `Path.Combine(uploadsFolder, fileName)`。
- 删除实体前只读取当前文档的 `full_relations`，没有调用 `GetNodeEdgesAsync` 或检查外部 `relation_chunks`。
- `StopAllTasksAsync` 只更新 `RagTaskStatus.Failed`，没有任何 token registry、linked token 或 cancel 调用。
- 日志出现 `RagTaskProcessorService` 处理 `DeleteDocument` 任务时报 `Document not found.`，但 Server 数据库里对应 Markdown row 仍处于 `Deleting` 或 `DeletionFailed`。
- `LightRAG.DeleteDocumentAsync` 已有 unknown document 返回 `Found=false` 的测试，但后台 processor 没有单独覆盖这个结果的队列语义。
- SQLite `MarkdownDocuments` 已为空、Qdrant 点数为 0，但 `rag_storage/full_docs.json`、`text_chunks.json`、`entity_chunks.json` 或 Neo4j 仍保留同一文档的 `file_path/source_id`。
- `doc_status.json` 为 `{}`，但其它 RAG JSON 文件仍含同一个 `doc--...`。
- 代码评审发现“测试都绿，但旁路没有复用主路径安全/一致性合同”。
- Artifact cleanup 只是删除 `documents/{id}` 目录，却在 local-only delete 或 `RagTaskStatusChangedHandler` 的 completed delete path 里位于 legacy upload cleanup / row remove 之前且没有局部 `try/catch`。
- `clear-all` 只调用 `StopAllTasksAsync`，但没有任何 conversion worker / conversion processor 协调点；review 或测试能构造出 converter 正在运行时 clear-all 先删 row，随后 worker 写回 `converted.md` 的 orphan artifact。

## Applicability / Non-Applicability

### Applies When

- 一个能力有单条主路径和 bulk/admin/后台处理等旁路。
- 删除图节点使用会级联删除关系的 `DETACH DELETE`。
- API 状态机和后台 processor 通过共享队列协作，但停止操作需要中断正在执行的工作。
- 文件删除安全已经在一个入口修复，另一个入口仍自己解析路径或 URL。
- 删除请求的目标资源可能已经被手工清库、重复任务、历史数据不一致或上一次部分成功的删除移除。
- 生命周期状态文件可能丢失、被清空或未随旧版本迁移，但 `full_docs` 仍能提供 chunk id 列表。

### Does Not Apply When

- 被删除实体没有任何跨文档或跨 chunk 的图关系。
- bulk 操作只清理内存状态，不触碰文件系统、图数据库或向量库。
- 后台任务本身不接收 `CancellationToken`，需要先做更大的可取消性改造。
- 非删除操作遇到 missing document；index/query 等非幂等操作仍应暴露错误而不是伪装完成。
- `full_docs` 也不存在的删除请求；这时没有可恢复元数据，按幂等完成本地清理即可。

## Related Artifacts

- Spec: [document deletion parity design](../../specs/2026-05-18-document-deletion-parity-design.md)
- Plan: [document deletion parity implementation plan](../../plans/2026-05-18-document-deletion-parity-implementation-plan.md)
- Archive: [document deletion parity archive](../../archives/2026-05/2026-05-18-document-deletion-parity-archives.md)
- Related Problems:
  - [testability refactor completion gaps](./2026-05-18-testability-refactor-completion-gaps-problem.md)
- Code or Test:
  - [DocumentDeletionService.cs](../../../../src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionService.cs)
  - [MarkdownDocumentsController.cs](../../../../src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs)
  - [MarkdownDocumentDeletionService.cs](../../../../src/LightRAGNet.Server/Services/MarkdownDocumentDeletionService.cs)
  - [RagTaskStatusChangedHandler.cs](../../../../src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs)
  - [RagTaskCancellationRegistry.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskCancellationRegistry.cs)
  - [RagTaskProcessorService.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs)
  - [LightRAG.cs](../../../../src/LightRAGNet/LightRAG.cs)
  - [DocumentDeletionServiceTests.cs](../../../../tests/LightRAGNet.Tests/DocumentDeletion/DocumentDeletionServiceTests.cs)
  - [LightRAGLifecycleIntegrationTests.cs](../../../../tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs)
  - [RagTaskProcessorServiceTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs)
  - [MarkdownDocumentsControllerTests.cs](../../../../tests/LightRAGNet.Server.Tests/MarkdownDocumentsControllerTests.cs)
  - [DocumentDeletionApiTests.cs](../../../../tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs)
  - [DocumentConversionCoordinator.cs](../../../../src/LightRAGNet.Server/Services/DocumentConversion/DocumentConversionCoordinator.cs)
