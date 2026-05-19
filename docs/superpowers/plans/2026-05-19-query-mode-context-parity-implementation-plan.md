# Query Mode Context Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Every production-code step must be preceded by a failing test and a recorded RED result.

**Goal:** Align .NET query mode behavior with Python LightRAG for `Naive`, `Bypass`, KG keyword fallback, explicit mode routing, and query raw data.

**Architecture:** Keep `LightRAG.QueryAsync` as the public API, but split mode-specific behavior. `Bypass` becomes a direct LLM path, `Naive` moves into a dedicated vector-only `NaiveQueryService`, and KG modes remain in `RetrievalContextService` behind an explicit strategy map and keyword fallback policy.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, Microsoft.Extensions.DependencyInjection, LightRAGNet fake stores.

---

## Required Worktree

Implementation work should happen in an isolated worktree:

```text
C:\WorkSpace\RiderProjects\LightRAGNet\.worktrees\query-mode-context-parity
```

Create it at execution time from `main`:

```powershell
git worktree add .\.worktrees\query-mode-context-parity -b feature/query-mode-context-parity
```

Baseline verification from the worktree:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore
```

If Visual Studio locks build outputs, use an isolated artifacts path:

```powershell
dotnet build .\LightRAGNet.slnx --artifacts-path "$env:TEMP\LightRAGNet-query-mode-artifacts"
```

## Spec-to-Plan Traceability

| Spec requirement | Plan coverage |
| --- | --- |
| `Bypass` directly calls LLM | Task 4 |
| `Bypass` skips keyword extraction and retrieval | Task 4 tests |
| `Naive` uses chunk vector retrieval only | Task 3 |
| `Naive` supports context, prompt, streaming, non-streaming | Task 4 |
| KG keyword fallback for short empty-keyword query | Task 2 |
| Long empty-keyword KG query returns fail/no-context | Task 2 and Task 4 |
| `KGSearchStrategyFactory` rejects unsupported modes | Task 1 |
| `RetrievalContextService` rejects `Naive`/`Bypass` direct calls | Task 1 |
| Raw data includes chunks, references, keywords, processing info | Task 3 and Task 5 |
| `QueryResult.ReferenceList` reads emitted references | Task 5 |
| API/Web chat remains default Mix streaming | Task 6 associated-impact check |
| Normal tests require no Docker | Every task uses fakes/substitutes |

## File Map

- Create: `src/LightRAGNet/Services/Query/QueryKeywordPolicy.cs`
  - Normalizes extracted keywords for KG query modes.
- Create: `src/LightRAGNet/Services/Query/NaiveQueryService.cs`
  - Builds vector-only context and raw data for `QueryMode.Naive`.
- Create: `src/LightRAGNet/Services/Query/NaiveQueryPromptBuilder.cs`
  - Builds the Naive system prompt and matching prompt-overhead text.
- Modify: `src/LightRAGNet/LightRAG.cs`
  - Routes `Bypass`, `Naive`, and KG modes explicitly.
- Modify: `src/LightRAGNet/Services/RetrievalContext/KGSearchStrategyFactory.cs`
  - Removes silent fallback.
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
  - Rejects non-KG modes and enriches raw data.
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Registers `NaiveQueryService`.
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
  - Records vector query calls and returns seeded search results.
- Create: `tests/LightRAGNet.Tests/Query/QueryKeywordPolicyTests.cs`
- Create: `tests/LightRAGNet.Tests/Query/LightRAGQueryModeTests.cs`
- Create: `tests/LightRAGNet.Tests/RetrievalContext/KGSearchStrategyFactoryTests.cs`
- Create: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceModeTests.cs`
- Create: `tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs`

---

## Task 1: Explicit KG Mode Boundaries

**Spec coverage:** unsupported KG modes fail loudly; `Naive` and `Bypass` cannot silently become `Mix`.

**Files:**

- Modify: `src/LightRAGNet/Services/RetrievalContext/KGSearchStrategyFactory.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
- Test: `tests/LightRAGNet.Tests/RetrievalContext/KGSearchStrategyFactoryTests.cs`
- Test: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceModeTests.cs`

- [ ] **Step 1: Write failing factory tests**

Create `KGSearchStrategyFactoryTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class KGSearchStrategyFactoryTests
{
    [Theory]
    [InlineData(QueryMode.Local, typeof(LocalSearchStrategy))]
    [InlineData(QueryMode.Global, typeof(GlobalSearchStrategy))]
    [InlineData(QueryMode.Hybrid, typeof(MixSearchStrategy))]
    [InlineData(QueryMode.Mix, typeof(MixSearchStrategy))]
    public void GetStrategy_WhenKgMode_ReturnsExplicitStrategy(QueryMode mode, Type expectedType)
    {
        var factory = CreateFactory();

        var strategy = factory.GetStrategy(mode);

        strategy.Should().BeOfType(expectedType);
    }

    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public void GetStrategy_WhenNonKgMode_Throws(QueryMode mode)
    {
        var factory = CreateFactory();

        Action act = () => factory.GetStrategy(mode);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Query mode '{mode}' is not a knowledge graph search mode.");
    }

    private static KGSearchStrategyFactory CreateFactory()
    {
        return new KGSearchStrategyFactory(
            Substitute.For<IVectorStore>(),
            Substitute.For<IGraphStore>(),
            NullLoggerFactory.Instance);
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~KGSearchStrategyFactoryTests
```

Expected RED:

```text
Expected a NotSupportedException to be thrown, but no exception was thrown.
```

- [ ] **Step 3: Remove silent fallback**

