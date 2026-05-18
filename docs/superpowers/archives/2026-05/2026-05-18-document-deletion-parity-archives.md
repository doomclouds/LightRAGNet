# Document Deletion Parity

- Date: `2026-05-18`
- Topic slug: `document-deletion-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `document-deletion`, `tdd`, `storage-consistency`, `blazor`

## Summary

本轮交付把 Python LightRAG 的 indexed document deletion 能力补齐到 LightRAGNet：已入库文档现在可以通过后台任务删除 RAG 存储、图谱引用、KV tracking、向量记录和可选 LLM cache；Server/API/UI 按 `204`、`202`、`409`、`DeletionFailed` retry 和 SignalR 状态同步形成完整删除体验。

## Delivered Scope

- 新增核心删除服务，基于 `doc_status` chunk ids 删除 text chunks、chunk vectors、full docs、graph tracking，并对 retained entity/relation 执行 `source_id` pruning 与向量重建。
- 接入 `LightRAG.DeleteDocumentAsync`、任务队列 delete operation、任务状态处理器和 Markdown row/file 成功后清理路径。
- 扩展 API 与 Blazor UI：indexed delete 返回 `202 Accepted`，行进入 `Deleting`，完成后移除，失败保留 `DeletionFailed` 并允许重试。
- 修正 clear-all 边界：包含 `doc_status`，先停止/取消任务，复用上传文件安全删除规则。
- 为核心删除、Server API、任务队列、UI 状态辅助和可选 Qdrant/Neo4j 集成测试补齐覆盖。

## Out of Scope

- 未让 `clear-all` 在 SQLite、Qdrant、Neo4j 和 KV stores 之间具备事务性。
- 未实现 Python pipeline 的全局 busy/shared status 语义，只保留同文档 active task 互斥。
- 可选真实存储集成测试默认不跑，需要显式设置 `LIGHTRAGNET_RUN_STORAGE_INTEGRATION=1`。
- 未处理既有 `System.Security.Cryptography.Xml 9.0.0` 的 NU1903 依赖警告。

## Verification Snapshot

- `dotnet test .\LightRAGNet.slnx` 通过：`LightRAGNet.Tests 94/94`，`LightRAGNet.Server.Tests 23/23`。
- `dotnet build .\LightRAGNet.slnx` 通过：`0 errors`，保留既有 `4` 个 NU1903 warnings。
- 显式 opt-in 跑过 `DocumentDeletionStorageIntegrationTests`，本机 Qdrant/Neo4j 路径 `2/2` 通过。
- 子代理完成 Task10 规格审查、代码质量复审和最终整体验收复审；最终复审结论为 `APPROVED`。

## Source Documents

- Spec: [document deletion parity design](../../specs/2026-05-18-document-deletion-parity-design.md)
- Visual: None found for this topic.
- Plan: [document deletion parity implementation plan](../../plans/2026-05-18-document-deletion-parity-implementation-plan.md)

## Related Problems

- [document deletion review gaps](../../problems/2026-05/2026-05-18-document-deletion-review-gaps-problem.md)

## Notes

- 本轮最终审查补出了三条重要旁路风险：跨文档 retained relation、clear-all 文件安全、active task 真实取消。相关经验已归档为 problem 资产。
