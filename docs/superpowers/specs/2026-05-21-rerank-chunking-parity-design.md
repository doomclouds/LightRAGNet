# Rerank Chunking Parity Design

- Date: `2026-05-21`
- Topic slug: `rerank-chunking-parity`
- Status: `Ready for review`
- Scope: `Core rerank flow + query chunk ordering + tests`
- Tags: `lightrag-alignment`, `rerank`, `query-quality`, `chunking`, `tdd`

## Purpose

LightRAGNet 已经把 query mode、query cache、indexing cache、KG related chunk vector selection 和 KG context builder 这些 Python 对齐底座补上了。下一层真正会影响答案质量的，不是继续改 UI 或 cache，而是 rerank 对“长 chunk”的处理方式。

当前 .NET 在 `NaiveQueryService` 和 `RetrievalContextService.RetrieveChunksAsync` 中都会把每个原始 `ChunkData.Content` 直接传给 `IRerankService.RerankAsync`。短 chunk 下这能工作；但当 chunk 很长时，rerank provider 可能只看前半段、拒绝超长输入，或者因为 API-level `topN` 先截断子结果而导致最终返回的原始 chunk 数量不足。Python LightRAG 已经为这个问题提供了明确语义：长文档先切成 rerank 子片段，子片段分数再聚合回原始文档。

本阶段的目标是让 .NET rerank 的评价对象回到“原始 chunk”，而不是误把“rerank 子片段”当成最终 topN 对象。这个切片直接服务于 `Naive` 和 KG `Mix` 的检索质量，也给后续接不同 rerank provider 留出稳定边界。

## Python Reference Semantics

Python LightRAG 的 rerank chunking 逻辑集中在 `lightrag/rerank.py`：

- `chunk_documents_for_rerank(documents, max_tokens=480, overlap_tokens=32)`
  - 短文档保持一对一。
  - 长文档按 token 切成多个 overlapping chunks。
  - 返回 `chunked_documents` 和 `doc_indices`，其中 `doc_indices[chunkIndex]` 指回原始 document index。
  - 如果 tokenizer 初始化失败，fallback 到字符近似：`1 token ~= 4 chars`。
  - 当 `overlap_tokens >= max_tokens` 时 clamp overlap，避免切片循环不前进。
- `aggregate_chunk_scores(chunk_results, doc_indices, num_original_docs, aggregation="max")`
  - 将 rerank provider 返回的子片段 score 聚合回原始文档。
  - 默认使用 `max`，也支持 `mean` / `first`。
  - 忽略无效 chunk index。
  - 只返回至少有 score 的原始文档，并按 score 降序排列。
- `generic_rerank_api(..., enable_chunking=True, top_n=N)`
  - chunking 开启时，API request 不传 `top_n`。
  - 这样 provider 会返回所有子片段 score，避免 `top_n` 只截到少数子片段。
  - 聚合回原始文档后，再应用原始 `top_n`。

本轮不要求照搬 Python 的所有 provider wrapper，但必须对齐这三个核心行为：切片、映射聚合、document-level topN。

## Current .NET Gap

当前 .NET 已有这些基础：

- `IRerankService.RerankAsync(query, documents, topN, cancellationToken)` 返回 `{ index, relevance_score }`。
- `NaiveQueryService` 在 vector chunk 检索后按 rerank score 重排 chunks。
- `RetrievalContextService.RetrieveChunksAsync` 在 KG `Mix` vector chunks 检索后按 rerank score 重排 chunks。
- `QueryParam.EnableRerank` 已经能控制是否调用 rerank。
- 测试中已有 fake rerank service 使用 `RerankResult.Index` 验证基本重排、重复 index 去重和非法 index 忽略。

但 .NET 缺少 Python 的长文档 rerank 保护层：

- 没有对超长 document/chunk 做 rerank 输入切片。
- 没有 `chunkIndex -> originalDocumentIndex` 映射。
- 没有把多个子片段 score 聚合回原始 chunk。
- 调用 provider 时仍直接传 `topN`，如果未来启用 chunking，会出现 API-level `topN` 截断子片段的错误语义。
- `Naive` 和 KG `Mix` 各自直接调用 `IRerankService`，缺少一个可共用、可单测的 rerank coordinator。

## Product Decision

新增一个小而硬的 rerank coordination layer：

