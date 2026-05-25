# Offline Retrieval Evaluation Fixture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic .NET offline retrieval evaluation fixture that runs in `dotnet test` and guards chunks, references, entities, relationships, and rerank survival without touching frontend or public API contracts.

**Architecture:** Build a test-only evaluation layer under `tests/LightRAGNet.Tests/Evaluation/`. The fixture seeds a compact in-memory corpus into existing test doubles, runs production `NaiveQueryService` and `RetrievalContextService`, and asserts raw retrieval data against oracle cases.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NSubstitute, existing LightRAGNet test doubles (`InMemoryVectorStore`, `InMemoryGraphStore`, `InMemoryKvStore`, `FakeTokenizer`).

---

## File Structure

- Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCase.cs`
  - Defines the test-only oracle case model and expected relationship pair model.
- Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`
  - Seeds a deterministic corpus into in-memory vector, graph, and KV stores.
- Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
  - Owns seeded stores, fake services, production retrieval service construction, and query execution helpers.
- Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationRunner.cs`
  - Centralizes assertions against chunks, references, entities, relationships, forbidden chunks, and metadata.
- Create `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`
  - Contains the five initial oracle tests.
- Modify only if needed: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
  - Add optional score-aware query ordering if current insertion-order behavior blocks deterministic vector/rerank cases.

Do not modify:

- `src/LightRAGNet.Web/**`
- generated frontend assets
- `src/LightRAGNet.Server/**`
- public API DTOs/controllers
- production storage/provider code

## Task 1: Add Evaluation Case Model

**Files:**
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCase.cs`

- [ ] **Step 1: Write the failing model usage test**

Create `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs` with this initial test only:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed class OfflineRetrievalEvaluationTests
{
    [Fact]
    public void RetrievalEvaluationCase_CapturesExpectedOracleFields()
    {
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Naive_ReturnsExpectedArchitectureChunk",
            Query: "Which components are required in a RAG system?",
            Mode: QueryMode.Naive,
            HighLevelKeywords: [],
            LowLevelKeywords: [],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: ["docs/eval/02-rag-architecture.md"],
            ExpectedEntityIds: [],
            ExpectedRelationshipPairs: [],
            ForbiddenChunkIds: ["chunk-storage-vector-databases"],
            EnableRerank: false);

        evaluationCase.Name.Should().Be("Naive_ReturnsExpectedArchitectureChunk");
        evaluationCase.Mode.Should().Be(QueryMode.Naive);
        evaluationCase.ExpectedChunkIds.Should().ContainSingle("chunk-architecture-rag-components");
        evaluationCase.ForbiddenChunkIds.Should().ContainSingle("chunk-storage-vector-databases");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCase_CapturesExpectedOracleFields" --verbosity minimal
```

Expected: build fails because `RetrievalEvaluationCase` does not exist.

- [ ] **Step 3: Implement the minimal case model**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCase.cs`:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed record RetrievalEvaluationCase(
    string Name,
    string Query,
    QueryMode Mode,
    IReadOnlyList<string> HighLevelKeywords,
    IReadOnlyList<string> LowLevelKeywords,
    int TopK,
    int ChunkTopK,
    IReadOnlyList<string> ExpectedChunkIds,
    IReadOnlyList<string> ExpectedReferenceFilePaths,
    IReadOnlyList<string> ExpectedEntityIds,
    IReadOnlyList<ExpectedRelationshipPair> ExpectedRelationshipPairs,
    IReadOnlyList<string> ForbiddenChunkIds,
    bool EnableRerank);

public sealed record ExpectedRelationshipPair(string SourceId, string TargetId);
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCase_CapturesExpectedOracleFields" --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationCase.cs tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs
git commit -m "test: add retrieval evaluation case model"
```

## Task 2: Add Deterministic Evaluation Corpus

**Files:**
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`
- Test: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`

- [ ] **Step 1: Write the failing corpus seeding test**

Append this test to `OfflineRetrievalEvaluationTests`:

```csharp
[Fact]
public async Task RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks()
{
    var fixture = await RetrievalEvaluationFixture.CreateAsync();

    fixture.VectorStore.Get("chunks", "chunk-architecture-rag-components")
        .Should()
        .NotBeNull();
    fixture.VectorStore.Get("chunks", "chunk-architecture-rag-components")!
        .Metadata["file_path"]
        .Should()
        .Be("docs/eval/02-rag-architecture.md");
    fixture.GraphStore.GetSeededNode("RETRIEVAL_SYSTEM")
        .Should()
        .NotBeNull();
    fixture.GraphStore.GetSeededEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")
        .Should()
        .NotBeNull();

    var architectureChunk = await fixture.TextChunks.GetByIdAsync(
        "chunk-architecture-rag-components",
        CancellationToken.None);
    architectureChunk.Should().NotBeNull();
    architectureChunk!["content"].Should().BeOfType<string>()
        .Which.Should().Contain("retrieval system");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks" --verbosity minimal
```

Expected: build fails because `RetrievalEvaluationFixture` and corpus seeding do not exist.

- [ ] **Step 3: Create the corpus seeder**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationCorpus
{
    public const string OverviewPath = "docs/eval/01-lightrag-overview.md";
    public const string ArchitecturePath = "docs/eval/02-rag-architecture.md";
    public const string OperationsPath = "docs/eval/03-operations.md";
    public const string StoragePath = "docs/eval/04-supported-storage.md";
    public const string EvaluationPath = "docs/eval/05-evaluation.md";

    public static async Task SeedAsync(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken = default)
    {
        SeedChunks(vectorStore);
        SeedGraph(graphStore);
        await SeedTextChunksAsync(textChunks, cancellationToken);
    }

    private static void SeedChunks(InMemoryVectorStore vectorStore)
    {
        SeedChunk(
            vectorStore,
            "chunk-overview-hallucination",
            OverviewPath,
            "LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references.");
        SeedChunk(
            vectorStore,
            "chunk-architecture-rag-components",
            ArchitecturePath,
            "A RAG system requires a retrieval system, an embedding model, and a generation model.");
        SeedChunk(
            vectorStore,
            "chunk-operations-health-cache",
            OperationsPath,
            "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.");
        SeedChunk(
            vectorStore,
            "chunk-storage-vector-databases",
            StoragePath,
            "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure.");
        SeedChunk(
            vectorStore,
            "chunk-evaluation-quality-metrics",
            EvaluationPath,
            "Evaluation tracks faithfulness, answer relevance, context recall, and context precision.");
    }

    private static void SeedChunk(
        InMemoryVectorStore vectorStore,
        string chunkId,
        string filePath,
        string content)
    {
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = chunkId,
            Content = content,
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["chunk_id"] = chunkId
            }
        });
    }

    private static void SeedGraph(InMemoryGraphStore graphStore)
    {
        graphStore.SeedNode("RETRIEVAL_SYSTEM", new Dictionary<string, object>
        {
            ["entity_id"] = "RETRIEVAL_SYSTEM",
            ["entity_type"] = "Component",
            ["description"] = "Retrieves relevant documents for a query.",
            ["source_id"] = "chunk-architecture-rag-components",
            ["file_path"] = ArchitecturePath
        });
        graphStore.SeedNode("EMBEDDING_MODEL", new Dictionary<string, object>
        {
            ["entity_id"] = "EMBEDDING_MODEL",
            ["entity_type"] = "Component",
            ["description"] = "Converts text into vectors for similarity retrieval.",
            ["source_id"] = "chunk-architecture-rag-components",
            ["file_path"] = ArchitecturePath
        });
        graphStore.SeedNode("CACHE_MANAGEMENT", new Dictionary<string, object>
        {
            ["entity_id"] = "CACHE_MANAGEMENT",
            ["entity_type"] = "Operation",
            ["description"] = "Manages cache visibility and safe maintenance.",
            ["source_id"] = "chunk-operations-health-cache",
            ["file_path"] = OperationsPath
        });

        graphStore.SeedEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL", new Dictionary<string, object>
        {
            ["keywords"] = "rag architecture",
            ["description"] = "Retrieval systems depend on embedding models for vector search.",
            ["weight"] = 3.0d,
            ["source_id"] = "chunk-architecture-rag-components"
        });
        graphStore.SeedEdge("CACHE_MANAGEMENT", "RETRIEVAL_SYSTEM", new Dictionary<string, object>
        {
            ["keywords"] = "operations retrieval",
            ["description"] = "Cache management protects retrieval operations during maintenance.",
            ["weight"] = 2.0d,
            ["source_id"] = "chunk-operations-health-cache"
        });
    }

    private static Task SeedTextChunksAsync(
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken)
    {
        return textChunks.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["chunk-overview-hallucination"] = Chunk("LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references.", OverviewPath),
            ["chunk-architecture-rag-components"] = Chunk("A RAG system requires a retrieval system, an embedding model, and a generation model.", ArchitecturePath),
            ["chunk-operations-health-cache"] = Chunk("Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.", OperationsPath),
            ["chunk-storage-vector-databases"] = Chunk("LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure.", StoragePath),
            ["chunk-evaluation-quality-metrics"] = Chunk("Evaluation tracks faithfulness, answer relevance, context recall, and context precision.", EvaluationPath)
        }, cancellationToken);
    }

    private static Dictionary<string, object> Chunk(string content, string filePath)
    {
        return new Dictionary<string, object>
        {
            ["content"] = content,
            ["file_path"] = filePath
        };
    }
}
```

- [ ] **Step 4: Create the minimal fixture shell**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`:

```csharp
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationFixture
{
    private RetrievalEvaluationFixture(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks)
    {
        VectorStore = vectorStore;
        GraphStore = graphStore;
        TextChunks = textChunks;
    }

    public InMemoryVectorStore VectorStore { get; }

    public InMemoryGraphStore GraphStore { get; }

    public InMemoryKvStore TextChunks { get; }

    public static async Task<RetrievalEvaluationFixture> CreateAsync()
    {
        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();

        await RetrievalEvaluationCorpus.SeedAsync(vectorStore, graphStore, textChunks);

        return new RetrievalEvaluationFixture(vectorStore, graphStore, textChunks);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks" --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationCorpus.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationFixture.cs tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs
git commit -m "test: seed offline retrieval evaluation corpus"
```

## Task 3: Add Naive Evaluation Runner

**Files:**
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationRunner.cs`
- Test: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`

- [ ] **Step 1: Write the failing Naive oracle test**

Append this test:

```csharp
[Fact]
public async Task Naive_ReturnsExpectedArchitectureChunk()
{
    var fixture = await RetrievalEvaluationFixture.CreateAsync();
    var evaluationCase = new RetrievalEvaluationCase(
        Name: "Naive_ReturnsExpectedArchitectureChunk",
        Query: "Which components are required in a RAG system?",
        Mode: QueryMode.Naive,
        HighLevelKeywords: [],
        LowLevelKeywords: [],
        TopK: 3,
        ChunkTopK: 2,
        ExpectedChunkIds: ["chunk-architecture-rag-components"],
        ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
        ExpectedEntityIds: [],
        ExpectedRelationshipPairs: [],
        ForbiddenChunkIds: [],
        EnableRerank: false);

    var result = await fixture.RunAsync(evaluationCase);

    RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Naive_ReturnsExpectedArchitectureChunk" --verbosity minimal
```

Expected: build fails because `RunAsync` and `RetrievalEvaluationRunner` do not exist.

- [ ] **Step 3: Add production Naive service construction to the fixture**

Replace `RetrievalEvaluationFixture.cs` with:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationFixture
{
    private readonly NaiveQueryService naiveQueryService;

    private RetrievalEvaluationFixture(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        NaiveQueryService naiveQueryService)
    {
        VectorStore = vectorStore;
        GraphStore = graphStore;
        TextChunks = textChunks;
        this.naiveQueryService = naiveQueryService;
    }

    public InMemoryVectorStore VectorStore { get; }

    public InMemoryGraphStore GraphStore { get; }

    public InMemoryKvStore TextChunks { get; }

    public static async Task<RetrievalEvaluationFixture> CreateAsync(
        IRerankService? rerankService = null)
    {
        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        var tokenizer = new FakeTokenizer();
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService ?? Substitute.For<IRerankService>(),
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);

        await RetrievalEvaluationCorpus.SeedAsync(vectorStore, graphStore, textChunks);

        return new RetrievalEvaluationFixture(
            vectorStore,
            graphStore,
            textChunks,
            new NaiveQueryService(vectorStore, rerankCoordinator, tokenizer));
    }

    public async Task<RetrievalEvaluationResult> RunAsync(RetrievalEvaluationCase evaluationCase)
    {
        var queryParam = new QueryParam
        {
            Mode = evaluationCase.Mode,
            TopK = evaluationCase.TopK,
            ChunkTopK = evaluationCase.ChunkTopK,
            HighLevelKeywords = [.. evaluationCase.HighLevelKeywords],
            LowLevelKeywords = [.. evaluationCase.LowLevelKeywords],
            EnableRerank = evaluationCase.EnableRerank
        };

        if (evaluationCase.Mode == QueryMode.Naive)
        {
            var result = await naiveQueryService.BuildContextAsync(
                evaluationCase.Query,
                queryParam,
                CancellationToken.None);

            return RetrievalEvaluationResult.FromRawData(result?.RawData);
        }

        throw new NotSupportedException($"Evaluation mode '{evaluationCase.Mode}' is not wired yet.");
    }
}

public sealed record RetrievalEvaluationResult(Dictionary<string, object>? RawData)
{
    public static RetrievalEvaluationResult FromRawData(Dictionary<string, object>? rawData)
    {
        return new RetrievalEvaluationResult(rawData);
    }
}
```

- [ ] **Step 4: Add raw data assertion runner**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationRunner.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationRunner
{
    public static void AssertCase(
        RetrievalEvaluationResult result,
        RetrievalEvaluationCase evaluationCase)
    {
        result.RawData.Should().NotBeNull($"{evaluationCase.Name} should produce raw retrieval data");
        var data = result.RawData!["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var metadata = result.RawData!["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;

        metadata["query_mode"].Should().Be(evaluationCase.Mode.ToString());
        metadata.Should().ContainKey("processing_info");

        var chunks = GetList(data, "chunks");
        var references = GetList(data, "references");
        var entities = GetList(data, "entities");
        var relationships = GetList(data, "relationships");

        foreach (var chunkId in evaluationCase.ExpectedChunkIds)
        {
            chunks.Should().Contain(
                chunk => ValueEquals(chunk, "chunk_id", chunkId),
                $"{evaluationCase.Name} should include expected chunk {chunkId}");
        }

        foreach (var chunkId in evaluationCase.ForbiddenChunkIds)
        {
            chunks.Should().NotContain(
                chunk => ValueEquals(chunk, "chunk_id", chunkId),
                $"{evaluationCase.Name} should not include forbidden chunk {chunkId}");
        }

        foreach (var filePath in evaluationCase.ExpectedReferenceFilePaths)
        {
            references.Should().Contain(
                reference => ValueEquals(reference, "file_path", filePath),
                $"{evaluationCase.Name} should include expected reference {filePath}");
        }

        foreach (var entityId in evaluationCase.ExpectedEntityIds)
        {
            entities.Should().Contain(
                entity => ValueEquals(entity, "entity_name", entityId),
                $"{evaluationCase.Name} should include expected entity {entityId}");
        }

        foreach (var pair in evaluationCase.ExpectedRelationshipPairs)
        {
            relationships.Should().Contain(
                relationship =>
                    ValueEquals(relationship, "src_id", pair.SourceId)
                    && ValueEquals(relationship, "tgt_id", pair.TargetId),
                $"{evaluationCase.Name} should include relationship {pair.SourceId}->{pair.TargetId}");
        }
    }

    private static List<Dictionary<string, object>> GetList(
        Dictionary<string, object> data,
        string key)
    {
        return data[key]
            .Should()
            .BeAssignableTo<IEnumerable<Dictionary<string, object>>>()
            .Subject
            .ToList();
    }

    private static bool ValueEquals(
        Dictionary<string, object> item,
        string key,
        string expected)
    {
        return item.TryGetValue(key, out var value)
               && string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Run the Naive oracle test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Naive_ReturnsExpectedArchitectureChunk" --verbosity minimal
```

Expected: pass. If it fails because `InMemoryVectorStore.QueryAsync` returns insertion order and the architecture chunk is outside `ChunkTopK`, change the corpus seeding order so `chunk-architecture-rag-components` is seeded before distractor chunks for this first test. Keep score-aware vector ranking for a later task only if needed.

- [ ] **Step 6: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationFixture.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationRunner.cs tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs
git commit -m "test: add naive retrieval evaluation oracle"
```

## Task 4: Add KG Local and Global Evaluation Cases

**Files:**
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
- Test: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`

- [ ] **Step 1: Write failing Local and Global oracle tests**

Append:

```csharp
[Fact]
public async Task Local_UsesLowLevelEntityFocus()
{
    var fixture = await RetrievalEvaluationFixture.CreateAsync();
    var evaluationCase = new RetrievalEvaluationCase(
        Name: "Local_UsesLowLevelEntityFocus",
        Query: "How does the retrieval system work?",
        Mode: QueryMode.Local,
        HighLevelKeywords: [],
        LowLevelKeywords: ["RETRIEVAL_SYSTEM"],
        TopK: 3,
        ChunkTopK: 2,
        ExpectedChunkIds: ["chunk-architecture-rag-components"],
        ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
        ExpectedEntityIds: ["RETRIEVAL_SYSTEM"],
        ExpectedRelationshipPairs: [],
        ForbiddenChunkIds: [],
        EnableRerank: false);

    var result = await fixture.RunAsync(evaluationCase);

    RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
}

[Fact]
public async Task Global_UsesHighLevelRelationshipFocus()
{
    var fixture = await RetrievalEvaluationFixture.CreateAsync();
    var evaluationCase = new RetrievalEvaluationCase(
        Name: "Global_UsesHighLevelRelationshipFocus",
        Query: "Which architecture relationship connects retrieval and embedding?",
        Mode: QueryMode.Global,
        HighLevelKeywords: ["rag architecture"],
        LowLevelKeywords: [],
        TopK: 3,
        ChunkTopK: 2,
        ExpectedChunkIds: ["chunk-architecture-rag-components"],
        ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
        ExpectedEntityIds: [],
        ExpectedRelationshipPairs: [new ExpectedRelationshipPair("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")],
        ForbiddenChunkIds: [],
        EnableRerank: false);

    var result = await fixture.RunAsync(evaluationCase);

    RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Local_UsesLowLevelEntityFocus|FullyQualifiedName~Global_UsesHighLevelRelationshipFocus" --verbosity minimal
```

Expected: fail with `Evaluation mode 'Local' is not wired yet` or `Evaluation mode 'Global' is not wired yet`.

- [ ] **Step 3: Wire RetrievalContextService in fixture**

Update `RetrievalEvaluationFixture.cs` by adding these usings:

```csharp
using LightRAGNet.Services.RetrievalContext;
using Microsoft.Extensions.Logging.Abstractions;
```

Add a field:

```csharp
private readonly RetrievalContextService retrievalContextService;
```

Update the private constructor signature and assignment:

```csharp
private RetrievalEvaluationFixture(
    InMemoryVectorStore vectorStore,
    InMemoryGraphStore graphStore,
    InMemoryKvStore textChunks,
    NaiveQueryService naiveQueryService,
    RetrievalContextService retrievalContextService)
{
    VectorStore = vectorStore;
    GraphStore = graphStore;
    TextChunks = textChunks;
    this.naiveQueryService = naiveQueryService;
    this.retrievalContextService = retrievalContextService;
}
```

In `CreateAsync`, add an embedding service and construct the retrieval service:

```csharp
var embeddingService = Substitute.For<IEmbeddingService>();
embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
    .Returns([0.1f, 0.2f, 0.3f]);

var retrievalContextService = new RetrievalContextService(
    embeddingService,
    vectorStore,
    graphStore,
    rerankCoordinator,
    tokenizer,
    textChunks,
    Options.Create(new LightRAGOptions { KgChunkPickMethod = "WEIGHT" }),
    NullLoggerFactory.Instance);
```

Update the return statement:

```csharp
return new RetrievalEvaluationFixture(
    vectorStore,
    graphStore,
    textChunks,
    new NaiveQueryService(vectorStore, rerankCoordinator, tokenizer),
    retrievalContextService);
```

Update `RunAsync` after the Naive branch:

```csharp
var keywords = new KeywordsResult
{
    HighLevelKeywords = [.. evaluationCase.HighLevelKeywords],
    LowLevelKeywords = [.. evaluationCase.LowLevelKeywords]
};

var contextResult = await retrievalContextService.BuildQueryContextAsync(
    evaluationCase.Query,
    keywords,
    queryParam,
    CancellationToken.None);

return RetrievalEvaluationResult.FromRawData(contextResult?.RawData);
```

- [ ] **Step 4: Run Local and Global tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Local_UsesLowLevelEntityFocus|FullyQualifiedName~Global_UsesHighLevelRelationshipFocus" --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationFixture.cs tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs
git commit -m "test: add kg retrieval evaluation oracles"
```

## Task 5: Add Mix and Rerank Survival Evaluation Cases

**Files:**
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
- Modify if needed: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
- Test: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`

- [ ] **Step 1: Write failing Mix oracle test**

Append:

```csharp
[Fact]
public async Task Mix_ReturnsKgEntityRelationshipAndRelatedChunk()
{
    var fixture = await RetrievalEvaluationFixture.CreateAsync();
    var evaluationCase = new RetrievalEvaluationCase(
        Name: "Mix_ReturnsKgEntityRelationshipAndRelatedChunk",
        Query: "How do retrieval and embedding work together in RAG architecture?",
        Mode: QueryMode.Mix,
        HighLevelKeywords: ["rag architecture"],
        LowLevelKeywords: ["RETRIEVAL_SYSTEM"],
        TopK: 3,
        ChunkTopK: 2,
        ExpectedChunkIds: ["chunk-architecture-rag-components"],
        ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
        ExpectedEntityIds: ["RETRIEVAL_SYSTEM"],
        ExpectedRelationshipPairs: [new ExpectedRelationshipPair("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")],
        ForbiddenChunkIds: [],
        EnableRerank: false);

    var result = await fixture.RunAsync(evaluationCase);

    RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
}
```

- [ ] **Step 2: Write failing rerank survival test**

Append:

```csharp
[Fact]
public async Task Rerank_KeepsRelevantChunkInFinalContext()
{
    var rerankService = new DeterministicEvaluationRerankService(new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["Operations include health checks, cache management, deployment readiness, and safe maintenance workflows."] = 0.99f,
        ["LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure."] = 0.10f,
        ["Evaluation tracks faithfulness, answer relevance, context recall, and context precision."] = 0.05f
    });
    var fixture = await RetrievalEvaluationFixture.CreateAsync(rerankService);
    var evaluationCase = new RetrievalEvaluationCase(
        Name: "Rerank_KeepsRelevantChunkInFinalContext",
        Query: "Which operational workflow covers cache and health checks?",
        Mode: QueryMode.Naive,
        HighLevelKeywords: [],
        LowLevelKeywords: [],
        TopK: 5,
        ChunkTopK: 3,
        ExpectedChunkIds: ["chunk-operations-health-cache"],
        ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.OperationsPath],
        ExpectedEntityIds: [],
        ExpectedRelationshipPairs: [],
        ForbiddenChunkIds: ["chunk-evaluation-quality-metrics"],
        EnableRerank: true);

    var result = await fixture.RunAsync(evaluationCase);

    RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
}
```

Add the helper class at the bottom of `OfflineRetrievalEvaluationTests.cs`:

```csharp
private sealed class DeterministicEvaluationRerankService(
    IReadOnlyDictionary<string, float> scoresByDocument) : IRerankService
{
    public Task<List<RerankResult>> RerankAsync(
        string query,
        List<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        var results = documents
            .Select((document, index) => new RerankResult
            {
                Index = index,
                RelevanceScore = scoresByDocument.TryGetValue(document, out var score) ? score : 0.0f
            })
            .OrderByDescending(result => result.RelevanceScore)
            .Take(topN)
            .ToList();

        return Task.FromResult(results);
    }
}
```

Add required usings:

```csharp
using LightRAGNet.Core.Interfaces;
```

- [ ] **Step 3: Run tests to verify failures**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Mix_ReturnsKgEntityRelationshipAndRelatedChunk|FullyQualifiedName~Rerank_KeepsRelevantChunkInFinalContext" --verbosity minimal
```

Expected: at least one failure. If the rerank test passes immediately, make `ForbiddenChunkIds` include the first non-relevant chunk returned by current insertion order and set `ChunkTopK = 1` so rerank survival is required.

- [ ] **Step 4: Make vector ordering deterministic for evaluation if needed**

If rerank cannot express the intended survival condition because `InMemoryVectorStore.QueryAsync` always returns insertion order and top-k excludes needed distractors, add a test-only score override to `InMemoryVectorStore`:

```csharp
public Dictionary<string, float> QueryScoresByDocumentId { get; } = new(StringComparer.Ordinal);
```

Update the `QueryAsync` ordering:

```csharp
var results = GetCollection(collection)
    .Values
    .Select(document => new
    {
        Document = document,
        Score = QueryScoresByDocumentId.TryGetValue(document.Id, out var score) ? score : 1.0f
    })
    .OrderByDescending(item => item.Score)
    .ThenBy(item => item.Document.Id, StringComparer.Ordinal)
    .Take(topK)
    .Select(item => new SearchResult
    {
        Id = item.Document.Id,
        Score = item.Score,
        Metadata = Clone(item.Document.Metadata),
        Content = item.Document.Content
    })
    .ToList();
```

In the rerank test, set initial vector scores before running:

```csharp
fixture.VectorStore.QueryScoresByDocumentId["chunk-storage-vector-databases"] = 0.90f;
fixture.VectorStore.QueryScoresByDocumentId["chunk-evaluation-quality-metrics"] = 0.80f;
fixture.VectorStore.QueryScoresByDocumentId["chunk-operations-health-cache"] = 0.70f;
```

- [ ] **Step 5: Run Mix and rerank tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Mix_ReturnsKgEntityRelationshipAndRelatedChunk|FullyQualifiedName~Rerank_KeepsRelevantChunkInFinalContext" --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Run the full evaluation suite**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal
```

Expected: all evaluation tests pass.

- [ ] **Step 7: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation tests\LightRAGNet.Tests\TestDoubles\InMemoryVectorStore.cs
git commit -m "test: add mix and rerank retrieval evaluation"
```

## Task 6: Verify Scope and Full Solution

**Files:**
- No new code files unless previous tasks reveal a compile issue.

- [ ] **Step 1: Verify no frontend or API files changed**

Run:

```powershell
git diff --name-only HEAD~5..HEAD
```

Expected changed files are limited to:

```text
tests/LightRAGNet.Tests/Evaluation/...
tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs
```

The exact `HEAD~5` count assumes Tasks 1-5 each committed once. If the commit count differs, use `git diff --name-only origin/main..HEAD` and apply the same file boundary.

- [ ] **Step 2: Run focused evaluation tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal
```

Expected: all evaluation tests pass.

- [ ] **Step 3: Run related retrieval tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~RerankCoordinator|FullyQualifiedName~ReferenceListBuilder" --verbosity minimal
```

Expected: all selected tests pass.

- [ ] **Step 4: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --verbosity minimal
```

Expected: all projects pass.

- [ ] **Step 5: Commit verification-only documentation if needed**

If no code changes are needed after verification, do not create an empty commit. If a small test fix was needed, commit it:

```powershell
git add tests\LightRAGNet.Tests\Evaluation tests\LightRAGNet.Tests\TestDoubles\InMemoryVectorStore.cs
git commit -m "test: finalize retrieval evaluation fixture"
```

## Plan Self-Review

- Spec coverage:
  - Small deterministic corpus: Task 2.
  - At least five oracle cases: Tasks 3, 4, and 5.
  - `Naive`, `Local`, `Global`, `Mix`: Tasks 3, 4, and 5.
  - Deterministic rerank survival: Task 5.
  - No frontend/API changes: File Structure and Task 6.
  - Normal `dotnet test` execution: Tasks 5 and 6.
- Placeholder scan:
  - No `TBD`, `TODO`, or unspecified implementation step remains.
- Type consistency:
  - `RetrievalEvaluationCase`, `ExpectedRelationshipPair`, `RetrievalEvaluationFixture`, `RetrievalEvaluationResult`, and `RetrievalEvaluationRunner` are introduced before use.
  - Test helpers use existing `InMemoryVectorStore`, `InMemoryGraphStore`, `InMemoryKvStore`, `FakeTokenizer`, `NaiveQueryService`, and `RetrievalContextService` types.