Change `GetStrategy`:

```csharp
public IKGSearchStrategy GetStrategy(QueryMode mode)
{
    if (_strategies.TryGetValue(mode, out var strategy))
    {
        return strategy;
    }

    throw new NotSupportedException($"Query mode '{mode}' is not a knowledge graph search mode.");
}
```

- [ ] **Step 4: Add direct RetrievalContext guard tests**

Create `RetrievalContextServiceModeTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Storage;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextServiceModeTests
{
    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public async Task BuildQueryContextAsync_WhenModeIsNotKg_Throws(QueryMode mode)
    {
        var service = CreateService();

        var act = async () => await service.BuildQueryContextAsync(
            "alpha",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam { Mode = mode },
            CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage($"Query mode '{mode}' is not supported by RetrievalContextService.");
    }

    private static RetrievalContextService CreateService()
    {
        return new RetrievalContextService(
            Substitute.For<IEmbeddingService>(),
            Substitute.For<IVectorStore>(),
            Substitute.For<IGraphStore>(),
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            new InMemoryKvStore(),
            Options.Create(new LightRAGOptions()),
            NullLoggerFactory.Instance);
    }
}
```

- [ ] **Step 5: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RetrievalContextServiceModeTests
```

Expected RED before the guard:

```text
Expected a NotSupportedException to be thrown
```

- [ ] **Step 6: Add RetrievalContext guard**

At the start of `BuildQueryContextAsync`:

```csharp
if (queryParam.Mode is QueryMode.Naive or QueryMode.Bypass)
{
    throw new NotSupportedException(
        $"Query mode '{queryParam.Mode}' is not supported by RetrievalContextService.");
}
```

- [ ] **Step 7: Verify GREEN and commit**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~KGSearchStrategyFactoryTests|FullyQualifiedName~RetrievalContextServiceModeTests"
git add src/LightRAGNet/Services/RetrievalContext tests/LightRAGNet.Tests/RetrievalContext
git commit -m "refactor: reject non-kg retrieval modes"
```

---

## Task 2: KG Keyword Fallback Policy

**Spec coverage:** Python short-query keyword fallback and long-query failure decision.

**Files:**

- Create: `src/LightRAGNet/Services/Query/QueryKeywordPolicy.cs`
- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Tests/Query/QueryKeywordPolicyTests.cs`

- [ ] **Step 1: Write failing policy tests**

Create `QueryKeywordPolicyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;

namespace LightRAGNet.Tests.Query;

public sealed class QueryKeywordPolicyTests
{
    [Fact]
    public void NormalizeForKg_WhenKeywordsProvided_ReturnsOriginalKeywords()
    {
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["qdrant"]
        };

        var result = QueryKeywordPolicy.NormalizeForKg("query", QueryMode.Mix, keywords);