- 引入 `RerankDocumentChunker` 或等价组件，负责把 rerank documents 切成子片段并返回映射。
- 引入 `RerankCoordinator` 或等价组件，负责：
  - 根据配置决定是否启用 rerank chunking。
  - 调用 `IRerankService`。
  - 将子片段 score 聚合回原始 document index。
  - 对外返回原始 document-level `RerankResult`。
- `NaiveQueryService` 和 `RetrievalContextService` 都通过同一层 rerank coordinator 排序 chunks。
- 默认聚合策略使用 Python 的 `max`。
- chunking 启用时，底层 provider call 的 `topN` 应覆盖所有子片段；document-level topN 在聚合后应用。
- chunking 禁用时，行为保持当前直接 rerank 语义。

首版推荐默认启用 chunking，但只在 document 超过 rerank chunk token limit 时实际切片。这样短文档完全不受影响，长文档得到 Python 语义保护。

## Architecture

建议结构：

```text
NaiveQueryService
  -> RerankCoordinator.RerankAsync(query, chunks, topK)

RetrievalContextService
  -> RerankCoordinator.RerankAsync(query, chunks, topK)

RerankCoordinator
  -> RerankDocumentChunker
  -> IRerankService
  -> aggregate chunk scores to original document indices
```

`RerankCoordinator` 不应该知道 `ChunkData` 的 storage 细节。推荐让它接受 document texts 并返回 document-level results：

```csharp
Task<List<RerankResult>> RerankAsync(
    string query,
    IReadOnlyList<string> documents,
    int topN,
    CancellationToken cancellationToken = default)
```

调用方继续负责把 `RerankResult.Index` 映射回 `ChunkData`。

`RerankDocumentChunker` 可以依赖现有 `ITokenizer`：

```csharp
RerankChunkingResult Chunk(
    IReadOnlyList<string> documents,
    int maxTokensPerDocument,
    int overlapTokens)
```

返回：

```text
Documents: rerank input subdocuments
DocumentIndices: subdocument index -> original document index
WasChunked: whether any document expanded
```

如果当前 tokenizer 只有 count 能力而没有 encode/decode 能力，首版可以使用 deterministic word/character approximation，只要测试明确锁住不死循环、overlap 生效和映射正确。不要为了这个切片改造全局 tokenizer 抽象。

## Configuration Boundary

首版不改 public API，不给 `QueryParam` 增加用户可见字段。推荐新增内部 options：

```text
Rerank:EnableChunking = true
Rerank:MaxTokensPerDocument = 480
Rerank:OverlapTokens = 32
```

如果配置缺失，使用 Python 参考默认值。`EnableRerank=false` 仍然完全跳过 rerank 和 rerank chunking。

`IRerankService` 接口可以保持不变。为了实现 chunking 语义，coordinator 在 chunking 启用时可以传入 `chunkedDocuments.Count` 作为 provider-level topN，确保 provider 返回足够多子片段；聚合后再应用用户的 document-level topN。

## Rerank Chunking Contract

短文档：

- 文档估算 token 数不超过 `MaxTokensPerDocument` 时，不切片。
- 输出 document 与输入 document 文本一致。
- `DocumentIndices` 为 `[0, 1, 2, ...]`。

长文档：

- 按 `MaxTokensPerDocument` 切成多个子片段。
- 相邻子片段保留 `OverlapTokens` 的重叠内容。
- 每个子片段都映射回原始 document index。
- `OverlapTokens >= MaxTokensPerDocument` 时，overlap 被 clamp 到 `MaxTokensPerDocument - 1`，保证循环前进。

空输入：

- 空 document list 返回空 chunking result。
- 空白 document 不应被传给 provider；如果当前 provider 仍会拒绝空白文档，coordinator 应保留现有失败语义或在设计实现中明确过滤策略，不能静默改变查询结果。

## Score Aggregation Contract

默认使用 `max` 聚合：

```text
docScore = max(score of all subdocuments mapped to doc)
```

聚合规则：

- 忽略 `RerankResult.Index < 0` 或超出子片段范围的 provider 结果。
- 同一子片段出现多次时，保留较高 score 或在聚合时自然取 max；最终每个原始 document 只出现一次。
- 未获得任何 score 的原始 document 不出现在聚合结果中。
- 聚合后按 score 降序排序。
- 聚合后再 `Take(topN)`，确保 topN 表示原始 document 数量。

## Query Flow Contract

