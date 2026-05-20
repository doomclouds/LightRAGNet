# Retrieval Context Vector Chunk Parity Design

- Date: `2026-05-20`
- Topic slug: `retrieval-context-vector-chunk-parity`
- Status: `Ready for review`
- Scope: `Core retrieval context + KG related chunk selection + tests`
- Tags: `lightrag-alignment`, `retrieval-context`, `vector-chunk-selection`, `kg-query`, `tdd`

## Purpose

LightRAGNet already supports `Local`、`Global`、`Hybrid`、`Mix`、`Naive` 和 `Bypass` query modes，并且 Chat UI 已经能显式调这些查询参数。下一层最影响真实问答质量的是 KG context 里的 related chunks 是否按 Python LightRAG 的策略选择。

当前 .NET `RetrievalContextService` 的 `KgChunkPickMethod` 默认值是 `VECTOR`，但 entity related chunks 和 relation related chunks 遇到 `VECTOR` 时会记录 warning，然后 fallback 到 `WEIGHT`。这意味着配置看起来已经启用了 Python 默认策略，但真实行为仍是 weighted polling。

本阶段只补齐 KG related chunk 的 `VECTOR` 选择策略，让 .NET 在这个边界上严格对齐 Python 行为。它刻意不触碰当前另一个隔离工作区正在执行的 indexing LLM cache parity 任务。

## Python Reference Semantics

Python LightRAG 在 `operate.py` 里通过 `_merge_all_chunks` 汇总三类 chunks：

```text
vector_chunks -> entity_chunks -> relation_chunks
```

对于 entity related chunks，`_find_related_text_unit_from_entities` 的关键语义是：

- 从 `source_id` 按 `GRAPH_FIELD_SEP` 收集 chunk ids。
- 对同一个 chunk id，只保留最早出现的 entity 位置。
- 每个 entity 内部按 chunk occurrence count 降序得到 `sorted_chunks`。
- 当 `kg_chunk_pick_method == "VECTOR"` 且有 `query` 和 `chunks_vdb` 时：
  - `num_of_chunks = int(related_chunk_number * len(entities_with_chunks) / 2)`
  - 调用 `pick_by_vector_similarity(...)`
  - 若结果为空，fallback 到 `WEIGHT`
- `pick_by_vector_similarity` 从所有 `sorted_chunks` 收集唯一 chunk ids。
- 获取 query embedding；若调用方已传入 query embedding，则复用。
- 从 chunks vector store 批量读取候选 chunk vectors。
- 如果没有取到 vectors，或取到数量和候选 chunk id 数量不一致，返回空结果。
- 对每个候选 chunk 计算 cosine similarity。
- 按 similarity 降序取前 `num_of_chunks` 个 chunk id。

对于 relation related chunks，`_find_related_text_unit_from_relations` 的语义类似，但多一个去重边界：

- 先收集 entity chunks 的 chunk ids。
- relation chunks 如果已存在于 entity chunks 中，直接排除。
- 只对剩余 relation chunks 计算 occurrence、排序和 `VECTOR` 选择。

如果 `VECTOR` 选择失败或返回空，Python 回退到 `pick_by_weighted_polling(..., min_related_chunks=1)`。

## Current .NET Gap

当前 .NET 已有这些基础：

- `RetrievalContextService` 已经在 KG query 中预先生成 `queryEmbedding`。
- `FindRelatedTextUnitFromEntitiesAsync` 和 `FindRelatedTextUnitFromRelationsAsync` 已经实现 source-id 收集、occurrence 统计、related chunk 去重、`WEIGHT` weighted polling、batch 读取 text chunks。
- `IVectorStore.GetByIdsAsync("chunks", ids)` 已能读取包含 `Vector` 的 `VectorDocument`。
- `QdrantVectorStore.GetByIdsAsync` 会请求 `withVectors: true`。
- 测试替身 `InMemoryVectorStore` 已能 seed 和批量返回 vectors。

但两个 related chunk 方法在 `KgChunkPickMethod == "VECTOR"` 时仍执行：

```text
VECTOR chunk pick method not fully implemented, falling back to WEIGHT
```

这会让默认配置和真实检索行为不一致。问题不在 UI，也不在 LLM cache，而是在 KG context 的候选 chunk 选择层。

## Product Decision

实现严格 Python 行为对齐的 `VECTOR` related chunk selection：

- Entity related chunks 和 relation related chunks 都使用同一个 vector similarity helper。
- helper 只负责从候选 chunk ids 中按 query embedding 相似度选出 chunk ids。
- 当 `KgChunkPickMethod == "VECTOR"` 且 `query` 非空、`queryEmbedding` 非空时优先走 helper。
- 如果候选为空、`num_of_chunks <= 0`、chunk vectors 缺失、chunk vector 数量不齐、任意异常导致无法可靠排序，返回空并让调用方 fallback 到 `WEIGHT`。
- fallback 行为保持当前 `WEIGHT` 逻辑。
- 保持 Python 的 `int(RelatedChunkNumber * itemCount / 2)` 选择数量。
- 保持 Python 的 relation-vs-entity 去重边界：relation 选择前先排除已经被 entity chunks 使用的 chunk id。

Do not implement quality-tuned behavior in this phase. The priority is parity first, then future quality improvements can be reviewed with a stable baseline.

## Out of Scope

- Indexing LLM cache, extract cache, summary cache, `llm_cache_list`, or cache deletion cleanup.
- `LightRAG.cs` insert/delete/lifecycle behavior.
- `DocumentProcessingService` or `KnowledgeGraphMerge` behavior.
- Query answer cache or keyword cache behavior.
- Context text format parity.
- Token budget algorithm changes.
- Chat UI, API request shape, diagnostics UI, or storage repair flow.
- Real Qdrant/Neo4j integration tests.
- Python prompt text perfect parity.