        result.Should().NotBeNull();
        result!.ShouldFail.Should().BeFalse();
        result.Keywords.HighLevelKeywords.Should().Equal("architecture");
        result.Keywords.LowLevelKeywords.Should().Equal("qdrant");
    }

    [Fact]
    public void NormalizeForKg_WhenBothKeywordListsEmptyAndQueryIsShort_UsesOriginalQueryAsLowLevelKeyword()
    {
        var result = QueryKeywordPolicy.NormalizeForKg(
            "Neo4j residual data",
            QueryMode.Mix,
            new KeywordsResult());

        result.Should().NotBeNull();
        result!.ShouldFail.Should().BeFalse();
        result.Keywords.HighLevelKeywords.Should().BeEmpty();
        result.Keywords.LowLevelKeywords.Should().Equal("Neo4j residual data");
    }

    [Fact]
    public void NormalizeForKg_WhenBothKeywordListsEmptyAndQueryIsLong_ReturnsFailDecision()
    {
        var longQuery = new string('a', 50);

        var result = QueryKeywordPolicy.NormalizeForKg(longQuery, QueryMode.Hybrid, new KeywordsResult());

        result.Should().NotBeNull();
        result!.ShouldFail.Should().BeTrue();
        result.Keywords.LowLevelKeywords.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~QueryKeywordPolicyTests
```

Expected RED:

```text
error CS0234: The type or namespace name 'Query' does not exist in the namespace 'LightRAGNet.Services'
```

- [ ] **Step 3: Implement policy**

Create `QueryKeywordPolicy.cs`:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.Query;

internal static class QueryKeywordPolicy
{
    public static QueryKeywordDecision NormalizeForKg(
        string query,
        QueryMode mode,
        KeywordsResult keywords)
    {
        if (mode is QueryMode.Naive or QueryMode.Bypass)
        {
            throw new ArgumentException($"Query mode '{mode}' is not a KG query mode.", nameof(mode));
        }

        if (keywords.HighLevelKeywords.Count > 0 || keywords.LowLevelKeywords.Count > 0)
        {
            return new QueryKeywordDecision(keywords, ShouldFail: false);
        }

        if (query.Length < 50)
        {
            return new QueryKeywordDecision(
                new KeywordsResult { LowLevelKeywords = [query] },
                ShouldFail: false);
        }

        return new QueryKeywordDecision(new KeywordsResult(), ShouldFail: true);
    }
}

internal sealed record QueryKeywordDecision(KeywordsResult Keywords, bool ShouldFail);
```

- [ ] **Step 4: Wire policy into KG path**

In `LightRAG.QueryAsync`, after extracting or using supplied keywords, add this only for KG modes:

```csharp
if (queryParam.Mode is QueryMode.Local or QueryMode.Global or QueryMode.Hybrid or QueryMode.Mix)
{
    var keywordDecision = QueryKeywordPolicy.NormalizeForKg(query, queryParam.Mode, keywords);
    if (keywordDecision.ShouldFail)
    {
        return new QueryResult
        {
            Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
        };
    }

    keywords = keywordDecision.Keywords;
}
```

`Naive` and `Bypass` branches will be added before keyword extraction in Task 4. Until then, this guard keeps Task 2 from accidentally invoking a KG-only policy for non-KG modes.

- [ ] **Step 5: Verify GREEN and commit**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~QueryKeywordPolicyTests
git add src/LightRAGNet/Services/Query/QueryKeywordPolicy.cs src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests/Query/QueryKeywordPolicyTests.cs
git commit -m "feat: add kg keyword fallback policy"
```

---

## Task 3: Naive Vector-Only Context Service

**Spec coverage:** `Naive` vector-only retrieval, dynamic chunk budget, references, raw data.

**Files:**

- Create: `src/LightRAGNet/Services/Query/NaiveQueryService.cs`
- Create: `src/LightRAGNet/Services/Query/NaiveQueryPromptBuilder.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
- Test: `tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs`

- [ ] **Step 1: Make vector store fake queryable**

Write the failing test inside `NaiveQueryServiceTests.cs` first. It will require `InMemoryVectorStore.QueryAsync` to return seeded documents:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class NaiveQueryServiceTests
{
    [Fact]
    public async Task BuildContextAsync_WhenChunksExist_QueriesChunksCollectionAndBuildsRawData()
    {
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha beta content",
            Metadata = new Dictionary<string, object> { ["file_path"] = "docs/a.md" }
        });
        var service = CreateService(vectorStore);

        var result = await service.BuildContextAsync(
            "alpha",
            new QueryParam { Mode = QueryMode.Naive, ChunkTopK = 3, TopK = 40, EnableRerank = false },
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Context.Should().Contain("alpha beta content");
        result.Context.Should().Contain("[1] docs/a.md");
        vectorStore.QueryCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Query == "alpha" &&
            call.TopK == 3);

        var data = result.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        data["entities"].Should().BeEquivalentTo(Array.Empty<object>());
        data["relationships"].Should().BeEquivalentTo(Array.Empty<object>());
        data["chunks"].Should().BeAssignableTo<IReadOnlyCollection<Dictionary<string, object>>>();

        var metadata = result.RawData["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;
        metadata["query_mode"].Should().Be("Naive");
        metadata["processing_info"].Should().BeAssignableTo<Dictionary<string, object>>();
    }

    private static NaiveQueryService CreateService(
        IVectorStore vectorStore,
        IRerankService? rerankService = null)
    {
        return new NaiveQueryService(
            vectorStore,
            rerankService ?? Substitute.For<IRerankService>(),
            new FakeTokenizer());
    }
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~NaiveQueryServiceTests.BuildContextAsync_WhenChunksExist
```

Expected RED:

```text
error CS0234: The type or namespace name 'Query' does not exist in the namespace 'LightRAGNet.Services'
```

- [ ] **Step 3: Record vector query calls**

Modify `InMemoryVectorStore`:

```csharp
public List<(string Collection, string Query, int TopK, float Threshold)> QueryCalls { get; } = [];

public Task<List<SearchResult>> QueryAsync(
    string collection,
    string query,
    int topK,
    float[]? queryEmbedding = null,
    float threshold = 0.2f,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    QueryCalls.Add((collection, query, topK, threshold));

    var results = GetCollection(collection)
        .Values
        .Take(topK)
        .Select(document => new SearchResult
        {
            Id = document.Id,
            Content = document.Content,
            Metadata = Clone(document.Metadata),
            Score = 1.0f
        })
        .ToList();

    return Task.FromResult(results);
}
```

- [ ] **Step 4: Implement `NaiveQueryService`**

Create `NaiveQueryService.cs`:

```csharp
using System.Text.Json;
using LightRAGNet;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.RetrievalContext;

namespace LightRAGNet.Services.Query;

public sealed class NaiveQueryService(
    IVectorStore vectorStore,
    IRerankService rerankService,
    ITokenizer tokenizer)
{
    private readonly ReferenceListBuilder _referenceListBuilder = new();
    private readonly ChunkTokenLimiter _chunkTokenLimiter = new(tokenizer);

    public async Task<QueryContextResult?> BuildContextAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken = default)
    {
        var topK = queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK;
        var results = await vectorStore.QueryAsync(
            "chunks",
            query,
            topK,
            queryEmbedding: null,
            cancellationToken: cancellationToken);

        if (results.Count == 0)
        {
            return null;
        }

        var chunks = results.Select(result => new ChunkData
        {
            ChunkId = result.Id,
            Content = result.Content,
            FilePath = result.Metadata.GetValueOrDefault("file_path")?.ToString() ?? "unknown_source"
        }).ToList();

        if (queryParam.EnableRerank && chunks.Count > 0)
        {
            var reranked = await rerankService.RerankAsync(
                query,
                chunks.Select(chunk => chunk.Content).ToList(),
                topK,
                cancellationToken);

            chunks = reranked
                .OrderByDescending(result => result.RelevanceScore)
                .Where(result => result.Index >= 0 && result.Index < chunks.Count)
                .Select(result => chunks[result.Index])
                .ToList();
        }

        var promptOverhead = tokenizer.CountTokens(
            NaiveQueryPromptBuilder.BuildPromptOverhead(queryParam));
        var availableChunkTokens = Math.Max(
            0,
            queryParam.MaxTotalTokens - promptOverhead - tokenizer.CountTokens(query) - 200);
        var limitedChunks = _chunkTokenLimiter.Limit(chunks, availableChunkTokens);
        if (limitedChunks.Count == 0)
        {
            return null;
        }

        var (references, chunksWithRefIds) = _referenceListBuilder.Build(limitedChunks);
        var context = BuildContext(chunksWithRefIds, references);
        var rawData = BuildRawData(results.Count, chunksWithRefIds, references);

        return new QueryContextResult
        {
            Context = context,
            RawData = rawData
        };
    }

    private static string BuildContext(
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        var chunkLines = chunks.Select(chunk => JsonSerializer.Serialize(new
        {
            reference_id = chunk.ReferenceId,
            content = chunk.Content
        }));

        var referenceLines = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Select(reference => $"[{reference.ReferenceId}] {reference.FilePath}");

        return $"""
                ---Document Chunks---
                {string.Join('\n', chunkLines)}

                ---Reference Document List---
                {string.Join('\n', referenceLines)}
                """;
    }

    private static Dictionary<string, object> BuildRawData(
        int totalChunksFound,
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        return new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["entities"] = Array.Empty<object>(),
                ["relationships"] = Array.Empty<object>(),
                ["chunks"] = chunks.Select(chunk => new Dictionary<string, object>
                {
                    ["chunk_id"] = chunk.ChunkId,
                    ["content"] = chunk.Content,
                    ["file_path"] = chunk.FilePath,
                    ["reference_id"] = chunk.ReferenceId,
                    ["source_type"] = "vector"
                }).ToList(),
                ["references"] = references.Select(reference => new Dictionary<string, object>
                {
                    ["reference_id"] = reference.ReferenceId,
                    ["file_path"] = reference.FilePath
                }).ToList()
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["query_mode"] = QueryMode.Naive.ToString(),
                ["keywords"] = new Dictionary<string, object>
                {
                    ["high_level"] = Array.Empty<string>(),
                    ["low_level"] = Array.Empty<string>()
                },
                ["processing_info"] = new Dictionary<string, object>
                {
                    ["total_chunks_found"] = totalChunksFound,
                    ["final_chunks_count"] = chunks.Count
                }
            }
        };
    }
}
```

Create `NaiveQueryPromptBuilder.cs`:

```csharp
using LightRAGNet;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.Query;

