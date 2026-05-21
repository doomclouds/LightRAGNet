# KG Context Builder Parity Design

- Date: `2026-05-21`
- Topic slug: `kg-context-builder-parity`
- Status: `Ready for review`
- Scope: `Core retrieval context formatting + token budget + tests`
- Tags: `lightrag-alignment`, `kg-query`, `context-builder`, `token-budget`, `tdd`

## Purpose

LightRAGNet 已经补齐了 Python LightRAG 对齐链路里的几个关键阶段：query mode 路由、query LLM cache、indexing LLM cache，以及 KG related chunks 的 `VECTOR` 选择。下一层最影响真实问答质量和后续调试稳定性的，不是继续加 UI 或 cache 管理，而是把 KG query 最终喂给 LLM 的 context 产物稳定下来。

当前 `RetrievalContextService` 仍直接拼接 KG context 文本，并使用偏人工可读的格式：

- entity: `Name (Type): Description`
- relationship: `Source -> Target: Keywords - Description`
- chunk: 通过文件名而不是 `reference_id` 关联 reference list

这种格式能工作，但和 Python LightRAG 的 context 合同有明显漂移。更麻烦的是，token budget 和实际 context 格式绑定在同一个大服务里，后续要继续对齐 prompt、raw data、引用展示或 rerank 行为时，容易把检索流程、格式化和预算计算搅成一锅粥。我们要先把这块拆出来，别让客厅继续堆满家具。

## Python Reference Semantics

Python LightRAG 的 KG context 构造集中在 `operate.py` 与 `prompt.py`：

- `_build_query_context(...)` 使用四阶段流程：
  - search
  - token truncation
  - merge chunks
  - build LLM context
- `_build_context_str(...)` 使用 `PROMPTS["kg_query_context"]` 模板。
- `kg_query_context` 包含四段：
  - `Knowledge Graph Data (Entity)`
  - `Knowledge Graph Data (Relationship)`
  - `Document Chunks`
  - `Reference Document List`
- entities、relations、text chunks 都以 JSON 文本进入对应 section。
- text chunks 使用 `reference_id`。
- reference list 使用同一个 `reference_id` 作为用户可见引用锚点。
- chunk token budget 基于最终 prompt/context 结构做动态计算：
  - `max_total_tokens`
  - 减去 system prompt overhead
  - 减去 KG context tokens
  - 减去 query tokens
  - 减去固定 buffer `200`
- 如果最终没有 entity、relation、chunk context，则返回 no-context。

这轮不追求 Python prompt text 的逐字一致，但要把 context 的结构化合同和 token 预算边界对齐到可以继续演进的形态。

## Current .NET Gap

当前 .NET 已有这些基础：

- `RetrievalContextService.BuildQueryContextAsync` 已经输出 `QueryContextResult.Context` 和 `RawData`。
- `ReferenceListBuilder` 已经能为 chunks 生成稳定 `reference_id`。
- `NaiveQueryService` 已经采用 chunk JSON line + reference list 的方向，接近 Python 的 `naive_query_context`。
- `TokenBudgetPlanner` 和 `ChunkTokenLimiter` 已存在，并已有单元测试。
- `RetrievalContextServiceRawDataTests` 已覆盖 nested keywords、references、processing info 等 raw data 合同。

但 KG context 仍存在这些偏差：

- KG entities / relationships 在 LLM context 中不是 JSON 结构，而是手写文本行。
- KG chunks 在 LLM context 中用文件名引用，和 raw data/reference id 不是同一锚点。
- reference list 文案强调文件名引用，而不是 `reference_id`。
- context 构造、entity/relation token 截断、chunk token 预算、reference list 输出都堆在 `RetrievalContextService` 内部。
- `TokenBudgetPlanner` 现在接受调用方传入的 `systemPrompt`、`knowledgeGraphContext` 等字符串，但 KG context 的最终格式仍由大服务临时拼接，测试很难直接锁住“预算输入格式”和“实际输出格式”一致。

## Product Decision

实现一个小而硬的 KG context builder parity phase：

- 新增或抽出 `KgQueryContextBuilder`，让它负责 KG context 的最终文本构造和 context 级 raw data 对齐。
- `RetrievalContextService` 继续负责检索、搜索策略、related chunk 选择和 orchestration。
- KG context 输出改为结构化 JSON section：
  - entities: JSON lines or JSON array entries，字段至少包含 `entity`、`type`、`description`。
  - relationships: JSON lines or JSON array entries，字段至少包含 `entity1`、`entity2`、`keywords`、`description`。
  - chunks: JSON lines or JSON array entries，字段至少包含 `reference_id`、`content`。
  - references: `[reference_id] file_path`。
- chunks 的 `reference_id` 必须与 raw data `data.chunks[].reference_id` 和 `data.references[].reference_id` 一致。
- token budget 计算必须以最终 KG context builder 使用的模板为准，避免预算和输出各算各的。
- 保持现有 no-context 行为：没有 entity、relationship、chunk 时返回 `null` 或既有 no-context 分支，不生成空壳 context。

