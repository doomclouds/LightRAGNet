# Rerank Chunking Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Python-style rerank document chunking so long retrieved chunks are reranked as overlapping subdocuments, then aggregated back to original chunk order.

**Architecture:** Introduce a provider-agnostic rerank coordination layer in `LightRAGNet.Services.Query`. The coordinator owns rerank document chunking, provider-level fan-out, score aggregation, and document-level `topN`; `NaiveQueryService` and `RetrievalContextService` reuse it while keeping public query APIs unchanged.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NSubstitute, existing `FakeTokenizer` and in-memory vector store test doubles.

---

## Scope Check

This plan implements one subsystem: rerank input chunking and document-level score aggregation for query-time chunk ordering. It does not change public API, UI, prompt templates, cache semantics, indexing, deletion, provider protocols, real Qdrant/Neo4j integration, or rerank provider selection.

## File Structure

- Create: `src/LightRAGNet/Services/Query/RerankChunkingOptions.cs`
  - Holds internal defaults for chunking behavior: enabled, max tokens per document, overlap tokens.
- Create: `src/LightRAGNet/Services/Query/RerankDocumentChunker.cs`
  - Splits long rerank documents into overlapping subdocuments.
  - Returns subdocument text plus a subdocument-to-original-document index map.
- Create: `src/LightRAGNet/Services/Query/RerankCoordinator.cs`
  - Calls `IRerankService`.
  - Switches between direct rerank and chunked rerank.
  - Aggregates subdocument scores back to original document indices using `max`.
  - Applies document-level `topN` after aggregation.
- Modify: `src/LightRAGNet/Services/Query/NaiveQueryService.cs`
  - Replace direct `IRerankService` dependency with `RerankCoordinator`.
  - Keep chunk-to-context and raw-data behavior unchanged.
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
  - Replace direct vector chunk rerank call with `RerankCoordinator`.
  - Leave KG entity/relation related chunks and context builder untouched.
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Bind `RerankChunkingOptions` from the existing `Rerank` section.
  - Register `RerankDocumentChunker` and `RerankCoordinator`.
- Create: `tests/LightRAGNet.Tests/Query/RerankDocumentChunkerTests.cs`
  - Unit coverage for short documents, long document overlap, mapping, clamp, and empty input.
- Create: `tests/LightRAGNet.Tests/Query/RerankCoordinatorTests.cs`
  - Unit coverage for direct provider call, chunked provider call, max aggregation, document-level topN, invalid indexes, and duplicates.
- Modify: `tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs`
  - Update constructor helper.
  - Add long chunk aggregation behavior test.
  - Keep existing duplicate/invalid index behavior covered through coordinator.
- Modify: retrieval context tests that construct `RetrievalContextService`
  - Update constructor call sites to pass `RerankCoordinator`.
  - Add one focused KG `Mix` vector chunk rerank aggregation test under `tests/LightRAGNet.Tests/RetrievalContext/`.

## Task 1: Add Rerank Document Chunker

**Files:**
- Create: `tests/LightRAGNet.Tests/Query/RerankDocumentChunkerTests.cs`
- Create: `src/LightRAGNet/Services/Query/RerankChunkingOptions.cs`
- Create: `src/LightRAGNet/Services/Query/RerankDocumentChunker.cs`

- [ ] **Step 1: Write the failing chunker tests**