internal static class NaiveQueryPromptBuilder
{
    public static string BuildResponsePrompt(QueryContextResult contextResult, QueryParam queryParam)
    {
        return BuildPrompt(queryParam, contextResult.Context);
    }

    public static string BuildPromptOverhead(QueryParam queryParam)
    {
        return BuildPrompt(queryParam, string.Empty);
    }

    private static string BuildPrompt(QueryParam queryParam, string context)
    {
        var responseType = string.IsNullOrEmpty(queryParam.ResponseType)
            ? "Multiple Paragraphs"
            : queryParam.ResponseType;
        var userPrompt = queryParam.UserPrompt ?? "n/a";

        return $"""
                ---Role---

                You are an expert AI assistant answering from retrieved document chunks.

                ---Goal---

                Generate a {responseType} answer using only the provided document chunks.

                ---Instructions---

                - Use only the information in the context.
                - If the answer is not present in the context, say that there is not enough information.
                - Answer in the same language as the user query.
                - Additional instructions: {userPrompt}

                ---Context---

                {context}
                """;
    }
}
```

- [ ] **Step 5: Add no-chunks and rerank tests**

Append:

```csharp
[Fact]
public async Task BuildContextAsync_WhenNoChunks_ReturnsNull()
{
    var service = CreateService(new InMemoryVectorStore());

    var result = await service.BuildContextAsync(
        "missing",
        new QueryParam { Mode = QueryMode.Naive },
        CancellationToken.None);

    result.Should().BeNull();
}

[Fact]
public async Task BuildContextAsync_WhenRerankEnabled_OrdersChunksByRerankScore()
{
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument { Id = "chunk-a", Content = "first", Metadata = new() { ["file_path"] = "a.md" } });
    vectorStore.Seed("chunks", new VectorDocument { Id = "chunk-b", Content = "second", Metadata = new() { ["file_path"] = "b.md" } });
    var rerankService = Substitute.For<IRerankService>();
    rerankService.RerankAsync("alpha", Arg.Any<List<string>>(), 2, Arg.Any<CancellationToken>())
        .Returns([
            new RerankResult { Index = 0, RelevanceScore = 0.1f },
            new RerankResult { Index = 1, RelevanceScore = 0.9f }
        ]);
    var service = CreateService(vectorStore, rerankService);

    var result = await service.BuildContextAsync(
        "alpha",
        new QueryParam { Mode = QueryMode.Naive, ChunkTopK = 2, EnableRerank = true },
        CancellationToken.None);

    result.Should().NotBeNull();
    result!.Context.IndexOf("second", StringComparison.Ordinal)
        .Should().BeLessThan(result.Context.IndexOf("first", StringComparison.Ordinal));
}
```

- [ ] **Step 6: Register service**

Add `using LightRAGNet.Services.Query;` to `ServiceCollectionExtensions.cs`.

In `ServiceCollectionExtensions.AddLightRAG`:

```csharp
services.AddSingleton<NaiveQueryService>();
```

- [ ] **Step 7: Verify GREEN and commit**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~NaiveQueryServiceTests
git add src/LightRAGNet/Services/Query src/LightRAGNet.Hosting tests/LightRAGNet.Tests/Query tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs
git commit -m "feat: add naive query context service"
```

