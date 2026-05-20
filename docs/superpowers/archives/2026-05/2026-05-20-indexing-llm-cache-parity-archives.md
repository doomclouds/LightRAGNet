# Indexing LLM Cache Parity

- Date: `2026-05-20`
- Topic slug: `indexing-llm-cache-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `indexing-cache`, `llm-cache`, `summary-cache`, `jsonl`

## Summary

本归档记录索引阶段 LLM cache 对齐 Python LightRAG 的实现：旧的 `chunk.Id -> ChunkResult` 缓存被替换为 `default:extract:{hash}` 和 `default:summary:{hash}` 扁平 cache 合同，extract key 会写入 `text_chunks[].llm_cache_list` 供删除清理使用，summary cache 则保持非 chunk-owned，不随单个文档删除。

## Delivered Scope

- 为 LLM cache 增加 `extract` / `summary` cache type、默认 `default:*:{sha256}` key builder、`chunk_id` 元数据和索引阶段开关。
- 将 `DocumentProcessingService` 改为基于实体抽取 prompt 的 raw LLM response cache，embedding 独立生成，不再存入 `llm_cache`。
- 在 text chunk 写入中稳定输出去重后的 `llm_cache_list`，让文档删除能清理 Python-style extract cache key。
- 为图谱实体/关系描述合并增加 JSONL summary prompt、summary cache hit/miss、`<think>` 清理和 `chunk_id=null` 保存语义。
- 补齐删除语义、旧 chunk-id cache 忽略、cache hit/miss、并发限流、JSONL 与 summary cache 的回归测试。

## Out of Scope

- 不实现旧 `ChunkResult` 缓存兼容或迁移工具；早期项目阶段直接切到新合同。
- 不实现 embedding cache、cache 管理 UI/API、共享 pipeline status 语义或真实存储端到端清库验证。
- 不把 summary cache key 挂到 `text_chunks[].llm_cache_list`，避免文档删除误删非 chunk-owned summary cache。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessing|FullyQualifiedName~DescriptionMerger|FullyQualifiedName~SummaryPromptBuilder|FullyQualifiedName~DocumentDeletionServiceTests|FullyQualifiedName~LightRAGLifecycleIntegrationTests" --verbosity minimal`：通过 `103` 个测试。
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`：`LightRAGNet.Tests` 通过 `302` 个，`LightRAGNet.Server.Tests` 通过 `32` 个，`LightRAGNet.Web.Tests` 通过 `20` 个。
- `dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal`：成功，`0` warning，`0` error。
- Subagent review 覆盖 Task 4/5/6/7，修复了 extract miss 并发上限、legacy cache 反向护栏、summary `<think>` 清理和删除语义测试缺口。

## Source Documents

- Spec: [2026-05-20-indexing-llm-cache-parity-design.md](../../specs/2026-05-20-indexing-llm-cache-parity-design.md)
- Visual: None found for this topic.
- Plan: [2026-05-20-indexing-llm-cache-parity-implementation-plan.md](../../plans/2026-05-20-indexing-llm-cache-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- 后续如果补真实存储验收，应继续遵守测试环境隔离，避免再触碰本机真实 Qdrant/Neo4j 数据。