Create `tests/LightRAGNet.Tests/Query/RerankDocumentChunkerTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.Query;

public sealed class RerankDocumentChunkerTests
{
    [Fact]
    public void Chunk_WhenDocumentsAreShort_PreservesOneToOneMapping()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 10, overlapTokens: 2);
        var documents = new[] { "alpha beta", "gamma delta" };

        var result = chunker.Chunk(documents);

        result.Documents.Should().Equal(documents);
        result.DocumentIndices.Should().Equal(0, 1);
        result.WasChunked.Should().BeFalse();
    }

    [Fact]
    public void Chunk_WhenDocumentExceedsLimit_SplitsWithOverlap()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 4, overlapTokens: 1);

        var result = chunker.Chunk(["one two three four five six seven"]);

        result.Documents.Should().Equal(
            "one two three four",
            "four five six seven");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenMultipleDocumentsExceedLimit_PreservesOriginalDocumentIndices()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 3, overlapTokens: 1);

        var result = chunker.Chunk([
            "a b c d e",
            "short",
            "x y z q"
        ]);

        result.Documents.Should().Equal(
            "a b c",
            "c d e",
            "short",
            "x y z",
            "z q");
        result.DocumentIndices.Should().Equal(0, 0, 1, 2, 2);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenOverlapIsGreaterThanOrEqualToMaxTokens_ClampsOverlapAndTerminates()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 2, overlapTokens: 5);

        var result = chunker.Chunk(["a b c"]);

        result.Documents.Should().Equal("a b", "b c");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenInputIsEmpty_ReturnsEmptyResult()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 4, overlapTokens: 1);

        var result = chunker.Chunk([]);

        result.Documents.Should().BeEmpty();
        result.DocumentIndices.Should().BeEmpty();
        result.WasChunked.Should().BeFalse();
    }

    private static RerankDocumentChunker CreateChunker(int maxTokensPerDocument, int overlapTokens)
    {
        return new RerankDocumentChunker(
            new FakeTokenizer(),
            Options.Create(new RerankChunkingOptions
            {
                MaxTokensPerDocument = maxTokensPerDocument,
                OverlapTokens = overlapTokens
            }));
    }
}
```

- [ ] **Step 2: Run the new chunker tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RerankDocumentChunkerTests --verbosity minimal
```

Expected: FAIL with compile errors because `RerankDocumentChunker` and `RerankChunkingOptions` do not exist.

- [ ] **Step 3: Add options and chunker implementation**

Create `src/LightRAGNet/Services/Query/RerankChunkingOptions.cs`:

```csharp
namespace LightRAGNet.Services.Query;

public sealed class RerankChunkingOptions
{
    public bool EnableChunking { get; set; } = true;

    public int MaxTokensPerDocument { get; set; } = 480;

    public int OverlapTokens { get; set; } = 32;
}
```

Create `src/LightRAGNet/Services/Query/RerankDocumentChunker.cs`:

```csharp
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.Query;

