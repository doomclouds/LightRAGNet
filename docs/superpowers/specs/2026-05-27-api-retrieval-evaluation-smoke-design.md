# API Retrieval Evaluation Smoke Design

- Date: `2026-05-27`
- Topic slug: `api-retrieval-evaluation-smoke`
- Status: `Ready for implementation`
- Scope: `Server tests + evaluation data linkage`
- Tags: `evaluation`, `api-smoke`, `retrieval-oracle`, `python-parity`, `testability`

## Purpose

The 2026-05-26 offline retrieval JSON oracle proved that LightRAGNet can validate retrieval contracts from Python-compatible sample data without real LLMs, Qdrant, Neo4j, Server, or Web. The next useful layer is to run the same oracle shape through the ASP.NET Core `/api/RagQuery/data` boundary.

This catches regressions that pure core tests cannot see:

- request mapping from `RagQueryRequest` to `QueryParam`,
- `/api/RagQuery/data` forcing non-streaming retrieval-data mode,
- JSON serialization of structured `data` and `metadata`,
- server dependency injection staying isolated from real external stores.

## Decision

Add a narrow Server test smoke suite that links the existing evaluation JSON data from `tests/LightRAGNet.Tests/Evaluation/Data/` and seeds in-memory API test doubles from that data.

The smoke suite should POST selected oracle cases to `/api/RagQuery/data` and assert the response body against the JSON oracle. It should use real ASP.NET routing, model binding, controller logic, `LightRAG.QueryAsync`, `NaiveQueryService`, and `RetrievalContextService`, but fake all cost-bearing and external dependencies.

## Boundary

In scope:

- Server test project copies/links the existing evaluation data files.
- Server-only loader reads enough of `sample_dataset.json`, `sample_retrieval_oracle.json`, and `lightragnet_retrieval_oracle.json` to seed API smoke tests.
- API smoke tests cover at least one `Naive` case and one KG case (`Local`, `Global`, or `Mix`).
- Test doubles replace vector store, graph store, keyed KV stores, embedding, rerank, tokenizer, and LLM dependencies.
- Assertions check `status`, `message`, `metadata.query_mode`, chunks, references, entities, and relationships.

Out of scope:

- RAGAS or answer-quality scoring.
- Real LLM, embedding, rerank, Qdrant, Neo4j, Web, React, browser, or Docker.
- Production API changes.
- New user-facing reports or dashboards.
- Duplicating the full core evaluation loader in Server tests.

## Architecture

Use data linkage instead of copying JSON:

```text
tests/LightRAGNet.Server.Tests/
  LightRAGNet.Server.Tests.csproj
    links ../LightRAGNet.Tests/Evaluation/Data/**/* to Evaluation/Data/
  Evaluation/
    ApiRetrievalEvaluationSmokeTests.cs
    ApiRetrievalEvaluationDataLoader.cs
    ApiRetrievalEvaluationTestDoubles.cs
```

The Server loader intentionally stays smaller than the core loader. Its job is only to:

- load the current JSON files from test output,
- expose corpus chunks/entities/relationships,
- expose API smoke cases,
- validate that referenced chunks and expected fields exist.

The test doubles should be explicit and local to `LightRAGNet.Server.Tests` so the Server tests do not depend on another test assembly.

## Test Flow

1. `ApiRetrievalEvaluationDataLoader.LoadDefault()` loads linked JSON data.
2. `ApiRetrievalEvaluationTestDoubles.Create(dataSet)` creates seeded in-memory stores and fake model services.
3. `LightRagServerFactory` replaces default external-storage guards with those seeded doubles.
4. The test POSTs `/api/RagQuery/data` with the case question, mode, keywords, `topK`, `chunkTopK`, and `enableRerank`.
5. The response is asserted against the oracle.

## Acceptance Criteria

- `tests/LightRAGNet.Server.Tests` can load the shared evaluation JSON data from test output.
- API smoke tests pass without real external stores or model calls.
- The tests exercise at least one Naive response and one KG response through `/api/RagQuery/data`.
- The response assertions check chunks, references, entities, relationships, and metadata.
- Existing `RagQueryControllerTests` still pass.
- Focused Server RAG query tests pass.
- Full Server test project passes.