---

## Task 4: Route `Bypass` and `Naive` in `LightRAG.QueryAsync`

**Spec coverage:** direct Bypass generation, Naive result generation, `OnlyNeedContext`, `OnlyNeedPrompt`, streaming.

**Files:**

- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`
- Modify: `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`
- Test: `tests/LightRAGNet.Tests/Query/LightRAGQueryModeTests.cs`

- [ ] **Step 1: Write failing Bypass test**

Create `LightRAGQueryModeTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class LightRAGQueryModeTests
{
    [Fact]
    public async Task QueryAsync_WhenBypass_DirectlyCallsLlmAndSkipsKeywordExtraction()
    {
        var llm = Substitute.For<ILLMService>();
        llm.GenerateAsync("hello", null, Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>>(), 0.3f, false, Arg.Any<CancellationToken>())
            .Returns("direct answer");
        var rag = CreateLightRag(llm);

        var result = await rag.QueryAsync("hello", new QueryParam { Mode = QueryMode.Bypass });

        result.Content.Should().Be("direct answer");
        await llm.DidNotReceive().ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        var metadata = result.Metadata;
        metadata["query_mode"].Should().Be("Bypass");
    }

    private static LightRAG CreateLightRag(ILLMService llm)
    {
        var tokenizer = new FakeTokenizer();
        var kvStore = new InMemoryKvStore();
        return new LightRAG(
            llm,
            Substitute.For<IVectorStore>(),
            documentProcessingService: null!,
            knowledgeGraphMergeService: null!,
            retrievalContextService: null!,
            tokenizer,
            kvStore,
            kvStore,
            kvStore,
            kvStore,
            kvStore,
            kvStore,
            kvStore,
            documentLifecycleService: null!,
            documentDeletionService: null!,
            NullLogger<LightRAG>.Instance);
    }
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryModeTests.QueryAsync_WhenBypass
```

Expected RED:

```text
System.NullReferenceException
```

The current code tries to run keyword extraction/retrieval instead of direct bypass.

- [ ] **Step 3: Add Bypass branch**

Add `using LightRAGNet.Services.Query;` to `LightRAG.cs`.

Add `NaiveQueryService naiveQueryService` to the `LightRAG` primary constructor after `RetrievalContextService retrievalContextService`.

Update the `CreateLightRag` test helper to match the new constructor:

```csharp
private static LightRAG CreateLightRag(
    ILLMService llm,
    NaiveQueryService? naiveQueryService = null)
{
    var tokenizer = new FakeTokenizer();
    var kvStore = new InMemoryKvStore();
    return new LightRAG(
        llm,
        Substitute.For<IVectorStore>(),
        documentProcessingService: null!,
        knowledgeGraphMergeService: null!,
        retrievalContextService: null!,
        naiveQueryService ?? new NaiveQueryService(
            Substitute.For<IVectorStore>(),
            Substitute.For<IRerankService>(),
            tokenizer),
        tokenizer,
        kvStore,
        kvStore,
        kvStore,
        kvStore,
        kvStore,
        kvStore,
        kvStore,
        documentLifecycleService: null!,
        documentDeletionService: null!,
        NullLogger<LightRAG>.Instance);
}
```

At the beginning of `QueryAsync`, after empty-query handling:

```csharp
if (queryParam.Mode == QueryMode.Bypass)
{
    return await RunBypassQueryAsync(query, queryParam, cancellationToken);
}
```

Add helper:

```csharp
private async Task<QueryResult> RunBypassQueryAsync(
    string query,
    QueryParam queryParam,
    CancellationToken cancellationToken)
{
    var rawData = new Dictionary<string, object>
    {
        ["data"] = new Dictionary<string, object>(),
        ["metadata"] = new Dictionary<string, object>
        {
            ["query_mode"] = QueryMode.Bypass.ToString()
        }
    };

    if (queryParam.Stream)
    {
        return new QueryResult
        {
            ResponseIterator = llmService.GenerateStreamAsync(
                query,
                systemPrompt: null,
                historyMessages: queryParam.ConversationHistory,
                temperature: 0.3f,
                cancellationToken: cancellationToken),
            RawData = rawData,
            IsStreaming = true
        };
    }

    var response = await llmService.GenerateAsync(
        query,
        systemPrompt: null,
        historyMessages: queryParam.ConversationHistory,
        temperature: 0.3f,
        cancellationToken: cancellationToken);

    return new QueryResult
    {
        Content = response,
        RawData = rawData
    };
}
```

- [ ] **Step 4: Write failing Naive context/prompt tests**

Append:

```csharp
[Fact]
public async Task QueryAsync_WhenNaiveAndOnlyNeedContext_ReturnsNaiveContextWithoutKeywordExtraction()
{
    var llm = Substitute.For<ILLMService>();
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "vector chunk",
        Metadata = new() { ["file_path"] = "docs/a.md" }
    });
    var naive = new NaiveQueryService(vectorStore, Substitute.For<IRerankService>(), new FakeTokenizer());
    var rag = CreateLightRag(llm, naive);

    var result = await rag.QueryAsync(
        "alpha",
        new QueryParam { Mode = QueryMode.Naive, OnlyNeedContext = true, EnableRerank = false });

    result.Content.Should().Contain("vector chunk");
    await llm.DidNotReceive().ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
    await llm.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>>(), Arg.Any<float>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task QueryAsync_WhenNaiveAndOnlyNeedPrompt_ReturnsPromptWithUserQuery()
{
    var llm = Substitute.For<ILLMService>();
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "prompt chunk",
        Metadata = new() { ["file_path"] = "docs/a.md" }
    });
    var naive = new NaiveQueryService(vectorStore, Substitute.For<IRerankService>(), new FakeTokenizer());
    var rag = CreateLightRag(llm, naive);

    var result = await rag.QueryAsync(
        "alpha",
        new QueryParam { Mode = QueryMode.Naive, OnlyNeedPrompt = true, EnableRerank = false });

    result.Content.Should().Contain("prompt chunk");
    result.Content.Should().Contain("---User Query---");
    result.Content.Should().Contain("alpha");
    await llm.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>>(), Arg.Any<float>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task QueryAsync_WhenNaive_GeneratesAnswerWithNaivePrompt()
{
    var llm = Substitute.For<ILLMService>();
    llm.GenerateAsync(
            "alpha",
            Arg.Is<string?>(prompt => prompt != null && prompt.Contains("answer chunk")),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>>(),
            0.3f,
            false,
            Arg.Any<CancellationToken>())
        .Returns("naive answer");
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "answer chunk",
        Metadata = new() { ["file_path"] = "docs/a.md" }
    });
    var naive = new NaiveQueryService(vectorStore, Substitute.For<IRerankService>(), new FakeTokenizer());
    var rag = CreateLightRag(llm, naive);

    var result = await rag.QueryAsync(
        "alpha",
        new QueryParam { Mode = QueryMode.Naive, EnableRerank = false });

    result.Content.Should().Be("naive answer");
    result.RawData.Should().NotBeNull();
}

