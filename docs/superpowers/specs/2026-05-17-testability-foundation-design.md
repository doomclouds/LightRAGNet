# LightRAGNet Testability Foundation Design

## Purpose

LightRAGNet needs a maintainable structure and a first layer of meaningful tests before more RAG features are added. The goal is to move the solution toward test-driven development by organizing production projects under `src/`, placing tests under `tests/`, and adding initial coverage around high-risk core logic.

This is not a feature rewrite. The first implementation should preserve current behavior, add characterization tests, and only introduce small seams where core logic cannot be tested cleanly in its current form.

## Scope

In scope:

- Move production projects into `src/`.
- Create `tests/LightRAGNet.Tests` for core library tests.
- Create `tests/LightRAGNet.Server.Tests` for server test host groundwork.
- Update `LightRAGNet.slnx` and all affected `ProjectReference` paths.
- Add tests for document chunking, retrieval context budget logic, chunk limiting, reference generation, source id limiting, and task queue behavior.
- Extract small pure logic components from large services when needed for testability.

Out of scope:

- UI automation for `LightRAGNet.Web`.
- Docker, Qdrant, Neo4j, or Testcontainers integration tests.
- Broad rewrites of retrieval, graph merge, queue, or storage behavior.
- Changes to the ignored Python `LightRAG/` reference implementation.

## Target Structure

```text
src/
  LightRAGNet.Core/
  LightRAGNet/
  LightRAGNet.Hosting/
  LightRAGNet.LLM/
  LightRAGNet.Embedding/
  LightRAGNet.Rerank/
  LightRAGNet.Storage/
  LightRAGNet.Server/
  LightRAGNet.Web/
  LightRAGNet.Share/
  LightRAGNet.Example/

tests/
  LightRAGNet.Tests/
  LightRAGNet.Server.Tests/
```

Root-level operational files such as `docker-compose.yml`, `README*.md`, `AGENTS.md`, and solution files remain at the repository root.

## Test Stack

Use a small, conventional .NET test stack:

- `xUnit` as the test framework.
- `FluentAssertions` for readable assertions.
- `NSubstitute` for mocks and fakes.
- `Microsoft.NET.Test.Sdk` and `coverlet.collector` for test execution and coverage collection.
- `Microsoft.AspNetCore.Mvc.Testing` only in `LightRAGNet.Server.Tests`.

Tests should avoid real LLM, embedding, Qdrant, Neo4j, or Docker dependencies in this phase.

## First Coverage Targets

### Document Processing

Cover `DocumentProcessingService.ChunkDocument` behavior:

- leading and trailing whitespace is trimmed before tokenization;
- sliding token windows honor chunk size and overlap;
- small trailing fragments merge into the previous chunk when appropriate;
- `splitByCharacter` splits content as expected;
- `splitByCharacterOnly=true` throws when a segment exceeds the configured token limit;
- cache hit processing rewrites entity and relationship source metadata to the current chunk.

Use fake tokenizer, fake key-value store, fake LLM service, and fake embedding service.

### Retrieval Context

`RetrievalContextService` is large and contains private logic that should not be tested through brittle reflection. Extract small pure components only where needed:

- `TokenBudgetPlanner` calculates available chunk budget after system prompt, query, knowledge graph context, reserved output tokens, and safety buffer.
- `ChunkTokenLimiter` truncates chunks by accumulated token count while preserving order.
- `ReferenceListBuilder` assigns reference ids and de-duplicates file paths for retrieved chunks.

The service should call these components, and tests should cover the components directly plus limited service-level behavior through fakes.

### Knowledge Graph Merge

Start with low-dependency logic:

- `SourceIdsLimiter.ApplyLimit`;
- `SourceIdsLimiter.ComputeTruncationInfo`;
- `DescriptionMerger` paths that do and do not require LLM summarization.

Entity and relation merge stages can receive focused boundary tests, but full graph merge integration is deferred.

### Task Queue

Cover `RagTaskQueueService` with an in-memory fake state store:

- enqueue creates a pending task;
- `GetNextTaskAsync` returns tasks by priority and eligible status;
- status updates publish state changes;
- failed tasks can be retried;
- processing tasks can be stopped;
- clearing tasks removes queued state.

## Refactoring Rules

- Preserve current behavior unless a test exposes a clear bug.
- Prefer characterization tests before changing logic.
- Extract pure logic components before adding complex service-level mocks.
- Do not widen public APIs only for convenience; use internal components and `InternalsVisibleTo` if needed.
- Keep each change reviewable and commit in small slices.

## Verification

The implementation is complete only when these commands pass:

```powershell
dotnet restore .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
dotnet test .\LightRAGNet.slnx
```

The final report should summarize the structural migration, tests added, verification output, and deferred second-phase coverage such as storage integration or API contract tests.

