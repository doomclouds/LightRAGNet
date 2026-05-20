# Indexing LLM Cache Parity Design

- Date: `2026-05-20`
- Topic slug: `indexing-llm-cache-parity`
- Status: `Ready for review`
- Scope: `Core indexing pipeline + LLM cache + storage contract + tests`
- Tags: `lightrag-alignment`, `indexing-cache`, `entity-extraction`, `summary-cache`, `jsonl`, `tdd`

## Purpose

LightRAGNet is still early enough that the indexing cache contract should be corrected instead of preserved for compatibility. The current .NET implementation has an older chunk-level cache that stores a whole `ChunkResult` under `chunk.Id`. That cache mixes embedding vectors, parsed entities, parsed relations, and LLM extraction results in one record. It is useful as a local interruption optimization, but it does not match Python LightRAG's `llm_response_cache` contract and does not naturally feed `text_chunks[].llm_cache_list`.

This phase replaces that old cache contract with Python-aligned indexing LLM cache semantics:

- entity extraction LLM calls use `default:extract:{hash}` cache entries
- entity and relation summary LLM calls use `default:summary:{hash}` cache entries
- extract cache entries are linked from `text_chunks[chunkId].llm_cache_list`
- summary cache entries are not linked to a single chunk
- embedding vectors are not stored in `llm_cache`
- cache values use the same flattened record shape as query cache

The goal is not to add another optimization layer. The goal is to make indexing, re-indexing, deletion cleanup, and future repair flows share the same cache contract as Python LightRAG.

## Current .NET State

The repository currently has three separate pieces that almost connect but do not form a Python-compatible indexing cache:

1. `DocumentProcessingService.ProcessChunkAsync` checks `llm_cache` by `chunk.Id`.
2. The old cached value is a parsed `ChunkResult` containing embedding, entities, and relationships.
3. `DocumentDeletionService` already knows how to collect `llm_cache_list` from `text_chunks`, but normal text chunk writes do not populate that list from real indexing cache ids.

This creates a misleading state:

- repeated chunk processing can skip LLM calls in some cases
- deletion can delete cache ids if tests manually seed `llm_cache_list`
- production indexing does not reliably produce Python-style cache ids for deletion cleanup
- summary LLM calls are not cached

Because this project is still in an early phase, this design intentionally removes the legacy chunk result cache instead of supporting both contracts.

## Python Reference Semantics

Python LightRAG uses flattened LLM cache keys:

```text
{mode}:{cache_type}:{hash}
```

For indexing-stage LLM calls:

- entity extraction uses `mode="default"` and `cache_type="extract"`
- entity and relation summaries use `mode="default"` and `cache_type="summary"`
- the hash is computed from the full LLM prompt payload
- cache entries store LLM return text and metadata
- extract cache entries include `chunk_id`
- summary cache entries do not include `chunk_id`

Python's generic cache entry shape is:

```json
{
  "return": "...",
  "cache_type": "extract",
  "chunk_id": "chunk-...",
  "original_prompt": "...",
  "queryparam": null
}
```

For cache hits, Python returns the cached `return` text and `create_time`. For cache misses, Python calls the LLM, removes `<think>...</think>` blocks from the response, stores the response, and then returns it.

## Product Decision

Implement a clean Python-aligned cache contract for indexing:

- Remove the old `chunk.Id -> ChunkResult` LLM cache path.
- Keep the existing `llm_cache` KV namespace.
- Reuse and extend the flattened cache entry/key infrastructure created for query cache.
- Add `extract` and `summary` cache types.
- Add `EnableLlmCacheForEntityExtract = true` to match Python's switch name and semantics.
- Treat `EnableLlmCache=false` as the global cache kill switch.
- Use JSONL for summary description lists because Python does.
- Keep the KV store file format as keyed JSON object storage in this phase, because Python's JSON KV implementation also writes keyed `.json` files. JSONL here is prompt/input formatting, not the whole KV file format.