[Fact]
public async Task QueryAsync_WhenNaiveAndStream_ReturnsStreamingIterator()
{
    var llm = Substitute.For<ILLMService>();
    llm.GenerateStreamAsync(
            "alpha",
            Arg.Is<string?>(prompt => prompt != null && prompt.Contains("stream chunk")),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>>(),
            0.3f,
            false,
            Arg.Any<CancellationToken>())
        .Returns(Stream("part-a", "part-b"));
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Content = "stream chunk",
        Metadata = new() { ["file_path"] = "docs/a.md" }
    });
    var naive = new NaiveQueryService(vectorStore, Substitute.For<IRerankService>(), new FakeTokenizer());
    var rag = CreateLightRag(llm, naive);

    var result = await rag.QueryAsync(
        "alpha",
        new QueryParam { Mode = QueryMode.Naive, Stream = true, EnableRerank = false });

    result.IsStreaming.Should().BeTrue();
    var parts = new List<string>();
    await foreach (var part in result.ResponseIterator!)
    {
        parts.Add(part);
    }
    parts.Should().Equal("part-a", "part-b");
}

private static async IAsyncEnumerable<string> Stream(params string[] parts)
{
    foreach (var part in parts)
    {
        yield return part;
        await Task.Yield();
    }
}
```

- [ ] **Step 5: Add Naive branch**

At the beginning of `QueryAsync`, after Bypass:

```csharp
if (queryParam.Mode == QueryMode.Naive)
{
    return await RunNaiveQueryAsync(query, queryParam, cancellationToken);
}
```

Add helper:

```csharp
private async Task<QueryResult> RunNaiveQueryAsync(
    string query,
    QueryParam queryParam,
    CancellationToken cancellationToken)
{
    var contextResult = await naiveQueryService.BuildContextAsync(query, queryParam, cancellationToken);
    if (contextResult is null)
    {
        logger.LogInformation("No naive query context could be built");
        return new QueryResult
        {
            Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
        };
    }

    if (queryParam is { OnlyNeedContext: true, OnlyNeedPrompt: false })
    {
        return new QueryResult
        {
            Content = contextResult.Context,
            RawData = contextResult.RawData
        };
    }

    var systemPrompt = NaiveQueryPromptBuilder.BuildResponsePrompt(contextResult, queryParam);
    if (queryParam.OnlyNeedPrompt)
    {
        return new QueryResult
        {
            Content = $"{systemPrompt}\n\n---User Query---\n{query}",
            RawData = contextResult.RawData
        };
    }

    if (queryParam.Stream)
    {
        return new QueryResult
        {
            ResponseIterator = llmService.GenerateStreamAsync(
                query,
                systemPrompt,
                historyMessages: queryParam.ConversationHistory,
                temperature: 0.3f,
                cancellationToken: cancellationToken),
            RawData = contextResult.RawData,
            IsStreaming = true
        };
    }

    var response = await llmService.GenerateAsync(
        query,
        systemPrompt,
        historyMessages: queryParam.ConversationHistory,
        temperature: 0.3f,
        cancellationToken: cancellationToken);

    return new QueryResult
    {
        Content = response,
        RawData = contextResult.RawData
    };
}
```

Use the shared `NaiveQueryPromptBuilder` from Task 3. Do not add a second prompt helper in `LightRAG.cs`; the token budget and final generation prompt must stay coupled.

- [ ] **Step 6: Register constructor dependency and update direct constructors**

The normal DI path is already covered by Task 3 registration:

```csharp
services.AddSingleton<NaiveQueryService>();
services.AddSingleton<LightRAG>();
```

Update direct test constructors in:

- `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`
- `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`

Add `using LightRAGNet.Services.Query;` if missing, then insert this after `retrievalContextService` in each `new LightRAG(...)` call:

```csharp
new NaiveQueryService(
    vectorStore,
    rerankService,
    tokenizer),
```

- [ ] **Step 7: Verify GREEN and commit**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryModeTests
git add src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests/Query/LightRAGQueryModeTests.cs
git commit -m "feat: route naive and bypass queries"
```