public sealed class RerankDocumentChunker(
    ITokenizer tokenizer,
    IOptions<RerankChunkingOptions> options)
{
    private readonly RerankChunkingOptions _options = options.Value;

    public RerankChunkingResult Chunk(IReadOnlyList<string> documents)
    {
        if (documents.Count == 0)
        {
            return new RerankChunkingResult([], [], WasChunked: false);
        }

        var maxTokens = Math.Max(1, _options.MaxTokensPerDocument);
        var overlapTokens = Math.Clamp(_options.OverlapTokens, 0, Math.Max(0, maxTokens - 1));
        var chunkedDocuments = new List<string>();
        var documentIndices = new List<int>();
        var wasChunked = false;

        for (var documentIndex = 0; documentIndex < documents.Count; documentIndex++)
        {
            var document = documents[documentIndex];
            if (tokenizer.CountTokens(document) <= maxTokens)
            {
                chunkedDocuments.Add(document);
                documentIndices.Add(documentIndex);
                continue;
            }

            wasChunked = true;
            var tokens = SplitByWhitespace(document);
            if (tokens.Count == 0)
            {
                chunkedDocuments.Add(document);
                documentIndices.Add(documentIndex);
                continue;
            }

            var start = 0;
            while (start < tokens.Count)
            {
                var count = Math.Min(maxTokens, tokens.Count - start);
                chunkedDocuments.Add(string.Join(' ', tokens.Skip(start).Take(count)));
                documentIndices.Add(documentIndex);

                if (start + count >= tokens.Count)
                {
                    break;
                }

                start += Math.Max(1, maxTokens - overlapTokens);
            }
        }

        return new RerankChunkingResult(chunkedDocuments, documentIndices, wasChunked);
    }

    private static List<string> SplitByWhitespace(string document)
    {
        return document
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

public sealed record RerankChunkingResult(
    List<string> Documents,
    List<int> DocumentIndices,
    bool WasChunked);
```

- [ ] **Step 4: Run chunker tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RerankDocumentChunkerTests --verbosity minimal
```

Expected: PASS for all `RerankDocumentChunkerTests`.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add src/LightRAGNet/Services/Query/RerankChunkingOptions.cs src/LightRAGNet/Services/Query/RerankDocumentChunker.cs tests/LightRAGNet.Tests/Query/RerankDocumentChunkerTests.cs
git commit -m "feat: add rerank document chunker"
```

## Task 2: Add Rerank Coordinator

**Files:**
- Create: `tests/LightRAGNet.Tests/Query/RerankCoordinatorTests.cs`
- Create: `src/LightRAGNet/Services/Query/RerankCoordinator.cs`

- [ ] **Step 1: Write failing coordinator tests**

Create `tests/LightRAGNet.Tests/Query/RerankCoordinatorTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class RerankCoordinatorTests
{
    [Fact]
    public async Task RerankAsync_WhenChunkingDisabled_PassesOriginalDocumentsAndTopN()
    {
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 1, Arg.Any<CancellationToken>())
            .Returns([new RerankResult { Index = 1, RelevanceScore = 0.9f }]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: false, maxTokensPerDocument: 2);

        var result = await coordinator.RerankAsync("alpha", ["one two three", "short"], 1);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RerankResult { Index = 1, RelevanceScore = 0.9f });
        await rerankService.Received(1)
            .RerankAsync("alpha", Arg.Is<List<string>>(docs => docs.SequenceEqual(new[] { "one two three", "short" })), 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_WhenChunkingEnabled_PassesAllSubdocumentsToProvider()
    {
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 3, Arg.Any<CancellationToken>())
            .Returns([
                new RerankResult { Index = 0, RelevanceScore = 0.1f },
                new RerankResult { Index = 1, RelevanceScore = 0.9f },
                new RerankResult { Index = 2, RelevanceScore = 0.3f }
            ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: true, maxTokensPerDocument: 2, overlapTokens: 1);

        var result = await coordinator.RerankAsync("alpha", ["one two three", "four five"], 1);

        result.Should().ContainSingle();
        result[0].Index.Should().Be(0);
        result[0].RelevanceScore.Should().Be(0.9f);
        await rerankService.Received(1)
            .RerankAsync(
                "alpha",
                Arg.Is<List<string>>(docs => docs.SequenceEqual(new[] { "one two", "two three", "four five" })),
                3,
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_WhenMultipleSubdocumentsMapToSameDocument_UsesMaxScore()
    {
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 4, Arg.Any<CancellationToken>())
            .Returns([
                new RerankResult { Index = 0, RelevanceScore = 0.2f },
                new RerankResult { Index = 1, RelevanceScore = 0.8f },
                new RerankResult { Index = 2, RelevanceScore = 0.5f },
                new RerankResult { Index = 3, RelevanceScore = 0.4f }
            ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: true, maxTokensPerDocument: 2, overlapTokens: 1);

        var result = await coordinator.RerankAsync("alpha", ["one two three", "four five six"], 2);

        result.Select(item => item.Index).Should().Equal(0, 1);
        result.Select(item => item.RelevanceScore).Should().Equal(0.8f, 0.5f);
    }

    [Fact]
    public async Task RerankAsync_AppliesTopNAfterDocumentAggregation()
    {
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 6, Arg.Any<CancellationToken>())
            .Returns([
                new RerankResult { Index = 0, RelevanceScore = 0.9f },
                new RerankResult { Index = 1, RelevanceScore = 0.8f },
                new RerankResult { Index = 2, RelevanceScore = 0.7f },
                new RerankResult { Index = 3, RelevanceScore = 0.6f },
                new RerankResult { Index = 4, RelevanceScore = 0.5f },
                new RerankResult { Index = 5, RelevanceScore = 0.4f }
            ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: true, maxTokensPerDocument: 2, overlapTokens: 1);

        var result = await coordinator.RerankAsync("alpha", ["a b c", "d e f", "g h i"], 2);

        result.Select(item => item.Index).Should().Equal(0, 1);
        result.Should().HaveCount(2);
        await rerankService.Received(1)
            .RerankAsync("alpha", Arg.Is<List<string>>(docs => docs.Count == 6), 6, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_IgnoresInvalidAndDuplicateSubdocumentIndexes()
    {
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 3, Arg.Any<CancellationToken>())
            .Returns([
                new RerankResult { Index = 99, RelevanceScore = 1.0f },
                new RerankResult { Index = -1, RelevanceScore = 0.95f },
                new RerankResult { Index = 1, RelevanceScore = 0.6f },
                new RerankResult { Index = 1, RelevanceScore = 0.8f },
                new RerankResult { Index = 2, RelevanceScore = 0.7f }
            ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: true, maxTokensPerDocument: 2, overlapTokens: 1);

        var result = await coordinator.RerankAsync("alpha", ["one two three", "four five"], 2);

        result.Select(item => item.Index).Should().Equal(0, 1);
        result.Select(item => item.RelevanceScore).Should().Equal(0.8f, 0.7f);
    }

    [Fact]
    public async Task RerankAsync_WhenDocumentsAreEmpty_ReturnsEmptyWithoutCallingProvider()
    {
        var rerankService = Substitute.For<IRerankService>();
        var coordinator = CreateCoordinator(rerankService);

        var result = await coordinator.RerankAsync("alpha", [], 3);

        result.Should().BeEmpty();
        await rerankService.DidNotReceiveWithAnyArgs().RerankAsync(default!, default!, default);
    }

    private static RerankCoordinator CreateCoordinator(
        IRerankService rerankService,
        bool enableChunking = true,
        int maxTokensPerDocument = 480,
        int overlapTokens = 32)
    {
        var options = Options.Create(new RerankChunkingOptions
        {
            EnableChunking = enableChunking,
            MaxTokensPerDocument = maxTokensPerDocument,
            OverlapTokens = overlapTokens
        });
        var chunker = new RerankDocumentChunker(new FakeTokenizer(), options);
        return new RerankCoordinator(rerankService, chunker, options);
    }
}
```

- [ ] **Step 2: Run coordinator tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RerankCoordinatorTests --verbosity minimal
```

Expected: FAIL with compile errors because `RerankCoordinator` does not exist.

- [ ] **Step 3: Add coordinator implementation**

Create `src/LightRAGNet/Services/Query/RerankCoordinator.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.Query;

public sealed class RerankCoordinator(
    IRerankService rerankService,
    RerankDocumentChunker chunker,
    IOptions<RerankChunkingOptions> options)
{
    private readonly RerankChunkingOptions _options = options.Value;

    public async Task<List<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0 || topN <= 0)
        {
            return [];
        }

        if (!_options.EnableChunking)
        {
            return await rerankService.RerankAsync(
                query,
                documents.ToList(),
                topN,
                cancellationToken);
        }

        var chunkingResult = chunker.Chunk(documents);
        if (!chunkingResult.WasChunked)
        {
            return await rerankService.RerankAsync(
                query,
                documents.ToList(),
                topN,
                cancellationToken);
        }

        var chunkResults = await rerankService.RerankAsync(
            query,
            chunkingResult.Documents,
            chunkingResult.Documents.Count,
            cancellationToken);

        return AggregateByMaxScore(
            chunkResults,
            chunkingResult.DocumentIndices,
            documents.Count,
            topN);
    }

    private static List<RerankResult> AggregateByMaxScore(
        IEnumerable<RerankResult> chunkResults,
        IReadOnlyList<int> documentIndices,
        int documentCount,
        int topN)
    {
        var bestScores = new Dictionary<int, float>();
        foreach (var result in chunkResults)
        {
            if (result.Index < 0 || result.Index >= documentIndices.Count)
            {
                continue;
            }

            var documentIndex = documentIndices[result.Index];
            if (documentIndex < 0 || documentIndex >= documentCount)
            {
                continue;
            }

            if (!bestScores.TryGetValue(documentIndex, out var current) || result.RelevanceScore > current)
            {
                bestScores[documentIndex] = result.RelevanceScore;
            }
        }

        return bestScores
            .Select(pair => new RerankResult
            {
                Index = pair.Key,
                RelevanceScore = pair.Value
            })
            .OrderByDescending(result => result.RelevanceScore)
            .ThenBy(result => result.Index)
            .Take(topN)
            .ToList();
    }
}
```

- [ ] **Step 4: Run chunker and coordinator tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~RerankDocumentChunkerTests|FullyQualifiedName~RerankCoordinatorTests" --verbosity minimal
```

Expected: PASS for all chunker and coordinator tests.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git add src/LightRAGNet/Services/Query/RerankCoordinator.cs tests/LightRAGNet.Tests/Query/RerankCoordinatorTests.cs
git commit -m "feat: coordinate rerank document chunking"
```

## Task 3: Route Naive Query Rerank Through Coordinator

**Files:**
- Modify: `src/LightRAGNet/Services/Query/NaiveQueryService.cs`
- Modify: `tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs`

- [ ] **Step 1: Write the failing Naive aggregation test**

In `tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs`, add this test near the existing rerank tests:

```csharp
[Fact]
public async Task BuildContextAsync_WhenLongChunksAreReranked_OrdersOriginalChunksByAggregatedScore()
{
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "alpha one two three",
        Metadata = new Dictionary<string, object> { ["file_path"] = "docs/a.md" }
    });
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-b",
        Content = "beta four five six",
        Metadata = new Dictionary<string, object> { ["file_path"] = "docs/b.md" }
    });
    var rerankService = Substitute.For<IRerankService>();
    rerankService
        .RerankAsync("alpha", Arg.Any<List<string>>(), 6, Arg.Any<CancellationToken>())
        .Returns([
            new RerankResult { Index = 0, RelevanceScore = 0.2f },
            new RerankResult { Index = 1, RelevanceScore = 0.95f },
            new RerankResult { Index = 2, RelevanceScore = 0.1f },
            new RerankResult { Index = 3, RelevanceScore = 0.4f },
            new RerankResult { Index = 4, RelevanceScore = 0.3f },
            new RerankResult { Index = 5, RelevanceScore = 0.2f }
        ]);
    var service = CreateService(
        vectorStore,
        rerankService,
        rerankChunkingOptions: new RerankChunkingOptions
        {
            EnableChunking = true,
            MaxTokensPerDocument = 2,
            OverlapTokens = 1
        });

    var result = await service.BuildContextAsync(
        "alpha",
        new QueryParam
        {
            Mode = QueryMode.Naive,
            ChunkTopK = 2,
            EnableRerank = true,
            MaxTotalTokens = 1000
        },
        CancellationToken.None);

    result.Should().NotBeNull();
    result!.Context.IndexOf("alpha one two three", StringComparison.Ordinal)
        .Should()
        .BeLessThan(result.Context.IndexOf("beta four five six", StringComparison.Ordinal));
    await rerankService.Received(1)
        .RerankAsync(
            "alpha",
            Arg.Is<List<string>>(docs => docs.Count == 6 && docs.Contains("alpha one") && docs.Contains("one two") && docs.Contains("two three")),
            6,
            Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Update the Naive test helper to construct coordinator**

Replace the `CreateService` helper at the bottom of `NaiveQueryServiceTests.cs` with:

```csharp
private static NaiveQueryService CreateService(
    IVectorStore vectorStore,
    IRerankService? rerankService = null,
    ITokenizer? tokenizer = null,
    RerankChunkingOptions? rerankChunkingOptions = null)
{
    var actualTokenizer = tokenizer ?? new FakeTokenizer();
    var options = Microsoft.Extensions.Options.Options.Create(
        rerankChunkingOptions ?? new RerankChunkingOptions { EnableChunking = false });
    var coordinator = new RerankCoordinator(
        rerankService ?? Substitute.For<IRerankService>(),
        new RerankDocumentChunker(actualTokenizer, options),
        options);

    return new NaiveQueryService(
        vectorStore,
        coordinator,
        actualTokenizer);
}
```

This keeps existing Naive tests on direct rerank by default while the new test explicitly enables chunking.

- [ ] **Step 3: Run Naive tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~NaiveQueryServiceTests --verbosity minimal
```

Expected: FAIL with compile errors because `NaiveQueryService` still expects `IRerankService`, not `RerankCoordinator`.

- [ ] **Step 4: Update NaiveQueryService to use coordinator**

Modify `src/LightRAGNet/Services/Query/NaiveQueryService.cs` constructor and rerank method:

```csharp
public sealed class NaiveQueryService(
    IVectorStore vectorStore,
    RerankCoordinator rerankCoordinator,
    ITokenizer tokenizer)
```

Replace the body of `RerankChunksAsync` with:

```csharp
private async Task<List<ChunkData>> RerankChunksAsync(
    string query,
    List<ChunkData> chunks,
    int topK,
    CancellationToken cancellationToken)
{
    var rerankResults = await rerankCoordinator.RerankAsync(
        query,
        chunks.Select(chunk => chunk.Content).ToList(),
        topK,
        cancellationToken);

    return rerankResults
        .OrderByDescending(result => result.RelevanceScore)
        .DistinctBy(result => result.Index)
        .Where(result => result.Index >= 0 && result.Index < chunks.Count)
        .Select(result => chunks[result.Index])
        .ToList();
}
```

- [ ] **Step 5: Run Naive and coordinator tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~NaiveQueryServiceTests|FullyQualifiedName~RerankCoordinatorTests|FullyQualifiedName~RerankDocumentChunkerTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

Run:

```powershell
git add src/LightRAGNet/Services/Query/NaiveQueryService.cs tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs
git commit -m "refactor: route naive rerank through coordinator"
```

## Task 4: Route KG Mix Vector Chunk Rerank Through Coordinator

**Files:**
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`
- Modify: every test helper or construction site that calls `new RetrievalContextService(...)`

- [ ] **Step 1: Add a focused KG Mix aggregation test**

Add this test to `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`:

```csharp
[Fact]
public async Task BuildQueryContextAsync_WhenMixVectorChunksAreReranked_UsesAggregatedDocumentScores()
{
    var rerankService = Substitute.For<IRerankService>();
    rerankService
        .RerankAsync("alpha", Arg.Any<List<string>>(), 6, Arg.Any<CancellationToken>())
        .Returns([
            new RerankResult { Index = 0, RelevanceScore = 0.1f },
            new RerankResult { Index = 1, RelevanceScore = 0.9f },
            new RerankResult { Index = 2, RelevanceScore = 0.2f },
            new RerankResult { Index = 3, RelevanceScore = 0.4f },
            new RerankResult { Index = 4, RelevanceScore = 0.3f },
            new RerankResult { Index = 5, RelevanceScore = 0.2f }
        ]);
    var harness = CreateHarness(
        rerankService: rerankService,
        rerankChunkingOptions: new RerankChunkingOptions
        {
            EnableChunking = true,
            MaxTokensPerDocument = 2,
            OverlapTokens = 1
        });
    var service = harness.Service;
    var vectorStore = harness.VectorStore;

    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "alpha one two three",
        Metadata = new Dictionary<string, object> { ["file_path"] = "docs/a.md" },
        Vector = [1f, 0f]
    });
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-b",
        Content = "beta four five six",
        Metadata = new Dictionary<string, object> { ["file_path"] = "docs/b.md" },
        Vector = [0f, 1f]
    });
    var result = await service.BuildQueryContextAsync(
        "alpha",
        new KeywordsResult { HighLevelKeywords = ["alpha"], LowLevelKeywords = ["alpha"] },
        new QueryParam
        {
            Mode = QueryMode.Mix,
            ChunkTopK = 2,
            TopK = 2,
            EnableRerank = true,
            MaxTotalTokens = 1000
        },
        CancellationToken.None);

    result.Should().NotBeNull();
    var rawData = (Dictionary<string, object>)result!.RawData["data"];
    var chunks = ((IEnumerable<object>)rawData["chunks"]).Cast<Dictionary<string, object>>().ToList();
    chunks.Select(chunk => chunk["chunk_id"]).Should().ContainInOrder("chunk-a", "chunk-b");
    await rerankService.Received(1)
        .RerankAsync(
            "alpha",
            Arg.Is<List<string>>(docs => docs.Count == 6 && docs.Contains("alpha one") && docs.Contains("one two") && docs.Contains("two three")),
            6,
            Arg.Any<CancellationToken>());
}
```

Replace the existing `CreateHarness` helper in `RetrievalContextVectorChunkParityTests.cs` with this overload:

```csharp
private static TestHarness CreateHarness(
    LightRAGOptions? options = null,
    string? failingQuery = null,
    IRerankService? rerankService = null,
    RerankChunkingOptions? rerankChunkingOptions = null)
{
    var embeddingService = Substitute.For<IEmbeddingService>();
    embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(call =>
        {
            if (call.ArgAt<string>(0) == failingQuery)
            {
                throw new InvalidOperationException("Query embedding failed.");
            }

            return [1.0f, 0.0f];
        });

    var actualRerankService = rerankService ?? Substitute.For<IRerankService>();
    var tokenizer = new FakeTokenizer();
    var rerankOptions = Options.Create(
        rerankChunkingOptions ?? new RerankChunkingOptions { EnableChunking = false });
    var vectorStore = new InMemoryVectorStore();
    var graphStore = new InMemoryGraphStore();
    var textChunks = new InMemoryKvStore();
    var service = new RetrievalContextService(
        embeddingService,
        vectorStore,
        graphStore,
        new RerankCoordinator(
            actualRerankService,
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions),
        tokenizer,
        textChunks,
        Options.Create(options ?? new LightRAGOptions
        {
            KgChunkPickMethod = "VECTOR",
            RelatedChunkNumber = 4
        }),
        NullLoggerFactory.Instance);

    return new TestHarness(service, vectorStore, graphStore, textChunks);
}
```

Add these usings at the top of the file if they are missing:

```csharp
using LightRAGNet.Services.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
```

- [ ] **Step 2: Run retrieval context tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~RetrievalContextVectorChunkParityTests|FullyQualifiedName~RetrievalContextServiceRawDataTests|FullyQualifiedName~RetrievalContextServiceModeTests" --verbosity minimal
```

Expected: FAIL with constructor compile errors because `RetrievalContextService` still has no `RerankCoordinator` parameter.

- [ ] **Step 3: Update RetrievalContextService constructor and rerank call**

Modify the primary constructor in `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs` to remove the direct `IRerankService` dependency and add `RerankCoordinator`:

```csharp
public class RetrievalContextService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    IGraphStore graphStore,
    RerankCoordinator rerankCoordinator,
    ITokenizer tokenizer,
    [FromKeyedServices(KVContracts.TextChunks)] IKVStore textChunksStore,
    IOptions<LightRAGOptions> options,
    ILoggerFactory loggerFactory)
```

Add this using if missing:

```csharp
using LightRAGNet.Services.Query;
```

In `RetrieveChunksAsync`, replace the direct rerank call:

```csharp
var rerankResults = await rerankService.RerankAsync(
    query,
    documents,
    queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK,
    cancellationToken);
```

with:

```csharp
var rerankResults = await rerankCoordinator.RerankAsync(
    query,
    vectorChunks.Select(chunk => chunk.Content).ToList(),
    queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK,
    cancellationToken);
```

Keep the existing `OrderByDescending`, `DistinctBy`, valid-index filtering, and `Select(result => vectorChunks[result.Index])` mapping.

- [ ] **Step 4: Update RetrievalContextService construction sites**

Run:

```powershell
rg -n "new RetrievalContextService|RetrievalContextService\\(" tests src -g "*.cs"
```

For every `new RetrievalContextService(...)` in tests, create a coordinator with chunking disabled by default:

```csharp
var tokenizer = new FakeTokenizer();
var rerankService = Substitute.For<IRerankService>();
var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
var rerankCoordinator = new RerankCoordinator(
    rerankService,
    new RerankDocumentChunker(tokenizer, rerankOptions),
    rerankOptions);
```

Then pass `rerankCoordinator` as the fourth constructor argument:

```csharp
var retrievalContextService = new RetrievalContextService(
    embeddingService,
    vectorStore,
    graphStore,
    rerankCoordinator,
    tokenizer,
    textChunksStore,
    Options.Create(lightRagOptions),
    loggerFactory);
```

Existing tests that do not explicitly verify chunking should use `EnableChunking = false` so their expectations stay stable.

- [ ] **Step 5: Register coordinator and options in Hosting**

In `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`, add configuration binding near the existing `AliyunRerankOptions` binding:

```csharp
services.Configure<RerankChunkingOptions>(configuration.GetSection("Rerank"));
```

Add singleton registrations in the retrieval services region before `NaiveQueryService` and `RetrievalContextService` consumers are resolved:

```csharp
services.AddSingleton<RerankDocumentChunker>();
services.AddSingleton<RerankCoordinator>();
```

Ensure `using LightRAGNet.Services.Query;` already covers these types.

- [ ] **Step 6: Run focused retrieval and query tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Rerank|FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~LightRAGQueryModeTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit Task 4**

Run:

```powershell
git add src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs tests/LightRAGNet.Tests
git commit -m "refactor: route kg rerank through coordinator"
```

## Task 5: Regression, Scope Audit, and Close-Out

**Files:**
- Modify only if a preceding task revealed a reviewed correction: `docs/superpowers/specs/2026-05-21-rerank-chunking-parity-design.md`
- Create after implementation is accepted: `docs/superpowers/archives/2026-05/2026-05-21-rerank-chunking-parity-archives.md`
- Modify after archive: `docs/superpowers/archives/INDEX.md`

- [ ] **Step 1: Run focused rerank/query/retrieval regression**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Rerank|FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~QueryCache" --verbosity minimal
```

Expected: PASS. This covers the new chunking layer, both query call sites, mode routing, and query cache key behavior around `EnableRerank`.

- [ ] **Step 2: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS for `LightRAGNet.Tests`, `LightRAGNet.Server.Tests`, and `LightRAGNet.Web.Tests`.

- [ ] **Step 3: Run full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: build succeeds with `0` errors. If warnings appear, inspect whether they are new and caused by this change before proceeding.

- [ ] **Step 4: Audit changed files for scope drift**

Run:

```powershell
git diff --name-only main...HEAD
```

Expected changed production files should be limited to:

```text
src/LightRAGNet/Services/Query/RerankChunkingOptions.cs
src/LightRAGNet/Services/Query/RerankDocumentChunker.cs
src/LightRAGNet/Services/Query/RerankCoordinator.cs
src/LightRAGNet/Services/Query/NaiveQueryService.cs
src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs
src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs
```

Expected test/docs files should be limited to:

```text
tests/LightRAGNet.Tests/Query/RerankDocumentChunkerTests.cs
tests/LightRAGNet.Tests/Query/RerankCoordinatorTests.cs
tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs
tests/LightRAGNet.Tests/RetrievalContext/*.cs
docs/superpowers/specs/2026-05-21-rerank-chunking-parity-design.md
docs/superpowers/plans/2026-05-21-rerank-chunking-parity-implementation-plan.md
```

If cache, indexing, deletion, prompt, server request, web UI, or real storage files changed, either revert the unrelated drift or document a reviewed reason before final review.

- [ ] **Step 5: Run asset completion gate before final handoff**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "rerank-chunking-parity" --json
```

Expected before archive: `missing_requirement_archive` is reported because the spec and plan exist. Route to archive after implementation is accepted and verified.

- [ ] **Step 6: Archive completed requirement**

Create `docs/superpowers/archives/2026-05/2026-05-21-rerank-chunking-parity-archives.md` using the archive skill template. Include:

```text
Summary: Rerank long documents are split into overlapping subdocuments, scored by the provider, aggregated back to original chunks with max score, and reused by Naive and KG Mix.
Delivered Scope: chunker, coordinator, Naive integration, KG Mix integration, focused tests, full regression.
Out of Scope: public API, UI, cache, indexing, deletion, prompt, real provider integration.
Verification Snapshot: focused tests, full solution tests, build, scope audit.
Related Problems: None for the initial archive draft; update this line only when spec review or code quality review reports a concrete reusable failure mode.
```

Update `docs/superpowers/archives/INDEX.md` with one `2026-05` entry:

```markdown
- [2026-05-21-rerank-chunking-parity-archives.md](./2026-05/2026-05-21-rerank-chunking-parity-archives.md): 将 rerank 长 chunk 对齐为 Python 风格的子片段评分与原始 chunk 级 max-score 聚合，供 Naive 和 KG Mix 共用。
```

- [ ] **Step 7: Validate archive and indexes**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\archive-superpowers-feature\scripts\validate_archive_asset.py .\docs\superpowers\archives\2026-05\2026-05-21-rerank-chunking-parity-archives.md
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_indexes.py . --json
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "rerank-chunking-parity" --json
```

Expected: archive validator OK, index check has no issues, completion gate status is `pass`.

- [ ] **Step 8: Commit close-out assets**

Run:

```powershell
git add docs/superpowers/archives/2026-05/2026-05-21-rerank-chunking-parity-archives.md docs/superpowers/archives/INDEX.md
git commit -m "docs: archive rerank chunking parity"
```

## Spec Coverage Review

- Python chunking semantics are covered by Task 1.
- Python max-score aggregation and document-level topN are covered by Task 2.
- Naive query integration is covered by Task 3.
- KG Mix vector chunk integration is covered by Task 4.
- Public API and UI compatibility are protected by Task 5 scope audit and full solution tests.
- Requirement archiving and problem gate are covered by Task 5.

## Execution Notes

- Use an isolated worktree for implementation.
- Use strict RED/GREEN TDD for each task.
- Prefer one commit per task.
- Keep `RerankChunkingOptions.EnableChunking = false` in unrelated existing tests unless the test explicitly verifies chunking. This prevents incidental behavior changes from hiding the focused signal.
- Task 4 removes `IRerankService` from `RetrievalContextService`; update all construction sites in that same task. Do not leave unused constructor parameters.
