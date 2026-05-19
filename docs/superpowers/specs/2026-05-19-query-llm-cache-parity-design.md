# Query LLM Cache Parity Design

- Date: `2026-05-19`
- Topic slug: `query-llm-cache-parity`
- Status: `Ready for review`
- Scope: `Core query orchestration + LLM cache + tests`
- Tags: `lightrag-alignment`, `query-cache`, `keywords-cache`, `workspace-revision`, `tdd`

## Purpose

LightRAGNet already has explicit `QueryMode` routing, `Naive` vector-only query, `Bypass` direct LLM query, and structured raw data for KG and Naive contexts. The next most valuable Python LightRAG parity step is to add bounded LLM cache support for query-time work without turning cache into a broad storage rewrite.

This phase adds cache parity for two expensive query-time operations:

- keyword extraction for KG query modes
- final non-streaming query responses for KG, Naive, and Bypass modes

The design intentionally protects correctness over maximum reuse. Cached RAG answers must not survive knowledge-base changes, and conversation-dependent answers must not be reused across different chat histories.

## Python Reference Semantics

Python LightRAG stores LLM cache entries in a flattened key shape:

```text
{mode}:{cache_type}:{hash}
```

Relevant Python behaviors:

- query response cache uses `cache_type="query"`
- keyword extraction cache uses `cache_type="keywords"`
- cache values store the generated return content, cache type, original prompt, and query parameters
- streaming responses are not saved as normal string cache entries
- query cache keys include query mode, query text, response type, top-k settings, token budgets, keywords, user prompt, and rerank flag

LightRAGNet should follow those semantics where they fit the .NET architecture, but it must also add a workspace revision dimension for RAG answer correctness.

## Current .NET Gap

- `KVContracts.LLMCache` already exists and is wired into `LightRAG`.
- Document deletion can optionally delete chunk-linked LLM cache ids.
- `LightRAG.QueryAsync` still extracts keywords and calls the LLM on every request.
- `QueryNaiveAsync` and `QueryBypassAsync` also call the LLM every time for non-streaming answers.
- There is no query cache invalidation boundary when documents are inserted or deleted.

## Product Decision

Implement a narrow query-cache parity phase:

- Add a small cache service and deterministic cache-key builder.
- Reuse the existing `llm_cache` KV store.
- Cache KG keyword extraction results.
- Cache final non-streaming query answers for KG, Naive, and Bypass.
- Add a workspace query revision to KG and Naive query cache keys.
- Bump the workspace revision after successful indexed document insert/delete/clear-all.
- Skip query answer cache when conversation history is present.
- Skip query answer cache for streaming, `OnlyNeedContext`, and `OnlyNeedPrompt`.

Do not implement embedding cache, entity extraction cache, summary cache, cache migration tools, cache management UI/API, or full Python pipeline shared-status semantics in this phase.

## Architecture

Add a focused cache area under the core `LightRAGNet` project:

```text
src/LightRAGNet/Services/QueryCache/
  LightRagLlmCacheService.cs
  LightRagCacheKeyBuilder.cs
  LightRagCacheEntry.cs
  LightRagCacheOptions.cs
```

Responsibilities:

- `LightRagCacheKeyBuilder` creates stable SHA-256 based keys using normalized query parameters.
- `LightRagLlmCacheService` reads and writes cache entries through the keyed `KVContracts.LLMCache` store.
- `LightRagLlmCacheService` owns workspace query revision read and bump behavior.
- `LightRAG.QueryAsync`, `QueryNaiveAsync`, and `QueryBypassAsync` decide when cache may be used, but do not manually build serialized cache records.

The cache service should not require new `IKVStore` capabilities such as prefix scanning. That keeps JSON, in-memory, and future stores compatible.

## Cache Options

Add these options to `LightRAGOptions`:

```csharp
public bool EnableLlmCache { get; set; } = true;
public bool EnableQueryCache { get; set; } = true;
public bool EnableKeywordCache { get; set; } = true;
```

Effective behavior:

- `EnableLlmCache=false` disables all query-time LLM cache reads and writes.
- `EnableQueryCache=false` disables only final query answer cache.
- `EnableKeywordCache=false` disables only keyword extraction cache.

## Cache Key Contract

Use flattened keys:

```text
{mode}:{cache_type}:{hash}
```

Supported `cache_type` values in this phase:

- `keywords`
- `query`
- `metadata`

KG and Naive query cache key inputs:

```text
workspace
workspace_query_revision
mode
query
response_type
top_k
chunk_top_k
max_entity_tokens
max_relation_tokens
max_total_tokens
high_level_keywords
low_level_keywords
user_prompt
enable_rerank
```

Bypass query cache key inputs:

```text
mode
query
response_type
user_prompt
```

Bypass does not depend on RAG storage and should not include the workspace revision.

Keyword cache key inputs:

```text
workspace
mode
query
language_or_default_marker
```

If LightRAGNet does not yet have a language option equivalent to Python `addon_params.language`, use a stable default marker for this phase.

## Cache Value Contract

Store values compatible with the existing KV object shape:

```json
{
  "return": "...",
  "cache_type": "query",
  "original_prompt": "...",
  "queryparam": {},
  "create_time": 1234567890
}
```