Do not implement embedding cache, cache migration tools, cache management UI/API, or shared pipeline-status semantics in this phase.

## Cache Options

Extend `LightRAGOptions`:

```csharp
public bool EnableLlmCache { get; set; } = true;
public bool EnableQueryCache { get; set; } = true;
public bool EnableKeywordCache { get; set; } = true;
public bool EnableLlmCacheForEntityExtract { get; set; } = true;
```

Effective behavior:

- `EnableLlmCache=false` disables all LLM cache reads and writes.
- `EnableLlmCacheForEntityExtract=false` disables indexing-stage `extract` and `summary` cache reads and writes.
- `EnableQueryCache=false` only disables query answer cache.
- `EnableKeywordCache=false` only disables query keyword cache.

The naming intentionally follows Python. Even though `summary` is not entity extraction, Python's `default` mode cache gate uses `enable_llm_cache_for_entity_extract`, and summaries are part of indexing/knowledge-graph construction.

## Cache Key Contract

Add cache type constants:

```text
extract
summary
```

Flattened key format:

```text
default:extract:{sha256}
default:summary:{sha256}
```

Hash input must be the canonical LLM prompt payload, not `chunk.Id`.

For entity extraction:

```text
user_prompt
system_prompt
history_messages_if_any
```

For summary:

```text
summary_prompt
```

The canonical string should follow Python's joining rule:

```text
safe_user_prompt
safe_system_prompt
history_json
```

Only non-empty parts are joined with `\n`. History JSON must use UTF-8 readable JSON with Chinese preserved, not escaped as `\uXXXX`.

## Cache Value Contract

Use one record shape for query and indexing cache:

```json
{
  "return": "...",
  "cache_type": "extract",
  "chunk_id": "chunk-...",
  "original_prompt": "...",
  "queryparam": null,
  "create_time": 1779273600
}
```

Rules:

- `return` stores the raw LLM text response for `extract` and `summary`.
- `cache_type` is `extract` or `summary`.
- `chunk_id` is the chunk id for `extract`.
- `chunk_id` is `null` for `summary`.
- `original_prompt` is the canonical prompt payload used for hashing.
- `queryparam` is `null` for indexing cache.
- `create_time` is a Unix timestamp generated when the LLM miss completes.

Malformed entries must be treated as cache misses. They should produce a warning but must not fail indexing unless the fresh LLM call also fails.

## Entity Extraction Flow

Refactor chunk processing around raw LLM responses:

1. Build the entity extraction system prompt and user prompt from the chunk content, entity types, and extraction limits.
2. Compute the `default:extract:{hash}` cache key from the canonical prompt payload.
3. If indexing cache is enabled, try to read the cache entry.
4. On hit, parse `entry.return` into entities and relationships.
5. On miss, call the LLM, clean `<think>` tags, save the raw response as `return`, then parse it.
6. Add source chunk id, file path, and timestamp to parsed entities and relationships.
7. Return a `ChunkResult` that includes `LlmCacheKeys = [extractCacheKey]`.
8. Generate embeddings independently and do not store them in `llm_cache`.

The public implementation does not need to keep using `ILLMService.ExtractEntitiesAsync` internally. To align with Python, the indexing pipeline should move prompt building and parsing into reusable services that can work with `ILLMService.GenerateAsync`:

```text
Services/DocumentProcessing/
  EntityExtractionPromptBuilder.cs
  EntityExtractionResultParser.cs
```

This makes cache hashing depend on the actual prompt sent to the LLM, not on a guessed parameter set.

## Text Chunk Cache References

Text chunk records must always include `llm_cache_list`:

```json
{
  "content": "...",
  "tokens": 120,
  "chunk_order_index": 0,
  "full_doc_id": "doc-...",
  "file_path": "file.md",
  "llm_cache_list": ["default:extract:..."]
}
```

Implementation rule:

- initialize `llm_cache_list` to an empty list for every chunk
- after `ProcessChunkAsync`, write the extract cache keys returned by the chunk result
- keep list order stable and de-duplicated

