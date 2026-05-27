# API Retrieval Evaluation Smoke Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic Server API smoke suite that drives `/api/RagQuery/data` from the existing retrieval JSON oracle.

**Architecture:** Link the core evaluation JSON files into `LightRAGNet.Server.Tests`, add a small server-only loader, and seed explicit in-memory doubles through `LightRagServerFactory`. The tests exercise the real ASP.NET endpoint and retrieval services while replacing all real external stores and model providers.

**Tech Stack:** .NET 10, ASP.NET Core `WebApplicationFactory`, xUnit, FluentAssertions, `System.Text.Json`, existing LightRAGNet Server test infrastructure.

---

## File Structure

- Modify: `tests/LightRAGNet.Server.Tests/LightRAGNet.Server.Tests.csproj`
  - Link `..\LightRAGNet.Tests\Evaluation\Data\**\*.*` into Server test output.
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationDataLoader.cs`
  - Server-only JSON loader and runtime records for API smoke tests.
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationTestDoubles.cs`
  - In-memory vector, graph, KV, tokenizer, embedding, rerank, and LLM test doubles.
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationSmokeTests.cs`
  - POSTs JSON oracle cases to `/api/RagQuery/data` and checks structured response.

Do not modify production code.

## Task 1: Link Evaluation Data Into Server Tests

**Files:**

- Modify: `tests/LightRAGNet.Server.Tests/LightRAGNet.Server.Tests.csproj`
- Test: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationSmokeTests.cs`

- [ ] **Step 1: Add the linked data copy rule**

Add this `ItemGroup` before `</Project>`:

```xml
  <ItemGroup>
    <None Include="..\LightRAGNet.Tests\Evaluation\Data\**\*.*"
          Link="Evaluation\Data\%(RecursiveDir)%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Verify the project still builds**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagQueryControllerTests" --no-restore --verbosity minimal
```

Expected: existing RAG query controller tests pass.

## Task 2: Add Server-Only Evaluation Loader

**Files:**

- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationDataLoader.cs`

- [ ] **Step 1: Write the loader with validation**

The loader must:

- read UTF-8 JSON from `AppContext.BaseDirectory/Evaluation/Data`,
- parse corpus chunks, entities, relationships, and cases,
- expose cases by name,
- validate referenced expected chunks, entities, relationships, and references.

- [ ] **Step 2: Run a targeted compile check**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests" --no-restore --verbosity minimal
```

Expected before Task 3: build fails because the smoke test class does not exist or has no tests.

## Task 3: Add In-Memory API Test Doubles

**Files:**

- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationTestDoubles.cs`

- [ ] **Step 1: Add doubles and seeding helper**

The seeding helper should register:

- `IVectorStore` with seeded `chunks`, `entities`, and `relationships` collections,
- `IGraphStore` with seeded graph nodes and edges,
- keyed `IKVStore` instances for every `KVContracts.GetKVStoreNames()` value, with `text_chunks` seeded,
- fake `IEmbeddingService`, `IRerankService`, `ILLMService`, and `ITokenizer`,
- `LightRAGOptions.KgChunkPickMethod = "WEIGHT"` for deterministic KG related chunk selection.

- [ ] **Step 2: Verify compile**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests" --no-restore --verbosity minimal
```

Expected before Task 4: build fails because smoke tests are not implemented.

## Task 4: Add API Smoke Tests

**Files:**

- Create: `tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationSmokeTests.cs`

- [ ] **Step 1: Write the failing API smoke tests**

Add tests for:

- `Naive_ReturnsExpectedArchitectureChunk`
- `Local_UsesLowLevelEntityFocus`

Each test should POST `/api/RagQuery/data` and assert:

- HTTP 200,
- `Status == "success"`,
- `Message == "Retrieval data returned."`,
- `Metadata["query_mode"]` matches the case mode,
- expected chunks are present,
- expected reference file paths are present,
- expected entities and relationship pairs are present.

- [ ] **Step 2: Run tests and confirm RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests" --no-restore --verbosity minimal
```

Expected: tests fail because API smoke seeding or assertions are not wired correctly yet.

- [ ] **Step 3: Implement minimal fixes**

Fix the test doubles or loader only. Do not change production code unless the failing test exposes a real production bug.

- [ ] **Step 4: Run tests and confirm GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests" --no-restore --verbosity minimal
```

Expected: API smoke tests pass.

## Task 5: Verification and Close-Out

**Files:**

- All changed docs and Server test files.

- [ ] **Step 1: Run focused RAG query Server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests|FullyQualifiedName~RagQueryControllerTests|FullyQualifiedName~RagQueryRequestMapperTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 2: Run full Server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 3: Inspect changed file boundary**

Run:

```powershell
git diff --name-only
```

Expected changes are limited to:

```text
docs/superpowers/specs/2026-05-27-api-retrieval-evaluation-smoke-design.md
docs/superpowers/plans/2026-05-27-api-retrieval-evaluation-smoke-implementation-plan.md
tests/LightRAGNet.Server.Tests/LightRAGNet.Server.Tests.csproj
tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationDataLoader.cs
tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationSmokeTests.cs
tests/LightRAGNet.Server.Tests/Evaluation/ApiRetrievalEvaluationTestDoubles.cs
```

## Plan Self-Review

- Spec coverage:
  - Shared JSON data linkage: Task 1.
  - Server-only loader: Task 2.
  - External dependency isolation: Task 3.
  - `/api/RagQuery/data` smoke assertions: Task 4.
  - Verification: Task 5.
- Placeholder scan:
  - No `TBD`, `TODO`, or unspecified implementation placeholders.
- Type consistency:
  - `ApiRetrievalEvaluationDataLoader`, `ApiRetrievalEvaluationTestDoubles`, and `ApiRetrievalEvaluationSmokeTests` are introduced before use.
