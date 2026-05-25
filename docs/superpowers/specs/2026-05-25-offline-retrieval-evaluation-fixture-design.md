# Offline Retrieval Evaluation Fixture Design

- Date: `2026-05-25`
- Topic slug: `offline-retrieval-evaluation-fixture`
- Status: `Ready for review`
- Scope: `Core tests + evaluation fixtures`
- Tags: `lightrag-alignment`, `evaluation`, `retrieval-oracle`, `testability`, `python-parity`

## Purpose

LightRAGNet has already aligned the core RAG path with Python LightRAG across query modes, retrieval context, references, rerank chunking, query data, cache, document lifecycle, and deletion. The next high-value independent step is to add a deterministic evaluation fixture that guards retrieval quality while another agent continues frontend React work.

This phase creates a .NET-native offline retrieval evaluation baseline. It should catch regressions in query routing, retrieval context construction, rerank integration, references, and raw data contracts without requiring Docker, external vector stores, real LLM calls, or frontend changes.

## Python Reference Semantics

Python LightRAG's evaluation story has three useful layers:

- Offline sample retrieval check:
  - `LightRAG/lightrag/evaluation/sample_documents/`
  - `LightRAG/lightrag/evaluation/sample_dataset.json`
  - `LightRAG/lightrag/evaluation/sample_retrieval_oracle.json`
  - `LightRAG/lightrag/evaluation/offline_retrieval_check.py`
- API/RAGAS quality evaluation:
  - `LightRAG/lightrag/evaluation/eval_rag_quality.py`
  - evaluates faithfulness, answer relevance, context recall, and context precision through a running API server and external evaluator models.
- Paper reproduction scripts:
  - `LightRAG/reproduce/Step_0.py` through `Step_3.py`
  - `LightRAG/reproduce/batch_eval.py`
  - oriented toward research comparison and LLM-judged answer quality.

LightRAGNet should align with this layered model, but the first .NET slice should intentionally target only the offline retrieval oracle layer.

## Current .NET Gap

LightRAGNet has strong component tests for:

- `NaiveQueryService`
- `RetrievalContextService`
- `KgQueryContextBuilder`
- `ReferenceListBuilder`
- `RerankCoordinator`
- `QueryResult.ReferenceList`
- cache, deletion, and document lifecycle behavior

The gap is that these tests are mostly component-level. There is no compact corpus of fixed documents and fixed user questions that verifies end-to-end retrieval behavior across query modes. A future change can keep unit tests green while still degrading whether the right chunk, reference, entity, or relationship appears for a realistic query.

## Product Decision

Build an offline retrieval evaluation fixture as test infrastructure, not as a product UI or API feature.

The first version should:

- live under `tests/LightRAGNet.Tests/Evaluation/`
- run inside normal `dotnet test LightRAGNet.slnx`
- use deterministic in-memory stores and fakes
- evaluate retrieval outputs, not final LLM prose
- avoid `LightRAGNet.Web`, React assets, browser tests, or public API changes
- provide a small, extensible case model that future parity work can grow

## Architecture

Add a focused evaluation test area:

```text
tests/LightRAGNet.Tests/Evaluation/
  RetrievalEvaluationCase.cs
  RetrievalEvaluationCorpus.cs
  RetrievalEvaluationFixture.cs
  RetrievalEvaluationRunner.cs
  OfflineRetrievalEvaluationTests.cs
```

The fixture should reuse existing production retrieval components where possible:

```text
Naive cases:
  RetrievalEvaluationCase
    -> RetrievalEvaluationFixture
    -> NaiveQueryService
    -> raw data / references assertions

KG cases:
  RetrievalEvaluationCase
    -> RetrievalEvaluationFixture
    -> RetrievalContextService
    -> KgQueryContextBuilder
    -> raw data / references assertions
```

The fixture can seed existing test doubles:

- `InMemoryVectorStore`
- `InMemoryGraphStore`
- in-memory KV/test stores already used by retrieval, graph, and lifecycle tests
- fake tokenizer / fake embedding service / fake reranker where deterministic vectors or rank order are required

Do not introduce a new production service unless the tests expose a real boundary that should be reusable outside evaluation.

## Corpus Contract

The first corpus should be deliberately small and artificial. It should describe a fictional technical product so expected references are unambiguous.

Each document should have:

- stable document id
- stable file path
- one to three chunk ids
- chunk content
- optional entities
- optional relationships
- optional deterministic vector

Example document themes:

- overview document: core product purpose and hallucination mitigation
- architecture document: retrieval, embedding, and generation components
- operations document: deployment, cache, and health checks
- storage document: vector and graph backend choices
- evaluation document: quality metrics and release checks