The easiest .NET shape is:

```csharp
public sealed class ChunkResult
{
    public string ChunkId { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public List<Entity> Entities { get; set; } = [];
    public List<Relationship> Relationships { get; set; } = [];
    public List<string> LlmCacheKeys { get; set; } = [];
}
```

Then `LightRAG.InsertAsync` can build `text_chunks` from both the original `Chunk` and matching `ChunkResult`.

## Summary Cache Flow

Python summary cache is not chunk-owned. It is a normal LLM cache call with `cache_type="summary"` and no `chunk_id`.

LightRAGNet should do the same:

1. `DescriptionMerger` decides whether an LLM summary is required using the existing token/count thresholds.
2. When summary is required, build the summary prompt from `description_type`, `description_name`, JSONL descriptions, summary length, and language.
3. Compute the `default:summary:{hash}` cache key from the prompt.
4. On hit, return the cached `return` value.
5. On miss, call the LLM, clean `<think>` tags, save the summary response, and return it.
6. Do not add summary cache keys to any chunk's `llm_cache_list`.

This means document deletion does not delete summary cache entries. That is intentional. Summary cache is derived from merged description content and may be shared across entities, relations, chunks, or repeated rebuilds. It has no single safe chunk owner.

## JSONL vs JSON Decision

The user-visible confusion is understandable: Python uses JSONL in summary prompt construction, but Python's JSON KV storage still writes a keyed `.json` file.

### JSON

JSON is one complete document:

```json
[
  { "Description": "first" },
  { "Description": "second" }
]
```

It is good when the whole value is one object or array. It is also natural for keyed KV storage:

```json
{
  "default:extract:abc": { "return": "...", "cache_type": "extract" },
  "default:summary:def": { "return": "...", "cache_type": "summary" }
}
```

### JSONL

JSONL means "JSON Lines": each line is an independent JSON object.

```jsonl
{ "Description": "first" }
{ "Description": "second" }
```

It is useful when the data is a sequence of records:

- LLM prompts can show one record per line.
- Token truncation can keep or drop records line by line.
- Streaming and log-like processing is easier.
- It avoids an outer array wrapper that adds little value for prompt input.

### Phase Decision

Use JSONL where Python uses JSONL:

- summary description lists
- future query/retrieval context blocks that are record sequences

Do not switch `llm_cache` KV file persistence to JSONL in this phase:

- Python `JsonKVStorage` writes `kv_store_<namespace>.json`, not `.jsonl`.
- `IKVStore.GetByIdAsync`, `UpsertAsync`, and `DeleteAsync` are key-addressed operations.
- JSONL is poor for in-place keyed delete/update without rewriting or tombstones.
- Changing all KV stores to JSONL is a storage-engine redesign, not an indexing cache fix.

If a future storage phase wants JSONL files, it should be designed as an append-friendly log or import/export format, not as a silent replacement for current keyed KV JSON files.

## Deletion Semantics

Document deletion already collects cache ids from `text_chunks[].llm_cache_list`. After this phase:

- `deleteLlmCache=false` keeps all cache entries
- `deleteLlmCache=true` deletes only extract cache ids linked from deleted chunks
- summary cache entries remain
- query cache invalidation still happens through workspace query revision bump

This matches the ownership boundary:

- extract cache belongs to a chunk
- summary cache belongs to a prompt
- query cache belongs to workspace revision and query parameters

## Testing Strategy

Use TDD. No production implementation should be changed before a failing test describes the desired behavior.

### Cache Key and Entry Tests

- `BuildExtractKey` creates `default:extract:{hash}`.
- `BuildSummaryKey` creates `default:summary:{hash}`.
- indexing cache entries serialize `return`, `cache_type`, `chunk_id`, `original_prompt`, `queryparam`, and `create_time`.
- Chinese prompt text is not written as Unicode escapes.

### Entity Extraction Tests

