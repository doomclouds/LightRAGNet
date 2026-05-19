# Query Mode Context Parity Design

- Date: `2026-05-19`
- Topic slug: `query-mode-context-parity`
- Status: `Ready for review`
- Scope: `Core query orchestration + retrieval context + tests`
- Tags: `lightrag-alignment`, `query-mode`, `naive`, `bypass`, `tdd`, `raw-data`

## Purpose

LightRAGNet already has the Python query modes in `QueryMode`, but not all modes have correct behavior. `Naive` and `Bypass` currently lack first-class .NET execution paths, and unsupported KG strategy modes silently fall back to `Mix`. This phase makes query behavior explicit, testable, and closer to Python LightRAG before deeper retrieval ranking work.

## Python Reference Semantics

The C# implementation should align with these Python behaviors:

- `local`, `global`, `hybrid`, and `mix` use `kg_query`.
- `naive` uses `naive_query`, which retrieves document chunks from the chunk vector store and does not query the knowledge graph.
- `bypass` directly calls the LLM without keyword extraction, vector search, graph search, or RAG context construction.
- Empty query returns the fail response.
- For KG modes, if extracted high-level and low-level keywords are both empty:
  - query length under 50 characters forces the original query into low-level keywords.
  - longer queries return the fail response.
- `only_need_context` returns the constructed context without LLM generation.
- `only_need_prompt` returns the complete prompt plus `---User Query---`.
- Raw data contains structured `data` and `metadata` suitable for UI/debug use.

## Current .NET Gap

- `QueryMode.Naive` and `QueryMode.Bypass` exist in `src/LightRAGNet.Core/Models/QueryMode.cs`.
- `KGSearchStrategyFactory` maps only `Local`, `Global`, `Hybrid`, and `Mix`; every other mode currently falls back to `Mix`.
- `LightRAG.QueryAsync` always extracts keywords and builds retrieval context before generating, so `Bypass` cannot behave as direct LLM.
- `RetrievalContextService` only retrieves vector chunks inside `Mix`, not a standalone `Naive` path.
- Raw data currently exposes references and basic keyword fields, but not enough processing information for parity-oriented tests.

## Product Decision

Implement a bounded query parity phase:

- Make query mode routing explicit.
- Add a dedicated `NaiveQueryService` for vector-only chunk context.
- Add a direct `Bypass` branch in `LightRAG.QueryAsync`.
- Add a testable keyword fallback policy for KG modes.
- Expand raw data for KG and Naive contexts enough to support UI/debug assertions.

Do not implement query LLM cache in this phase. Query cache is important, but it cuts across cache key construction, invalidation semantics, and streaming behavior. It should follow after mode contracts are stable.

## Architecture

Add a small query orchestration area:

```text
src/LightRAGNet/Services/Query/
  NaiveQueryService.cs
  NaiveQueryPromptBuilder.cs
  QueryKeywordPolicy.cs
```

`LightRAG.QueryAsync` remains the public entry point, but it delegates by mode:

```text
Bypass -> direct LLM
Naive  -> NaiveQueryService -> NaiveQueryPromptBuilder -> LLM
KG     -> QueryKeywordPolicy -> RetrievalContextService -> KG prompt -> LLM
```

`RetrievalContextService` remains responsible for KG context only. It must reject `Naive` and `Bypass` if called directly so future bugs fail loudly instead of becoming accidental `Mix` queries.

## Naive Query Contract

`NaiveQueryService` retrieves chunks from the `chunks` vector collection:

- `topK = queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK`
- collection name: `chunks`
- no keyword extraction
- no graph store calls
- optional rerank when `EnableRerank` is true
- dynamic chunk token budget:
  - `MaxTotalTokens - promptOverhead - queryTokens - 200`
  - never below zero
- reference IDs generated with existing `ReferenceListBuilder`

`promptOverhead` must be computed from the same Naive prompt template used for LLM generation with an empty context. Do not estimate it with an unrelated constant; otherwise the chunk limiter and final prompt can drift.

Naive context should include JSON-line chunk entries and a reference list:

```text
---Document Chunks---
{"reference_id":"1","content":"..."}

---Reference Document List---
[1] path/to/file.md
```

## Bypass Query Contract

`Bypass` is the simplest mode:

- return fail response for empty query, consistent with existing behavior
- call `ILLMService.GenerateAsync` or `GenerateStreamAsync` directly
- pass `queryParam.ConversationHistory`
- pass `systemPrompt: null`
- do not call `ExtractKeywordsAsync`
- do not call `RetrievalContextService`
- do not call vector, graph, embedding, rerank, or tokenizer services
- return empty raw data:

```json
{
  "data": {},
  "metadata": {
    "query_mode": "Bypass"
  }
}
```

## Raw Data Contract

KG context raw data should include:

```text
data.entities
data.relationships
data.chunks
data.references
metadata.query_mode
metadata.keywords.high_level
metadata.keywords.low_level
metadata.processing_info.total_entities_found
metadata.processing_info.total_relations_found
metadata.processing_info.final_chunks_count
```

Naive context raw data should include:

```text
data.entities = []
data.relationships = []
data.chunks
data.references
metadata.query_mode = "Naive"
metadata.keywords.high_level = []
metadata.keywords.low_level = []
metadata.processing_info.total_chunks_found
metadata.processing_info.final_chunks_count
```

The old flat `high_level_keywords` and `low_level_keywords` metadata keys may remain for compatibility during this phase, but new tests should assert the nested `metadata.keywords` contract.

`QueryResult.ReferenceList` must be able to read the reference list emitted by these raw data structures. It should support both the current `List<Dictionary<string, object>>` shape and any older `List<object>` shape so callers do not see an empty reference list when raw data contains references.

## Testing Strategy

Use TDD and keep normal tests independent of Docker.

Core tests:

- `QueryKeywordPolicyTests`
  - supplied keywords pass through unchanged
  - empty keywords with short query force low-level original query
  - empty keywords with long query return no-context/fail decision
- `KGSearchStrategyFactoryTests`
  - `Local`, `Global`, `Hybrid`, and `Mix` map explicitly
  - `Naive` and `Bypass` throw instead of falling back to `Mix`
- `RetrievalContextServiceModeTests`
  - direct `BuildQueryContextAsync` with `Naive` or `Bypass` throws a clear error
- `NaiveQueryServiceTests`
  - vector-only retrieval uses `chunks`
  - `ChunkTopK` overrides `TopK`
  - no chunks returns `null`
  - rerank reorders chunks when enabled
  - context and raw data include chunks, references, and processing info
- `LightRAGQueryModeTests`
  - `Bypass` calls LLM directly and skips keyword extraction
  - `Naive` skips keyword extraction and uses naive context
  - `OnlyNeedContext` and `OnlyNeedPrompt` work for Naive
  - streaming and non-streaming generation work for Naive

## Migration and Compatibility

No database migration is required.

The public `QueryParam` and `QueryResult` models remain compatible. Existing callers using `Local`, `Global`, `Hybrid`, or `Mix` keep the same public API. The main behavior change is that unsupported KG strategy usage fails explicitly instead of silently behaving like `Mix`.

## Associated Impact

- Dependency injection changes because `LightRAG` receives `NaiveQueryService`; update DI registration and direct test constructors.
- `RagQueryController` currently creates `QueryParam` with default `Mix` mode. This phase should not expose a public mode selector in the API or Web UI; existing chat behavior should remain Mix + streaming.
- `LightRAGNet.Web` consumes SSE text chunks only, not raw data. No Web component change is required unless a future phase exposes references or mode selection in chat.
- `LightRAGNet.Example` uses `QueryResult.ReferenceList`; raw data shape changes must keep that property useful instead of silently empty.
- `IncludeReferences` is currently not part of query generation behavior. Do not reinterpret it in this phase.
- No Qdrant, Neo4j, SQLite, or file storage schema change is expected.
- Query LLM cache remains unchanged because this phase does not implement cache lookup or cache persistence.

## Out of Scope

- Query LLM cache key construction and persistence.
- Batch embedding optimization for KG search.
- Deep rerank/ranking algorithm parity.
- UI query page redesign.
- API/Web mode selection for `Naive` or `Bypass`.
- Changing `IncludeReferences` semantics.
- Real Qdrant/Neo4j query integration tests.
- Prompt text perfect parity with every Python prompt template.

## Acceptance Criteria

- `Bypass` is direct LLM generation and performs no retrieval work.
- `Naive` is chunk-vector-only retrieval and performs no KG work.
- `Naive` supports `OnlyNeedContext`, `OnlyNeedPrompt`, streaming, and non-streaming results.
- KG keyword fallback matches the Python short-query behavior.
- `KGSearchStrategyFactory` no longer silently maps unsupported modes to `Mix`.
- Raw data exposes chunks, references, nested keywords, and processing info for KG and Naive contexts.
- New tests run in the normal `dotnet test LightRAGNet.slnx` suite without Docker.
- Existing deletion, lifecycle, task queue, and retrieval component tests remain green.
