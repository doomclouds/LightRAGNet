# Offline Retrieval JSON Dataset and Oracle Design

- Date: `2026-05-26`
- Topic slug: `offline-retrieval-json-dataset-oracle`
- Status: `Ready for review`
- Scope: `Core tests + evaluation data files`
- Tags: `evaluation`, `retrieval-oracle`, `json-dataset`, `python-parity`, `testability`

## Purpose

The existing offline retrieval evaluation fixture has proven the first layer: a deterministic in-memory corpus and seven raw-data oracle cases can protect `Naive`, `Local`, `Global`, `Mix`, keyword routing, references, KG entities, KG relationships, and rerank survival without Docker, real vector stores, real LLMs, or frontend changes.

The next phase should make that fixture data-driven. Today the corpus and oracle cases are embedded in C# builders and tests. That is good for the first cut, but it makes future evaluation growth noisy: adding one new question means editing test code, fixture constants, and expected assertions together. The Python LightRAG reference already separates sample questions, sample documents, and retrieval oracle JSON. LightRAGNet should follow that shape while preserving the finer raw-data assertions that the .NET fixture already has.

## Decision

Create a Python-compatible dataset layer plus a LightRAGNet-specific extended oracle layer.

Use Python sample evaluation files as the reference shape, not as the only source of truth:

- Python-compatible dataset: questions and ground truth answers.
- Python-compatible document oracle: question to expected document file names.
- LightRAGNet extended oracle: query mode, manual keywords, expected chunks, references, entities, relationships, forbidden chunks, rerank expectations, and optional vector/rerank score hints.
- Sample documents: markdown files copied or adapted from Python `LightRAG/lightrag/evaluation/sample_documents/`.

This phase does not call a real LLM. It keeps evaluation deterministic and local.

## Python Reference

Relevant Python files:

- `LightRAG/lightrag/evaluation/sample_dataset.json`
- `LightRAG/lightrag/evaluation/sample_retrieval_oracle.json`
- `LightRAG/lightrag/evaluation/sample_documents/*.md`
- `LightRAG/lightrag/evaluation/offline_retrieval_check.py`

Python's offline check is intentionally lightweight. It loads:

- `sample_dataset.json` with `test_cases[]`
- `sample_retrieval_oracle.json` with `oracle[]`
- markdown files from `sample_documents/`

Then it runs a deterministic lexical ranker and reports recall/MRR. It does not start LightRAG, call an API server, compute embeddings, or call LLM/RAGAS services.

LightRAGNet should preserve that offline spirit, but the current .NET fixture validates more detailed production retrieval contracts than the Python sample oracle can express.

## Proposed File Layout

Add data files under the test project:

```text
tests/LightRAGNet.Tests/Evaluation/Data/
  sample_dataset.json
  sample_retrieval_oracle.json
  lightragnet_retrieval_oracle.json
  sample_documents/
    01_lightrag_overview.md
    02_rag_architecture.md
    03_lightrag_improvements.md
    04_supported_databases.md
    05_evaluation_and_deployment.md
```

Keep the existing C# fixture types, but change their responsibility:

```text
RetrievalEvaluationCorpus
  before: owns hard-coded chunks/entities/relationships
  after: loads sample documents + extended corpus metadata from JSON-backed definitions

RetrievalEvaluationCase
  before: constructed inline in tests
  after: deserialized from lightragnet_retrieval_oracle.json

OfflineRetrievalEvaluationTests
  before: one test method per hard-coded case
  after: data-driven theory or loop over loaded cases, with small targeted tests for parser validation
```

## Dataset Contract

`sample_dataset.json` should stay close to Python:

```json
{
  "test_cases": [
    {
      "question": "What are the three main components required in a RAG system?",
      "ground_truth": "A RAG system requires three main components...",
      "project": "lightrag_evaluation_sample"
    }
  ]
}
```

Rules:

- `question` is the stable join key between dataset and oracle files.
- `ground_truth` is retained for future answer-quality evaluation, but this phase does not score final answers.
- `project` is retained for Python compatibility and future grouping.

## Python-Compatible Oracle Contract

`sample_retrieval_oracle.json` should stay close to Python:

```json
{
  "oracle": [
    {
      "question": "What are the three main components required in a RAG system?",
      "expected_documents": ["02_rag_architecture.md"]
    }
  ]
}
```