## Architecture

Keep the change inside `RetrievalContextService` unless implementation pressure proves a small internal helper class would make tests cleaner.

Recommended internal helper shape:

```csharp
private async Task<List<string>> PickByVectorSimilarityAsync(
    string query,
    float[] queryEmbedding,
    IReadOnlyCollection<List<string>> sortedChunkGroups,
    int numOfChunks,
    CancellationToken cancellationToken)
```

Expected helper behavior:

- Flatten all `sortedChunkGroups`.
- Deduplicate chunk ids while preserving a deterministic order suitable for test stability.
- Call `vectorStore.GetByIdsAsync("chunks", uniqueChunkIds, cancellationToken)`.
- Require `documents.Count == uniqueChunkIds.Count`, matching Python's strict all-vectors-present gate.
- Require every returned document to have a non-empty `Vector`.
- Compute cosine similarity between `queryEmbedding` and each chunk vector.
- Sort by similarity descending.
- Return top `numOfChunks` chunk ids.

Implementation may use an internal static cosine helper:

```text
dot = sum(a[i] * b[i])
norm = sqrt(sum(a[i]^2)) * sqrt(sum(b[i]^2))
similarity = dot / norm
```

If dimensions differ, norm is zero, or the vector is empty, the helper should treat the vector selection as failed so the caller can fallback to `WEIGHT`.

## Entity Chunk Contract

`FindRelatedTextUnitFromEntitiesAsync` should keep its existing first steps:

- collect entity source chunk ids
- deduplicate repeated chunks by first occurrence
- compute occurrence count
- build per-entity `sorted_chunks`

Then:

- if `KgChunkPickMethod == "VECTOR"` and `queryEmbedding` exists:
  - compute `numOfChunks = (int)(RelatedChunkNumber * entitiesWithChunks.Count / 2.0)`
  - call vector helper with each entity's sorted chunks
  - if helper returns any ids, use them
  - otherwise fallback to `WEIGHT`
- batch load text chunk data by the selected ids
- preserve current result shape and `chunkTracking` updates

## Relation Chunk Contract

`FindRelatedTextUnitFromRelationsAsync` should keep its existing relation-specific behavior:

- collect relation source chunk ids
- remove chunks already present in `entityChunks`
- count occurrences only for remaining chunks
- remove relations that no longer have chunks
- build per-relation `sorted_chunks`

Then apply the same `VECTOR` helper:

- `numOfChunks = (int)(RelatedChunkNumber * relationsWithChunks.Count / 2.0)`
- selected relation chunks must not include entity chunk ids
- empty or failed vector selection falls back to `WEIGHT`
- preserve current result shape and `chunkTracking` updates

## Testing Strategy

Add focused tests under `tests/LightRAGNet.Tests/RetrievalContext/`.

Core behavior tests:

- `BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity`
- `BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsRelationChunksByCosineSimilarity`
- `BuildQueryContextAsync_WhenRelationVectorChunksOverlapEntityChunks_ExcludesEntityChunks`
- `BuildQueryContextAsync_WhenVectorSelectionReturnsNoVectors_FallsBackToWeightedPolling`
- `BuildQueryContextAsync_WhenChunkVectorMissing_FallsBackToWeightedPolling`
- `BuildQueryContextAsync_WhenKgChunkPickMethodWeight_DoesNotReadChunkVectors`

Test setup should use in-memory stores only:

- seeded entities / relations with controlled `source_id`
- seeded text chunks with known content and file path
- seeded chunk vectors in `InMemoryVectorStore`
- deterministic `queryEmbedding`
- `EnableRerank = false` unless the test explicitly needs rerank isolation

Tests should assert selected chunk order through `QueryContextResult.RawData["data"]["chunks"]` or context output if raw data is insufficient.

If necessary, extend `InMemoryVectorStore` test double to record `GetByIdsAsync` calls. That extension is test infrastructure only and must not affect production code.

## Compatibility

No public API changes are required.

Existing configuration remains valid:

```text
LightRAGOptions.KgChunkPickMethod = "VECTOR"
LightRAGOptions.RelatedChunkNumber = <int>
```

The only behavior change is that the default `VECTOR` setting starts doing what it already claims to do. If vector data is incomplete or unusable, behavior remains compatible through `WEIGHT` fallback.

## Acceptance Criteria

- `KgChunkPickMethod=VECTOR` chooses entity related chunks by cosine similarity instead of weighted polling.
- `KgChunkPickMethod=VECTOR` chooses relation related chunks by cosine similarity instead of weighted polling.
- Relation chunk selection excludes chunks already selected from entity context.
- Missing or incomplete chunk vectors cause deterministic fallback to `WEIGHT`.
- `KgChunkPickMethod=WEIGHT` keeps the existing weighted polling behavior and does not call `GetByIdsAsync("chunks", ...)` on the vector store.
- No cache, indexing, deletion, or UI files are changed by this phase.
- Targeted retrieval-context tests pass.
- Existing query mode, query cache, lifecycle, deletion, and server tests remain green after implementation.

## Implementation Notes

Do not depend on dictionary iteration order when test expectations require deterministic ordering. Python uses a set for candidate chunk ids before vector lookup, but .NET tests should still make the final behavior deterministic by using distinct similarity values and asserting similarity-ranked output.

Use the existing `queryEmbedding` generated in `PerformKGSearchAsync`; do not call `IEmbeddingService.GenerateEmbeddingAsync` again inside the helper. That keeps the .NET implementation close to Python's precomputed embedding path and avoids extra network calls.

Keep fallback local to related chunk selection. A failed vector selection should not fail the whole query; it should degrade to `WEIGHT`, matching Python's operational behavior.