The corpus should be expressed in C# fixture builders for the first implementation. JSON files can be introduced later if the cases become large enough to benefit from external data files.

## Case Contract

Each evaluation case should define:

- `Name`
- `Query`
- `Mode`
- optional manual high-level keywords
- optional manual low-level keywords
- `TopK`
- `ChunkTopK`
- expected chunk ids
- expected reference file paths
- expected entity ids
- expected relationship pairs
- optional forbidden chunk ids

The runner should expose small assertion helpers:

- expected chunks are present in final raw data chunks
- expected references are present and stable
- expected entities are present for KG modes
- expected relationships are present for KG modes
- forbidden chunks are absent when specified
- raw data metadata includes query mode and processing information

Do not assert exact complete ordering except in cases that are explicitly about rerank or top-k truncation. Most evaluation cases should prefer presence and absence checks over brittle full-list equality.

## First Case Set

Start with five cases:

1. `Naive_ReturnsExpectedArchitectureChunk`
   - Mode: `Naive`
   - Asserts chunk vector retrieval returns the architecture chunk and its reference.

2. `Mix_ReturnsKgEntityAndRelatedChunk`
   - Mode: `Mix`
   - Asserts a query with low-level and high-level keywords returns a target entity plus the supporting chunk reference.

3. `Local_UsesLowLevelEntityFocus`
   - Mode: `Local`
   - Uses manual low-level keywords to avoid LLM keyword extraction.
   - Asserts entity-focused retrieval does not depend on global relationship-only matches.

4. `Global_UsesHighLevelRelationshipFocus`
   - Mode: `Global`
   - Uses manual high-level keywords.
   - Asserts relationship-focused retrieval returns the expected relationship and reference.

5. `Rerank_KeepsRelevantChunkInFinalContext`
   - Mode: `Naive` or `Mix`
   - Seeds extra distractor chunks.
   - Uses deterministic rerank scores.
   - Asserts the relevant chunk survives final top-k selection.

## Metrics

The first phase should not introduce a full scoring report. It should still define metric vocabulary so the fixture can grow:

- `recall@k`: expected items found in top-k retrieval output
- `precision@k`: returned top-k items that are expected
- `mrr`: reciprocal rank of the first expected item
- `context_recall`: expected references or chunks present in final context/raw data

Initial tests may compute these internally for diagnostics, but pass/fail should be based on explicit oracle assertions.

## Out of Scope

- RAGAS integration
- final answer quality scoring
- model-based judging
- calling real LLM, embedding, rerank, Qdrant, or Neo4j services
- frontend pages, React components, browser tests, or API UI changes
- persistent evaluation reports
- CSV/JSON export
- CI performance dashboards
- paper reproduction parity
- large benchmark datasets

## Testing Strategy

Use TDD for implementation.

Focused tests:

- evaluation case parsing/building if a case model is introduced
- corpus seeding creates expected chunks/entities/relationships/references
- each first-case oracle test fails before its retrieval fixture is wired correctly
- runner error messages identify missing chunks, references, entities, and relationships clearly

Regression command:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal
```

Full verification:

```powershell
dotnet test .\LightRAGNet.slnx --verbosity minimal
```

## Compatibility and Isolation

This phase must not change public API contracts, frontend routes, database schema, or production startup behavior.

It may add internal test helpers and reuse existing fake services. If a new helper is generally useful outside evaluation, keep it in `tests/LightRAGNet.Tests/TestDoubles/` instead of production code.

The fixture must not require environment variables, user secrets, external services, Docker, or running Server/Web projects.

## Acceptance Criteria

- A new offline retrieval evaluation test suite exists under `tests/LightRAGNet.Tests/Evaluation/`.
- The suite includes a small deterministic corpus and at least five oracle cases.
- Cases cover `Naive`, `Local`, `Global`, and `Mix`.
- At least one case covers deterministic rerank survival.
- Tests assert chunks, references, and KG entities/relationships from raw retrieval data.
- The evaluation suite runs in normal `dotnet test` without Docker or model calls.
- No `LightRAGNet.Web`, React, browser, or generated frontend asset files are changed.
- Full solution tests remain green.

## Future Extensions

After the offline fixture is stable, later phases can add:

- JSON-backed datasets matching Python's `sample_dataset.json` / `sample_retrieval_oracle.json` shape.
- API-level `/api/RagQuery/data` evaluation smoke tests.
- optional report output for local diagnostics.
- answer-quality evaluation using an external evaluator model.
- RAGAS-compatible context export.
- a cross-runtime comparison harness that can run similar cases against Python LightRAG and LightRAGNet.