---

## Task 5: Raw Data Parity for KG Context

**Spec coverage:** KG raw data exposes entities, relationships, chunks, references, nested keywords, and processing info.

**Files:**

- Modify: `src/LightRAGNet.Core/Models/QueryResult.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
- Test: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs`
- Test: `tests/LightRAGNet.Tests/Query/QueryResultReferenceListTests.cs`

- [ ] **Step 1: Write focused raw data unit test**

Create `RetrievalContextServiceRawDataTests.cs`. Use a substitute `IVectorStore` and `IGraphStore` to return one local entity and one chunk. Keep the test narrow: it asserts raw data shape, not ranking quality.

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Storage;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextServiceRawDataTests
{
    [Fact]
    public async Task BuildQueryContextAsync_WhenKgResultsExist_IncludesStructuredRawData()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore.QueryAsync("entities", "alpha", Arg.Any<int>(), Arg.Any<float[]?>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns([
                new SearchResult
                {
                    Id = "entity-alpha",
                    Content = "Alpha entity",
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = "Alpha",
                        ["entity_type"] = "Concept",
                        ["description"] = "Alpha description",
                        ["source_id"] = "chunk-a"
                    }
                }
            ]);
        var graphStore = Substitute.For<IGraphStore>();
        graphStore.GetNodesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, GraphNode>
            {
                ["Alpha"] = new()
                {
                    Id = "Alpha",
                    Properties = new Dictionary<string, object>
                    {
                        ["entity_type"] = "Concept",
                        ["description"] = "Alpha description",
                        ["source_id"] = "chunk-a"
                    }
                }
            });
        graphStore.GetNodeDegreesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["Alpha"] = 1 });
        graphStore.GetNodesEdgesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, List<(string SourceId, string TargetId)>>());
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f]);
        var textChunks = new InMemoryKvStore();
        await textChunks.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["chunk-a"] = new()
            {
                ["content"] = "chunk content",
                ["file_path"] = "docs/a.md"
            }
        });
        var service = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            textChunks,
            Options.Create(new LightRAGOptions { KgChunkPickMethod = "VECTOR" }),
            NullLoggerFactory.Instance);

        var result = await service.BuildQueryContextAsync(
            "alpha",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam { Mode = QueryMode.Local, EnableRerank = false },
            CancellationToken.None);

        result.Should().NotBeNull();
        var data = result!.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        data.Should().ContainKeys("entities", "relationships", "chunks", "references");

        var metadata = result.RawData["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;
        metadata["query_mode"].Should().Be("Local");
        metadata["keywords"].Should().BeAssignableTo<Dictionary<string, object>>();
        metadata["processing_info"].Should().BeAssignableTo<Dictionary<string, object>>();
    }
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RetrievalContextServiceRawDataTests
```

Expected RED:

```text
Expected dictionary to contain key "entities"
```

- [ ] **Step 3: Enrich KG raw data**

Replace the current raw data block in `BuildQueryContextAsync` with:

```csharp
var rawData = new Dictionary<string, object>
{
    ["data"] = new Dictionary<string, object>
    {
        ["entities"] = searchResult.Entities.Select(entity => new Dictionary<string, object>
        {
            ["entity_name"] = entity.Name,
            ["entity_type"] = entity.Type,
            ["description"] = entity.Description,
            ["rank"] = entity.Rank,
            ["source_id"] = entity.SourceId ?? string.Empty,
            ["file_path"] = entity.FilePath ?? string.Empty
        }).ToList(),
        ["relationships"] = searchResult.Relations.Select(relation => new Dictionary<string, object>
        {
            ["src_id"] = relation.SourceId,
            ["tgt_id"] = relation.TargetId,
            ["keywords"] = relation.Keywords,
            ["description"] = relation.Description,
            ["rank"] = relation.Rank,
            ["weight"] = relation.Weight,
            ["source_id"] = relation.RSourceId ?? string.Empty
        }).ToList(),
        ["chunks"] = searchResult.Chunks.Select(chunk => new Dictionary<string, object>
        {
            ["chunk_id"] = chunk.ChunkId,
            ["content"] = chunk.Content,
            ["file_path"] = chunk.FilePath,
            ["reference_id"] = chunk.ReferenceId
        }).ToList(),
        ["references"] = searchResult.References.Select((reference, i) => new Dictionary<string, object>
        {
            ["reference_id"] = string.IsNullOrEmpty(reference.ReferenceId) ? (i + 1).ToString() : reference.ReferenceId,
            ["file_path"] = reference.FilePath
        }).ToList()
    },
    ["metadata"] = new Dictionary<string, object>
    {
        ["query_mode"] = queryParam.Mode.ToString(),
        ["high_level_keywords"] = keywords.HighLevelKeywords,
        ["low_level_keywords"] = keywords.LowLevelKeywords,
        ["keywords"] = new Dictionary<string, object>
        {
            ["high_level"] = keywords.HighLevelKeywords,
            ["low_level"] = keywords.LowLevelKeywords
        },
        ["processing_info"] = new Dictionary<string, object>
        {
            ["total_entities_found"] = searchResult.Entities.Count,
            ["total_relations_found"] = searchResult.Relations.Count,
            ["final_chunks_count"] = searchResult.Chunks.Count
        }
    }
};
```

- [ ] **Step 4: Add `QueryResult.ReferenceList` compatibility test**

Create `QueryResultReferenceListTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Query;