Rules:

- Every dataset question should have one document-level oracle entry.
- Document names are relative to `sample_documents/`.
- This file is used for compatibility checks and document-level assertions.
- It is not enough for the .NET raw-data fixture by itself.

## LightRAGNet Extended Oracle Contract

`lightragnet_retrieval_oracle.json` should express .NET raw-data expectations:

```json
{
  "cases": [
    {
      "name": "Local_UsesLowLevelEntityFocus",
      "question": "How does the retrieval system work?",
      "mode": "Local",
      "highLevelKeywords": [],
      "lowLevelKeywords": ["RETRIEVAL_SYSTEM"],
      "topK": 3,
      "chunkTopK": 2,
      "enableRerank": false,
      "expectedDocumentNames": ["02_rag_architecture.md"],
      "expectedChunkIds": ["chunk-architecture-rag-components"],
      "expectedReferenceFilePaths": ["docs/eval/02_rag_architecture.md"],
      "expectedEntityIds": ["RETRIEVAL_SYSTEM"],
      "expectedRelationshipPairs": [
        { "sourceId": "RETRIEVAL_SYSTEM", "targetId": "EMBEDDING_MODEL" }
      ],
      "forbiddenChunkIds": []
    }
  ]
}
```

Additional optional fields:

```json
{
  "expectedChunkOrder": ["chunk-operations-health-cache", "chunk-storage-vector-databases"],
  "vectorScoresByChunkId": {
    "chunk-storage-vector-databases": 0.9,
    "chunk-operations-health-cache": 0.7
  },
  "rerankScoresByContent": {
    "Operations include health checks...": 0.99
  }
}
```

Rules:

- `mode` maps to `QueryMode`.
- `question` should exist in `sample_dataset.json`.
- `expectedDocumentNames` should be a subset of Python-compatible `expected_documents` where the same question exists.
- `expectedReferenceFilePaths` can use LightRAGNet's current `docs/eval/...` path convention.
- `expectedChunkOrder` is optional and should only be used for top-k/rerank cases where exact order is the behavior under test.
- `vectorScoresByChunkId` and `rerankScoresByContent` are optional test-only hints for deterministic in-memory ranking.

## Corpus Metadata

Python sample documents are document-level markdown files. The current .NET fixture also needs chunk ids, entities, relationships, source ids, file paths, and deterministic vector entries. Do not hide that metadata in C# after this phase.

Add a compact corpus metadata section either inside `lightragnet_retrieval_oracle.json` or in a separate `lightragnet_corpus.json`. Use one file unless it gets unwieldy.

Recommended shape inside `lightragnet_retrieval_oracle.json`:

```json
{
  "corpus": {
    "chunks": [
      {
        "id": "chunk-architecture-rag-components",
        "documentName": "02_rag_architecture.md",
        "filePath": "docs/eval/02_rag_architecture.md",
        "contentSelector": "Main Components of RAG Systems"
      }
    ],
    "entities": [
      {
        "id": "RETRIEVAL_SYSTEM",
        "type": "Component",
        "description": "Retrieves relevant documents for a query.",
        "sourceId": "chunk-architecture-rag-components",
        "filePath": "docs/eval/02_rag_architecture.md"
      }
    ],
    "relationships": [
      {
        "sourceId": "RETRIEVAL_SYSTEM",
        "targetId": "EMBEDDING_MODEL",
        "keywords": "rag architecture",
        "description": "Retrieval systems depend on embedding models for vector search.",
        "weight": 3.0,
        "sourceIdList": "chunk-architecture-rag-components"
      }
    ]
  }
}
```

`contentSelector` is a convenience for humans, not a parser requirement. The implementation can either:

- map chunk ids to the full markdown file content, or
- map chunk ids to explicit `content` fields copied from the current C# fixture.

For this phase, prefer explicit `content` fields if selector parsing would add avoidable fragility.

## Loader Design

Add test-only JSON loader classes under `tests/LightRAGNet.Tests/Evaluation/`:

```text
RetrievalEvaluationDataSet.cs
RetrievalEvaluationDataLoader.cs
RetrievalEvaluationJsonModels.cs
```

Responsibilities:

- Read files with UTF-8.
- Deserialize with `System.Text.Json` using case-insensitive property matching or explicit attributes.
- Validate required fields with clear error messages.
- Join dataset questions, document oracle entries, and extended oracle cases.
- Produce the existing `RetrievalEvaluationCase` records plus corpus seed specs.
- Fail fast when:
  - a case references an unknown question,
  - a document oracle is missing,
  - an expected document name has no markdown file,
  - a chunk references an unknown document,
  - relationship endpoints reference unknown entities.

Do not add production code for this loader. It belongs to tests.

## Test Flow

The final evaluation flow should look like this:

```text
OfflineRetrievalEvaluationTests
  -> RetrievalEvaluationDataLoader.LoadDefault()
  -> RetrievalEvaluationFixture.CreateAsync(dataSet)
  -> foreach case in dataSet.Cases
       fixture.RunAsync(case)
       RetrievalEvaluationRunner.AssertCase(result, case)
```

Keep a few focused parser tests:

- valid JSON files load all expected case names.
- every extended oracle question exists in `sample_dataset.json`.
- every Python-compatible oracle question exists in `sample_dataset.json`.
- unknown document references fail with a helpful message.

Keep the current runner semantics:

- presence assertions for chunks, references, entities, and relationships.
- forbidden chunk assertions.
- metadata assertions.
- exact chunk order only for explicit `expectedChunkOrder`.
- relationship matching stays direction-insensitive unless a later case needs directed matching.

## Real LLM Boundary

This phase must not use real LLM, embedding, rerank, Qdrant, Neo4j, Server, Web, browser, or API calls.

Why:

- The purpose is stable retrieval contract regression.
- Real LLM keyword extraction and final answer scoring are slower, cost-bearing, and nondeterministic.
- The JSON dataset keeps `ground_truth` so a later answer-quality phase can reuse it.

Future LLM-related phases can build on this:

- LLM keyword extraction evaluation.
- API-level `/api/RagQuery/data` evaluation smoke.
- RAGAS or evaluator-model answer quality.
- Cross-runtime comparison against Python LightRAG.

## Migration Plan

Do this as a compatibility-preserving refactor:

1. Add JSON data files that encode the current seven .NET oracle cases and Python-compatible sample shape.
2. Add loader tests.
3. Change `RetrievalEvaluationCorpus` to seed from loaded corpus data.
4. Change `OfflineRetrievalEvaluationTests` to enumerate loaded cases.
5. Keep current test names visible through case names in assertion output.
6. Remove duplicated C# inline case construction after JSON-driven tests pass.

No production behavior should change.

## Alternatives Considered

### Option A: Copy Python JSON exactly and only test expected documents

Pros:

- Fastest route to Python compatibility.
- Very small implementation.

Cons:

- Loses .NET raw-data coverage for chunks, entities, relationships, modes, and rerank.
- Regresses the value of yesterday's fixture.

Decision: reject.

### Option B: Keep all data in C#

Pros:

- Strong typing and simple debugging.
- Current tests already pass.

Cons:

- Every new evaluation case requires code edits.
- Harder to compare with Python LightRAG samples.
- Harder to generate reports or let humans review test coverage.

Decision: reject.

### Option C: Python-compatible plus LightRAGNet extended oracle

Pros:

- Keeps Python alignment.
- Preserves .NET raw-data contract coverage.
- Makes future case growth data-driven.
- Keeps real LLM evaluation out of the deterministic test layer.

Cons:

- Slightly more loader validation code.
- Two oracle layers must be kept consistent.

Decision: choose this.

## Acceptance Criteria

- Evaluation data files exist under `tests/LightRAGNet.Tests/Evaluation/Data/`.
- Python-compatible `sample_dataset.json` and `sample_retrieval_oracle.json` can be loaded and validated.
- LightRAGNet extended oracle can express all current seven evaluation cases.
- Current C# inline corpus/case definitions are replaced by JSON-driven loading.
- Existing `Evaluation` tests still pass.
- No production code, Server controllers, Web/React files, frontend assets, database migrations, or public API DTOs change.
- The full solution test suite remains green.

## Out of Scope

- Real LLM validation.
- RAGAS.
- Final answer quality scoring.
- API-level evaluation.
- Browser/UI work.
- CI dashboard or persistent HTML/CSV reports.
- Cross-runtime execution against Python LightRAG.
- Large benchmark datasets.