- cache miss calls LLM and stores a `default:extract:{hash}` entry.
- cache hit does not call LLM.
- malformed extract cache entry falls back to LLM.
- parsed cache hit still assigns current `SourceId`, `SourceChunkId`, `FilePath`, and timestamp.
- `EnableLlmCache=false` skips read/write.
- `EnableLlmCacheForEntityExtract=false` skips read/write.

### Text Chunk Tests

- inserted text chunks include `llm_cache_list`.
- extract cache keys returned from chunk processing are written into the matching text chunk.
- duplicate cache keys are not written twice.

### Summary Tests

- summary cache miss calls LLM and stores `cache_type="summary"` with `chunk_id=null`.
- summary cache hit does not call LLM.
- summary description list is JSONL, not a JSON array.
- summary cache keys are not written to chunk `llm_cache_list`.

### Deletion Tests

- deleting with `deleteLlmCache=true` removes real extract cache keys produced by indexing.
- deleting with `deleteLlmCache=false` keeps extract cache keys.
- summary cache entries are not deleted through chunk deletion.

### Regression Tests

- old `chunk.Id -> ChunkResult` cache is no longer used.
- embedding is generated on insert even when extract cache hits.
- query cache tests still pass.
- document lifecycle re-index after missing vectors can reuse extract cache but still rebuild embeddings and chunk vectors.

## Migration and Compatibility

No legacy cache migration is required.

Project policy for this phase:

- old `chunk.Id` cache entries may remain in existing local files
- new code will not read them
- clean-data tools may delete `llm_cache.json` when developers want a fresh local state
- tests must not rely on the old cache shape

This is acceptable because the project is still in startup alignment mode and the user explicitly chose a clean Python-aligned redesign over backward compatibility.

## Implementation Units

Recommended unit boundaries:

```text
src/LightRAGNet/Services/QueryCache/
  LightRagCacheKeyBuilder.cs        # add extract/summary key builders
  LightRagCacheEntry.cs             # ensure chunk_id support
  LightRagLlmCacheService.cs        # add generic indexing get/save helpers

src/LightRAGNet/Services/DocumentProcessing/
  EntityExtractionPromptBuilder.cs
  EntityExtractionResultParser.cs
  DocumentProcessingService.cs
  Chunk.cs

src/LightRAGNet/Services/KnowledgeGraphMerge/
  SummaryPromptBuilder.cs
  DescriptionMerger.cs

src/LightRAGNet/
  LightRAG.cs
  LightRAGOptions.cs
```

The query cache service can remain named `LightRagLlmCacheService` if it becomes the generic LLM cache façade. If that name becomes misleading, rename or wrap it as `LightRagPromptCacheService` in a separate refactor.

## Acceptance Criteria

- New indexing extract cache keys use `default:extract:{hash}`.
- New summary cache keys use `default:summary:{hash}`.
- `llm_cache` no longer stores embedding vectors.
- `text_chunks` records include `llm_cache_list`.
- Extract cache ids are written into `llm_cache_list`.
- Summary cache ids are not written into `llm_cache_list`.
- Summary prompt descriptions use JSONL.
- `deleteLlmCache=true` removes extract cache entries linked to deleted chunks.
- Legacy `chunk.Id -> ChunkResult` cache reads are removed.
- All new behavior is covered by failing-first tests.

## Out of Scope

- Embedding cache.
- Cache management UI.
- Cache migration from old local files.
- Prefix-scan cleanup for orphaned summary/query cache.
- Python pipeline shared-status semantics.
- Changing every KV store file from JSON object format to JSONL.

## Open Review Points

1. Whether `EnableLlmCacheForEntityExtract` should govern summary cache exactly like Python, or whether .NET should add a separate `EnableSummaryCache`.
2. Whether old local `llm_cache.json` should be ignored silently or whether `CleanData` should proactively remove legacy chunk-id entries.
3. Whether `ILLMService.ExtractEntitiesAsync` and `SummarizeAsync` should remain public convenience methods after the indexing pipeline moves to prompt builders plus `GenerateAsync`.