public sealed class QueryResultReferenceListTests
{
    [Fact]
    public void ReferenceList_WhenReferencesAreDictionaryList_ReturnsReferences()
    {
        var result = new QueryResult
        {
            RawData = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["references"] = new List<Dictionary<string, object>>
                    {
                        new()
                        {
                            ["reference_id"] = "1",
                            ["file_path"] = "docs/a.md"
                        }
                    }
                }
            }
        };

        result.ReferenceList.Should().ContainSingle(reference =>
            reference.ReferenceId == "1" &&
            reference.FilePath == "docs/a.md");
    }
}
```

Verify RED:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~QueryResultReferenceListTests
```

Expected RED:

```text
Expected result.ReferenceList to contain a single item, but the collection is empty.
```

- [ ] **Step 5: Make `ReferenceList` read both reference shapes**

Replace the `ReferenceList` property in `QueryResult.cs` with:

```csharp
public List<ReferenceItem> ReferenceList
{
    get
    {
        if (RawData?.TryGetValue("data", out var data) != true ||
            data is not Dictionary<string, object> dataDict ||
            dataDict.TryGetValue("references", out var refs) != true)
        {
            return [];
        }

        if (refs is IEnumerable<Dictionary<string, object>> dictionaryRefs)
        {
            return dictionaryRefs.Select(ToReferenceItem).ToList();
        }

        if (refs is IEnumerable<object> objectRefs)
        {
            return objectRefs
                .OfType<Dictionary<string, object>>()
                .Select(ToReferenceItem)
                .ToList();
        }

        return [];
    }
}

private static ReferenceItem ToReferenceItem(Dictionary<string, object> reference)
{
    return new ReferenceItem
    {
        ReferenceId = reference.GetValueOrDefault("reference_id")?.ToString() ?? string.Empty,
        FilePath = reference.GetValueOrDefault("file_path")?.ToString() ?? string.Empty
    };
}
```

- [ ] **Step 6: Verify GREEN and commit**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalContextServiceRawDataTests|FullyQualifiedName~QueryResultReferenceListTests"
git add src/LightRAGNet.Core/Models/QueryResult.cs src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs tests/LightRAGNet.Tests/Query/QueryResultReferenceListTests.cs
git commit -m "feat: enrich query raw data"
```

---

## Task 6: Full Verification and Plan Closure

**Spec coverage:** normal suite green, no Docker dependency, no unrelated churn.

- [ ] **Step 1: Associated-impact review**

Before running final tests, inspect the associated consumers and verify the implementation did not expand the public surface unintentionally:

```powershell
rg -n "new LightRAG\(|AddSingleton<LightRAG>|QueryParam|QueryMode|ReferenceList|IncludeReferences|RagQueryController|QueryRagAsync" src tests -g "*.cs" -g "*.razor"
```

Expected findings:

- all direct `new LightRAG(...)` call sites include `NaiveQueryService`
- `ServiceCollectionExtensions` registers `NaiveQueryService` before `LightRAG`
- `RagQueryController` still uses default `Mix` mode and streaming unless this phase explicitly adds tests for a controller behavior change
- `LightRAGNet.Web` SSE chat code does not need changes because it consumes text chunks only
- `LightRAGNet.Example` can still read references through `QueryResult.ReferenceList`
- `IncludeReferences` remains untouched

- [ ] **Step 2: Run targeted tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Query|FullyQualifiedName~RetrievalContext"
```

Expected:

```text
Passed
```

- [ ] **Step 3: Run full tests**

```powershell
dotnet test .\LightRAGNet.slnx --no-restore
```

Expected:

```text
Passed
```

- [ ] **Step 4: Run build**

If Visual Studio is not locking outputs:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore
```

If outputs are locked:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --artifacts-path "$env:TEMP\LightRAGNet-query-mode-artifacts"
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 5: Run spec-plan traceability grep**

```powershell
rg -n "Bypass|Naive|keyword|raw data|ReferenceList|OnlyNeedContext|OnlyNeedPrompt|KGSearchStrategyFactory|RetrievalContextService|NaiveQueryPromptBuilder|Associated Impact|IncludeReferences|RagQueryController" docs/superpowers/specs/2026-05-19-query-mode-context-parity-design.md docs/superpowers/plans/2026-05-19-query-mode-context-parity-implementation-plan.md
```

Expected: every spec term has a matching plan task.

- [ ] **Step 6: Run asset completion gate**

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "query mode context parity" --json
```

Expected: if implementation is complete, archive coverage is required. Write or update an archive before final handoff if the gate reports missing completed-topic archive coverage.

- [ ] **Step 7: Final commit**

```powershell
git status --short
git add src tests docs/superpowers
git commit -m "feat: align query mode context behavior"
```

## Self-Review Checklist

- `Bypass` cannot call keyword extraction, retrieval context, vector search, graph search, rerank, or tokenizer.
- `Naive` cannot call keyword extraction or graph search.
- KG modes still call keyword extraction when keywords are not supplied.
- Empty extracted KG keywords follow the Python short-query fallback.
- Long empty-keyword KG queries return the existing no-context fail response.
- `Naive` and `Bypass` do not reach `KGSearchStrategyFactory`.
- Direct `RetrievalContextService` calls with `Naive` or `Bypass` throw a clear exception.
- Raw data contains `data.entities`, `data.relationships`, `data.chunks`, `data.references`, `metadata.keywords`, and `metadata.processing_info`.
- API/Web chat behavior is not accidentally expanded beyond default Mix streaming.
- `IncludeReferences` semantics are unchanged.
- Normal tests do not require Qdrant, Neo4j, or Docker.
- No API keys, local credentials, or generated runtime data are committed.
