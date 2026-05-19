# Query LLM Cache Parity

- Date: `2026-05-19`
- Topic slug: `query-llm-cache-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `query-cache`, `keywords-cache`, `workspace-revision`, `tdd`

## Summary

本轮交付为 LightRAGNet 查询阶段补齐一层有边界的 Python LightRAG 风格 LLM cache：KG keyword extraction 可以复用缓存，KG/Naive/Bypass non-streaming query answer 可以复用缓存，同时用 workspace query revision 防止文档插入、删除或 clear-all 后继续命中旧 RAG 答案。

## Delivered Scope

- Added deterministic flattened cache keys and typed cache entry conversion for `keywords`, `query`, and `metadata` records.
- Added `LightRagLlmCacheService` over the existing keyed `llm_cache` KV store, with independent `EnableLlmCache`, `EnableQueryCache`, and `EnableKeywordCache` switches.
- Cached KG keyword extraction results while supplied keywords, Naive, and Bypass skip keyword cache.
- Cached KG, Naive, and Bypass non-streaming query answers, while streaming, prompt-only, context-only, and conversation-history queries skip answer cache.
- Bumped workspace query revision after successful insert, successful indexed delete, and clear-all, including workspace normalization for clear-all.

## Out of Scope

- Embedding cache, entity extraction cache, entity/relation summary cache, cache migration tools, cache management UI/API, and prefix-scan cleanup remain outside this slice.
- Python pipeline shared-status semantics and streaming response cache were intentionally not implemented.

## Verification Snapshot

- `dotnet test .\LightRAGNet.slnx`: passed with `LightRAGNet.Tests` 184/184 and `LightRAGNet.Server.Tests` 25/25.
- `dotnet build .\LightRAGNet.slnx`: passed with `0` warnings and `0` errors.
- Task-level reviews approved cache key/service, keyword cache, query answer cache, and workspace revision invalidation after targeted regressions.
- Scope scan confirmed cache usage stayed within query-time LLM cache paths and did not add embedding/entity/summary cache implementation.

## Source Documents

- Spec: [query llm cache parity design](../../specs/2026-05-19-query-llm-cache-parity-design.md)
- Visual: None found for this topic.
- Plan: [query llm cache parity implementation plan](../../plans/2026-05-19-query-llm-cache-parity-implementation-plan.md)

## Related Problems

- None at archive time.

## Notes

- A Task 4 review finding tightened the revision-read path: KG/Naive answer cache now requires a successful strict workspace revision read, so revision metadata read failures degrade to live generation rather than trusting revision `0`.
- A Task 5 review finding aligned clear-all workspace normalization with document lifecycle behavior by trimming configured workspace names before bumping revision.
