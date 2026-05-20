# RAG Vector Store State Drift

- Date: `2026-05-20`
- Topic slug: `rag-vector-store-state-drift`
- Status: `Inbox`
- Lifecycle: `Partially promoted`
- Revisit trigger: `When Chat returns no-context while Markdown/RAG status or KV storage says documents are indexed`
- Scope: `Runtime debugging`
- Confidence: `Medium`
- Route candidate: `new-problem`

## Signal

用户上传 `线性修正业务说明.md` 后，Chat 一直返回没有上下文。用控制台查询同一内容相关问题也返回 `[no-context]`。运行时证据显示：

- `rag_storage/full_docs.json`、`text_chunks.json`、`full_entities.json`、`full_relations.json` 中能搜到该文档内容和 `tbReviseRatio`、`973E`、`983E` 等关键词。
- Qdrant `lightrag_vdb_dotnet_chunks_2048d`、`entities_2048d`、`relationships_2048d` 三个 collection 起初都是 `points_count = 0`。
- 重新插入同一文件后，`chunks` collection 变为 `points_count = 3`，`Mix` 查询能命中文档并返回引用。
- 插入过程中 Neo4j 曾报 authentication failure，后续定位到控制台项目从仓库根目录启动时使用 `Directory.GetCurrentDirectory()` 作为配置基准，导致没有读取 `src/LightRAGNet.Example/appsettings.json`，`Neo4JOptions.Password` 落回默认 `"password"`。
- 修复配置基准和 Neo4j password fallback 后，重新清库并插入同一文件，最终写入 `3` 个 chunk vectors、`63` 个 entity vectors、`70` 个 relationship vectors。
- 控制台查询参数已用真实运行验证：`Local`、`Global`、`Hybrid`、`Naive`、`Mix`、`Bypass` 均可执行；`context-only`、`prompt-only`、`stream`、`references`、`top-k`、`chunk-top-k`、`rerank`、高低级关键词参数按预期生效。

## Why It Might Matter

当前 UI 的“已加入 RAG / Completed”状态主要来自 Markdown 元数据和任务状态，查询真正依赖 Qdrant/Neo4j。若 KV 或数据库状态与向量库状态漂移，用户会看到“已经入库”但 Chat 永远 no-context。

这类问题未来可能在清空 Qdrant、Docker volume 重建、Neo4j 密码漂移、部分索引失败或手动清理存储后再次出现。

控制台类诊断工具还要特别注意配置基准目录：从仓库根目录 `dotnet run --project ...` 和从项目输出目录启动，`Directory.GetCurrentDirectory()` 不一定相同。对于部署到输出目录的 `appsettings.json`，应优先用 `AppContext.BaseDirectory`。

## What Is Missing

- 还没有确认 Qdrant 为空的直接触发源：是 Docker volume 被重置、`CleanData` 清理、collection 被外部清空，还是索引过程中曾经失败。
- 还没有自动化健康检查证明每个 `Processed` 文档都对应至少一个 chunk vector。
- 还没有 UI/API 侧提示用户“RAG 状态完成但向量库无命中”的一致性风险。

## Likely Next Route

本轮已补第一层修复：`LightRAG.InsertAsync` 遇到 `Processed` duplicate 时会先校验 `chunks` 向量是否完整，完整才跳过；缺失则重建索引。重建时 `DocumentProcessingService.ProcessChunkAsync` 仍会优先读取 `llm_cache`，因此已有缓存的 chunk 不会重新做实体/关系抽取。

仍建议后续补诊断切片：

- 控制台或 API 增加 `diagnose` 命令，输出 Markdown 状态、KV 文档状态、Qdrant collection 点数、Neo4j 连通性、Naive context-only 查询结果。
- 对 `Completed` 文档增加可选 reindex/repair 流程，避免只因 lifecycle status 为 `Processed` 就跳过向量重建。
- Web Chat 在 no-context 时提示用户检查 `Naive + Context only`、Qdrant points、任务状态和 Neo4j 配置。
- 后续若再增强控制台诊断，保留 `verify-query-options` 这类无外部依赖的参数自检入口，并把需要外部服务的诊断和参数解析诊断分开。

## Related Assets

- [query mode context parity design](../../specs/2026-05-19-query-mode-context-parity-design.md)
- [query mode context parity archive](../../archives/2026-05/2026-05-19-query-mode-context-parity-archives.md)
- [chat query UI adaptation archive](../../archives/2026-05/2026-05-20-chat-query-ui-adaptation-archives.md)
- [LightRAG.cs](../../../../src/LightRAGNet/LightRAG.cs)
- [QueryCommandOptions.cs](../../../../src/LightRAGNet.Example/QueryCommandOptions.cs)
- [Program.cs](../../../../src/LightRAGNet.Example/Program.cs)
- [ServiceCollectionExtensions.cs](../../../../src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs)
- [Neo4jGraphStore.cs](../../../../src/LightRAGNet.Storage/Neo4jGraphStore.cs)
- [LightRAGLifecycleIntegrationTests.cs](../../../../tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs)
- [QueryCommandOptionsTests.cs](../../../../tests/LightRAGNet.Tests/Example/QueryCommandOptionsTests.cs)
- [Neo4JOptionsTests.cs](../../../../tests/LightRAGNet.Tests/Storage/Neo4JOptionsTests.cs)