`NaiveQueryService`：

- vector store 仍按 `ChunkTopK > 0 ? ChunkTopK : TopK` 取候选 chunks。
- `EnableRerank=false` 时不调用 coordinator。
- `EnableRerank=true` 时通过 coordinator 获取 document-level rerank results。
- 使用 document-level `RerankResult.Index` 映射回原始 `ChunkData`。
- 后续 context token budget、reference list、raw data 逻辑保持不变。

KG `Mix` vector chunks：

- `RetrievalContextService.RetrieveChunksAsync` 的 vector retrieval 保持不变。
- `EnableRerank=true` 时通过同一个 coordinator 重排 vector chunks。
- Entity/relation related chunks、KG context builder、reference list 和 final context budget 不在本阶段改动。

## Out of Scope

- 不实现新的 rerank provider。
- 不修改 Aliyun/Jina/Cohere 的真实 HTTP 协议兼容层，除非实现 coordinator 所需的最小调用调整。
- 不改变 public `QueryParam`、Server request、Blazor UI 或 Chat settings。
- 不改 KG entity/relation ranking、related chunk vector selection、context builder 或 prompt 模板。
- 不做真实 rerank API integration tests。
- 不引入 embedding、Qdrant、Neo4j 或 LLM 依赖。
- 不实现 `mean` / `first` 聚合的用户配置；首版仅内部锁定 `max`。

## Testing Strategy

Use strict TDD. No production code before a failing test.

Core unit tests:

- `RerankDocumentChunkerTests`
  - short documents are not chunked and preserve one-to-one indices
  - long document expands into multiple overlapping subdocuments
  - multiple documents preserve correct subdocument-to-document mapping
  - overlap is clamped when it is greater than or equal to max tokens
  - empty input returns empty result
- `RerankCoordinatorTests`
  - without chunking, provider receives original documents and `topN`
  - with chunking, provider receives subdocuments and provider-level `topN` covers all subdocuments
  - aggregated results use max score per original document
  - document-level `topN` is applied after aggregation
  - invalid provider indexes are ignored
  - duplicate subdocument indexes do not duplicate original documents
- `NaiveQueryServiceTests`
  - long chunk rerank results reorder original chunks by aggregated document score
  - `EnableRerank=false` still skips coordinator/provider
- `RetrievalContext` focused tests
  - KG `Mix` vector chunks use the shared coordinator for aggregated document-level rerank order
  - existing vector chunk parity behavior remains unchanged when rerank is disabled

Verification should include:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Rerank|FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext" --verbosity minimal
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

## Compatibility

No database migration is required.

Public query APIs remain compatible:

- `LightRAG.QueryAsync` signature does not change.
- `QueryParam` public shape does not change in this phase.
- Server and Web request models do not change.
- `IRerankService` can remain provider-facing and unchanged if coordinator handles the expanded document list.
- Short document behavior remains effectively identical because no chunking occurs.

The intended behavior change is limited to long documents when rerank is enabled: rerank quality should be based on aggregated subdocument scores while the caller still receives original chunks.

## Acceptance Criteria

- Short rerank documents keep existing one-document-one-score behavior.
- Long rerank documents are split into overlapping subdocuments and mapped back to original document indices.
- Rerank score aggregation uses max score per original document and returns document-level results.
- Chunking-enabled rerank does not apply provider-level `topN` before aggregation.
- `Naive` rerank uses aggregated document-level results to reorder original chunks.
- KG `Mix` vector chunk rerank uses the same aggregated document-level behavior.
- `EnableRerank=false` skips all rerank chunking and provider calls.
- No public API, UI, cache, indexing, deletion, prompt, or real storage behavior is changed.
- Focused rerank/query/retrieval tests pass.
- Full solution tests and build remain green after implementation.

## Implementation Notes

Keep this phase intentionally provider-agnostic. The coordinator should make provider calls safer by controlling the document list and post-processing results, but it should not learn Aliyun/Jina/Cohere protocol details.

Prefer deterministic tests with deliberately distinct scores. Avoid assertions that depend on dictionary iteration order or approximate tokenizer internals. If the initial chunker uses word/character approximation, document that as an internal implementation detail and keep the externally visible contract at “long documents split with overlap and stable mapping”.

Do not let this feature become prompt parity. Rerank chunking happens before context construction; prompt and final answer format stay out of scope.
