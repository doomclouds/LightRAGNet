# Retrieval Context Vector Chunk Parity

- Date: `2026-05-20`
- Topic slug: `retrieval-context-vector-chunk-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `retrieval-context`, `vector-chunk-selection`, `kg-query`, `tdd`

## Summary

本轮交付把 `RetrievalContextService` 中 KG related chunks 的默认 `VECTOR` 配置从“记录 warning 后退回 WEIGHT”推进为真实的 chunk vector cosine similarity 选择。Entity 与 relation related chunks 现在复用同一套向量相似度 helper，并在 chunk vectors 缺失、非法、数量不齐或 query embedding 不可用时确定性降级到既有 `WEIGHT` weighted polling，从而让 .NET 行为对齐 Python LightRAG 的相关 chunk 选择边界。

## Delivered Scope

- Entity related chunks 在 `KgChunkPickMethod=VECTOR` 时按 query embedding 与 chunk vectors 的 cosine similarity 取前 `int(RelatedChunkNumber * entityCount / 2.0)` 个 chunk。
- Relation related chunks 使用同样的 vector selection，并在选择前排除已经来自 entity context 的 chunk ids。
- `PickByVectorSimilarityAsync` 严格要求候选 chunk vectors 全部可读取且维度/数值可计算，否则返回空结果交给调用方 fallback 到 `WEIGHT`。
- `queryEmbedding == null`、空 query 或 vector helper 失败都会显式切回 `WEIGHT`，避免默认 `VECTOR` 下 related chunks 静默为空。
- `KgChunkPickMethod=WEIGHT` 保持原 weighted polling 行为，并通过测试锁定不会读取 `GetByIdsAsync("chunks", ...)`。
- `InMemoryVectorStore` 测试替身记录 batch `GetByIdsAsync` 调用，用于验证 vector chunk read 边界与顺序。

## Out of Scope

- 未触碰 indexing LLM cache、extract cache、summary cache、query cache、删除清理或 `llm_cache_list`。
- 未修改 `LightRAG.cs` insert/delete/lifecycle、`DocumentProcessingService`、`KnowledgeGraphMerge`、Server/API、Blazor UI 或 Chat diagnostics。
- 未调整 context text format、token budget algorithm、prompt 文本或 Python prompt perfect parity。
- 未加入真实 Qdrant/Neo4j integration tests；本轮验证基于 in-memory test doubles。

## Verification Snapshot

- RED：新增 `BuildQueryContextAsync_WhenQueryEmbeddingFails_FallsBackToWeightedPolling` 与 `BuildQueryContextAsync_WhenRelationQueryEmbeddingFails_FallsBackToWeightedPolling` 后，两个测试均复现 related chunks 为空。
- GREEN：补上 `VECTOR` 不可用时的 entity/relation 显式 `WEIGHT` fallback 后，上述 2 个回归测试通过。
- Parity 组：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RetrievalContextVectorChunkParityTests --verbosity minimal` 通过：`8/8`。
- 定向组：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~RetrievalContext|FullyQualifiedName~InMemoryVectorStoreTests" --verbosity minimal` 通过：`26/26`。
- Core 回归：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --verbosity minimal` 顺序重跑通过：`279/279`。
- Solution 回归：`dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` 通过：`LightRAGNet.Tests 279/279`、`LightRAGNet.Server.Tests 32/32`、`LightRAGNet.Web.Tests 20/20`。
- Build：`dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal` 成功，`0` warning / `0` error。
- 范围审计：`git diff --name-only main...HEAD` 只包含 `RetrievalContextService.cs`、`RetrievalContextVectorChunkParityTests.cs`、`InMemoryVectorStore.cs`、`InMemoryVectorStoreTests.cs`；未发现 QueryCache、DocumentProcessing、KnowledgeGraphMerge、LightRAG.cs、Server/Web、TaskQueue、indexing/delete/cache 文件变更。

## Source Documents

- Spec: [retrieval context vector chunk parity design](../../specs/2026-05-20-retrieval-context-vector-chunk-parity-design.md)
- Visual: None found for this topic.
- Plan: [retrieval context vector chunk parity implementation plan](../../plans/2026-05-20-retrieval-context-vector-chunk-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- 终审发现默认 `VECTOR` 在 query embedding 预生成失败时不会进入 `WEIGHT` 的边界漏洞；已在 `35ce24b` 修复并补回 entity/relation 两条回归测试。
- 一次并行验证同时运行同一测试项目和 solution，触发 `tasks.json` 清理文件锁；随后顺序重跑 core 与 solution 均通过，该现象未归因于本次代码变更。