## Architecture

目标结构：

```text
RetrievalContextService
  -> KGSearchStrategyFactory / IKGSearchStrategy
  -> related chunk selection
  -> ReferenceListBuilder
  -> KgQueryContextBuilder
       - build entity context
       - build relationship context
       - build chunk context with reference_id
       - build reference list
       - expose token-countable final context shape
```

`RetrievalContextService` 仍返回 `QueryContextResult`，外部 API 不变。

`KgQueryContextBuilder` 的输入应该尽量接近已经检索完成的数据，而不是重新访问存储：

```text
KGSearchResult searchResult
QueryParam queryParam
string query
```

builder 可以内部使用：

- `ReferenceListBuilder`
- `ITokenizer`
- `LightRAGJsonOptions.HumanReadable`

如果实现时发现 entity/relation 截断需要保留在 service 外部，也应优先把“按照最终 JSON 行计算 token”移动到 builder 附近，而不是继续按旧文本格式计算。

## Context Format Contract

KG context 应包含这些 section。标题不要求逐字等同 Python，但应稳定、可测试，并和后续 prompt parity 兼容：

````text
Knowledge Graph Data (Entity):

```json
{"entity":"ALPHA","type":"concept","description":"..."}
```

Knowledge Graph Data (Relationship):

```json
{"entity1":"ALPHA","entity2":"BETA","keywords":"depends on","description":"..."}
```

Document Chunks (Each entry has a reference_id refer to the `Reference Document List`):

```json
{"reference_id":"1","content":"..."}
```

Reference Document List (Each entry starts with a [reference_id] that corresponds to entries in the Document Chunks):

```
[1] docs/a.md
```
````

When a section has no data, omit that data block unless omitting all sections would produce no-context.

## Token Budget Contract

The phase should make token accounting testable at the builder boundary:

- Entity truncation counts the JSON representation that will appear in the final entity section.
- Relationship truncation counts the JSON representation that will appear in the final relationship section.
- Chunk truncation counts the final chunk section plus reference list impact, because `reference_id` and references are part of the real prompt context.
- Available chunk tokens should keep the current high-level formula:

```text
availableChunkTokens =
  MaxTotalTokens
  - systemPromptOverheadTokens
  - queryTokens
  - kgContextTokens
  - reserved/buffer tokens
```

This design does not require changing public `QueryParam` defaults. The goal is consistency first: the same context shape used for token estimates must be the one returned to LLM generation and `OnlyNeedContext`.

## Testing Strategy

Use strict TDD. No production code before a failing test.

Core tests:

- `KgQueryContextBuilderTests`
  - builds entity JSON context with `entity`, `type`, and `description`
  - builds relationship JSON context with `entity1`, `entity2`, `keywords`, and `description`
  - builds chunk JSON context with `reference_id` and `content`
  - builds reference list with matching `[reference_id] file_path`
  - omits empty data sections without producing an empty all-context result
- `RetrievalContextServiceRawDataTests`
  - KG context chunk `reference_id` values match raw data chunks and references
  - `OnlyNeedContext` returns the same structured reference ids that raw data reports
- `TokenBudgetPlannerTests` or new builder-budget tests
  - entity/relation token limiting uses JSON context shape, not legacy text lines
  - chunk token limiting accounts for reference list overhead
  - zero or negative available chunk budget returns no chunks without throwing

Verification should include:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~KgQueryContextBuilder|FullyQualifiedName~RetrievalContext|FullyQualifiedName~TokenBudget" --verbosity minimal
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

## Migration and Compatibility

No database migration is required.

Public query APIs remain compatible:

- `LightRAG.QueryAsync` signature does not change.
- `QueryContextResult` shape does not change.
- Raw data keeps existing `data.entities`, `data.relationships`, `data.chunks`, `data.references`, and nested `metadata` fields.
- `QueryResult.ReferenceList` should continue to work because references remain in raw data.

The visible `OnlyNeedContext` output changes from legacy text lines to structured JSON sections. This is an intentional parity improvement and should be covered by tests.

## Out of Scope

- Rerank algorithm parity or provider fallback behavior.
- Query cache key changes.
- Cache management UI/API.
- Prompt perfect parity with every Python prompt template.
- Server/Web UI redesign.
- Real Qdrant/Neo4j integration tests.
- Embedding cache.
- Migration support for old context strings.

## Acceptance Criteria

- KG context uses structured JSON entries for entities, relationships, and chunks.
- KG chunks use `reference_id`; context, raw data chunks, and raw data references agree on the same ids.
- Reference list uses `[reference_id] file_path`.
- Token budget tests prove estimates use the final structured context shape.
- `RetrievalContextService` delegates formatting work to a focused builder or equivalent small component.
- No-context behavior remains unchanged for empty retrieval results.
- Existing query cache, indexing cache, deletion, Naive, Bypass, and vector chunk selection tests remain green.