Keyword cache entries store JSON in `return`:

```json
{
  "high_level_keywords": ["..."],
  "low_level_keywords": ["..."]
}
```

Invalid keyword cache JSON must be treated as a cache miss and should not fail the query.

## Workspace Revision

Store the current RAG answer revision in `llm_cache`:

```text
metadata:query_revision:{workspace}
```

Value:

```json
{
  "revision": 12,
  "updated_at": "2026-05-19T00:00:00Z"
}
```

Bump rules:

- successful new document insert bumps the workspace query revision
- duplicate insert short-circuit does not bump
- failed insert does not bump
- successful indexed document deletion bumps the workspace query revision
- idempotent delete where no RAG data exists does not need to bump
- clear-all completion bumps the workspace query revision

The first implementation may store revision as a monotonically increasing integer in the KV value. If the value is missing or malformed, treat the current revision as `0`.

## Query Flow

### KG Modes

1. Reject empty query as today.
2. If caller supplied keywords, use them directly and skip keyword cache.
3. If keyword cache is enabled, try `keywords` cache.
4. On keyword cache miss, call `ExtractKeywordsAsync`, normalize keyword fallback, then save valid extracted keywords.
5. Build retrieval context as today.
6. Return `OnlyNeedContext` and `OnlyNeedPrompt` results without query answer cache.
7. If `Stream=true`, call streaming LLM without query answer cache.
8. If conversation history is non-empty, call LLM without query answer cache.
9. Otherwise, try `query` cache using the workspace revision and full query parameters.
10. On miss, call non-streaming LLM and save the answer.

### Naive Mode

1. Reject empty query as today.
2. Build Naive context as today.
3. Return `OnlyNeedContext` and `OnlyNeedPrompt` results without query answer cache.
4. If `Stream=true`, call streaming LLM without query answer cache.
5. If conversation history is non-empty, call LLM without query answer cache.
6. Otherwise, try `query` cache with workspace revision and Naive parameters.
7. On miss, call non-streaming LLM and save the answer.

### Bypass Mode

1. Reject empty query as today.
2. Return `OnlyNeedContext` and `OnlyNeedPrompt` results without query answer cache.
3. If `Stream=true`, call streaming LLM without query answer cache.
4. If conversation history is non-empty, call LLM without query answer cache.
5. Otherwise, try `query` cache without workspace revision.
6. On miss, call non-streaming LLM and save the answer.

## Error Handling

- Cache read failures should log a warning and proceed as cache miss.
- Cache write failures should log a warning and return the generated answer.
- Malformed keyword cache values should log a warning and proceed with live extraction.
- Malformed revision values should log a warning and behave as revision `0`.
- Cache behavior must never change the no-context fail response semantics.

## Testing Strategy

Core tests should stay independent of Docker.

Add tests under:

```text
tests/LightRAGNet.Tests/QueryCache/
```

Cache key and value tests:

- same inputs produce the same flattened key
- different workspace revisions produce different KG/Naive query keys
- Bypass query keys do not include workspace revision
- query parameter ordering for keyword lists is deterministic

Keyword cache tests:

- cache hit skips `ExtractKeywordsAsync`
- cache miss calls `ExtractKeywordsAsync` and stores valid keywords
- malformed keyword cache falls back to live extraction
- supplied keywords skip keyword cache

Query cache tests:

- KG non-streaming cache hit skips final `GenerateAsync`
- Naive non-streaming cache hit skips final `GenerateAsync`
- Bypass non-streaming cache hit skips final `GenerateAsync`
- cache miss stores generated non-streaming response
- `Stream=true` skips query answer cache
- `OnlyNeedContext` and `OnlyNeedPrompt` skip query answer cache
- non-empty conversation history skips query answer cache

Revision tests:

- successful new insert bumps revision
- duplicate insert does not bump revision
- failed insert does not bump revision
- successful indexed delete bumps revision
- a query cached before revision bump does not satisfy a query after revision bump

## Compatibility

No database migration is required.

The public `QueryParam` and `QueryResult` shapes remain compatible. Existing callers get the same answers, but repeated eligible non-streaming queries can skip LLM calls. Existing cache deletion behavior for document-linked cache ids remains unchanged.

## Out of Scope

- Embedding cache.
- Entity extraction cache.
- Entity/relation summary cache.
- Cache migration tools.
- Cache management API or Blazor UI.
- Prefix-scan cache cleanup.
- Full Python pipeline busy/shared status semantics.
- Streaming query response cache.
- Query cache when `ConversationHistory` is non-empty.
- Prompt text perfect parity with Python.

## Acceptance Criteria

- Eligible KG keyword extraction can be served from cache.
- Eligible KG, Naive, and Bypass non-streaming answers can be served from cache.
- Streaming and conversation-history queries do not use query answer cache.
- `OnlyNeedContext` and `OnlyNeedPrompt` do not write query answer cache.
- Insert/delete/clear-all storage mutations cannot reuse stale KG/Naive answer cache.
- Cache failures degrade to live LLM behavior.
- New tests run in the normal `dotnet test LightRAGNet.slnx` suite without Docker.
- Existing query, deletion, lifecycle, task queue, and server tests remain green.
