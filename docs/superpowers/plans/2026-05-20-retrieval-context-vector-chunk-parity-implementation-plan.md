# Retrieval Context Vector Chunk Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Python-aligned `VECTOR` selection for KG related entity/relation chunks in `RetrievalContextService`.

**Architecture:** Keep the production change inside `RetrievalContextService`: entity and relation related-chunk paths keep their existing source-id collection, deduplication, and `WEIGHT` fallback, but call a shared vector-similarity helper when `KgChunkPickMethod == "VECTOR"` and a query embedding is available. Tests use in-memory graph, KV, and vector stores with deterministic vectors and raw-data chunk assertions.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, existing `InMemoryVectorStore`, existing `InMemoryGraphStore`, existing `InMemoryKvStore`.

---

## Scope Guard

This plan implements the approved spec:

- Spec: `docs/superpowers/specs/2026-05-20-retrieval-context-vector-chunk-parity-design.md`

Do not change these areas in this plan:

- `src/LightRAGNet/Services/QueryCache/`
- `src/LightRAGNet/Services/DocumentProcessing/`
- `src/LightRAGNet/Services/KnowledgeGraphMerge/`
- `src/LightRAGNet/LightRAG.cs`
- Server/API/Web UI files
- indexing LLM cache, extract cache, summary cache, `llm_cache_list`, deletion cleanup, or storage repair flows

## File Structure

Modify:

- `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
  - Add `GetByIdsCalls` tracking so retrieval tests can prove `WEIGHT` does not read chunk vectors.
- `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStoreTests.cs`
  - Cover `GetByIdsAsync` call tracking and clone behavior.
- `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
  - Replace the current `VECTOR` warning/fallback branches with Python-aligned vector similarity selection.
  - Add private helper methods for candidate selection and cosine similarity.

Create:

- `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`
  - Focused parity tests for entity vector selection, relation vector selection, entity/relation deduplication, fallback, and `WEIGHT` isolation.

---

### Task 1: Track Vector Batch Reads in InMemoryVectorStore

**Files:**
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStoreTests.cs`

- [ ] **Step 1: Write failing test for `GetByIdsAsync` call tracking**

Add this test to `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStoreTests.cs`:

```csharp
[Fact]
public async Task InMemoryVectorStore_GetByIdsAsync_RecordsCallsAndReturnsDeepClones()
{
    var store = new InMemoryVectorStore();
    store.Seed("chunks", new VectorDocument
    {
        Id = "chunk-a",
        Vector = [1.0f, 0.0f],
        Metadata = new Dictionary<string, object>
        {
            ["file_path"] = "docs/a.md"
        },
        Content = "chunk content"
    });

    var firstRead = await store.GetByIdsAsync("chunks", ["chunk-a", "missing"]);
    firstRead[0].Vector[0] = 99.0f;
    firstRead[0].Metadata["file_path"] = "changed.md";

    var secondRead = await store.GetByIdsAsync("chunks", ["chunk-a"]);

    store.GetByIdsCalls.Should().BeEquivalentTo([
        ("chunks", new[] { "chunk-a", "missing" }),
        ("chunks", new[] { "chunk-a" })
    ]);
    secondRead.Should().ContainSingle();
    secondRead[0].Vector.Should().Equal(1.0f, 0.0f);
    secondRead[0].Metadata["file_path"].Should().Be("docs/a.md");
}
```

- [ ] **Step 2: Run the test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~InMemoryVectorStore_GetByIdsAsync_RecordsCallsAndReturnsDeepClones --verbosity minimal
```

Expected: fail because `InMemoryVectorStore.GetByIdsCalls` does not exist.

- [ ] **Step 3: Implement `GetByIdsCalls` tracking**

In `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`, add the property beside the existing call lists:

```csharp
public List<(string Collection, IReadOnlyList<string> Ids)> GetByIdsCalls { get; } = [];
```

Update `GetByIdsAsync`:

```csharp
public Task<List<VectorDocument>> GetByIdsAsync(
    string collection,
    IEnumerable<string> ids,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    var idsList = ids.ToList();
    GetByIdsCalls.Add((collection, idsList));

    var documents = idsList
        .Select(id => Get(collection, id))
        .OfType<VectorDocument>()
        .ToList();

    return Task.FromResult(documents);
}
```

- [ ] **Step 4: Run test and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~InMemoryVectorStore_GetByIdsAsync_RecordsCallsAndReturnsDeepClones --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add tests\LightRAGNet.Tests\TestDoubles\InMemoryVectorStore.cs tests\LightRAGNet.Tests\TestDoubles\InMemoryVectorStoreTests.cs
git commit -m "test: track vector store batch reads"
```

---

### Task 2: Add Entity VECTOR Related Chunk Parity

**Files:**
- Create: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`

- [ ] **Step 1: Create failing entity vector selection test**

Create `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs` with this content:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextVectorChunkParityTests
{
    private const string Sep = "<SEP>";

    [Fact]
    public async Task BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid",
            ["file_path"] = "docs/alpha.md"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Ids.Order().SequenceEqual(new[] { "chunk-far", "chunk-mid", "chunk-near" }.Order()));
    }

    private static TestHarness CreateHarness(LightRAGOptions? options = null)
    {
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.0f]);

        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        var service = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            textChunks,
            Options.Create(options ?? new LightRAGOptions
            {
                KgChunkPickMethod = "VECTOR",
                RelatedChunkNumber = 4
            }),
            NullLoggerFactory.Instance);

        return new TestHarness(service, vectorStore, graphStore, textChunks);
    }

    private static async Task SeedTextChunksAsync(
        InMemoryKvStore textChunks,
        IEnumerable<(string Id, string Content, string FilePath)> chunks)
    {
        await textChunks.UpsertAsync(chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => new Dictionary<string, object>
            {
                ["content"] = chunk.Content,
                ["file_path"] = chunk.FilePath
            }));
    }

    private static void SeedChunkVectors(
        InMemoryVectorStore vectorStore,
        IEnumerable<(string Id, float[] Vector)> chunks)
    {
        foreach (var (id, vector) in chunks)
        {
            vectorStore.Seed("chunks", new VectorDocument
            {
                Id = id,
                Vector = vector,
                Content = $"{id} content",
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["file_path"] = $"docs/{id}.md"
                }
            });
        }
    }

    private static List<string> GetChunkIds(QueryContextResult result)
    {
        var data = result.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var chunks = data["chunks"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        return chunks.Select(chunk => chunk["chunk_id"].ToString()!).ToList();
    }

    private sealed record TestHarness(
        RetrievalContextService Service,
        InMemoryVectorStore VectorStore,
        InMemoryGraphStore GraphStore,
        InMemoryKvStore TextChunks);
}
```

- [ ] **Step 2: Run the entity test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity --verbosity minimal
```

Expected: fail because current `VECTOR` path falls back to `WEIGHT`, returning `chunk-far`, `chunk-near`, `chunk-mid` instead of `chunk-near`, `chunk-mid`.

- [ ] **Step 3: Add vector similarity helper methods**

In `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`, add these helpers immediately before `PickByWeightedPolling`:

```csharp
private async Task<List<string>> PickByVectorSimilarityAsync(
    IReadOnlyCollection<List<string>> sortedChunkGroups,
    int numOfChunks,
    float[] queryEmbedding,
    CancellationToken cancellationToken)
{
    if (sortedChunkGroups.Count == 0 || numOfChunks <= 0 || queryEmbedding.Length == 0)
    {
        return [];
    }

    var uniqueChunkIds = sortedChunkGroups
        .SelectMany(group => group)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.Ordinal)
        .ToList();

    if (uniqueChunkIds.Count == 0)
    {
        return [];
    }

    try
    {
        var documents = await vectorStore.GetByIdsAsync("chunks", uniqueChunkIds, cancellationToken);
        if (documents.Count != uniqueChunkIds.Count)
        {
            _logger.LogWarning(
                "Vector chunk selection expected {ExpectedCount} vectors but found {ActualCount}. Falling back to WEIGHT.",
                uniqueChunkIds.Count,
                documents.Count);
            return [];
        }

        var vectorsById = documents.ToDictionary(document => document.Id, StringComparer.Ordinal);
        var similarities = new List<(string ChunkId, double Similarity)>();
        foreach (var chunkId in uniqueChunkIds)
        {
            if (!vectorsById.TryGetValue(chunkId, out var document) ||
                !TryCosineSimilarity(queryEmbedding, document.Vector, out var similarity))
            {
                _logger.LogWarning(
                    "Vector chunk selection could not compute similarity for chunk {ChunkId}. Falling back to WEIGHT.",
                    chunkId);
                return [];
            }

            similarities.Add((chunkId, similarity));
        }

        return similarities
            .OrderByDescending(item => item.Similarity)
            .Take(numOfChunks)
            .Select(item => item.ChunkId)
            .ToList();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogWarning(ex, "Vector chunk selection failed. Falling back to WEIGHT.");
        return [];
    }
}

private static bool TryCosineSimilarity(float[] left, float[] right, out double similarity)
{
    similarity = 0;
    if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
    {
        return false;
    }

    double dot = 0;
    double leftNorm = 0;
    double rightNorm = 0;
    for (var i = 0; i < left.Length; i++)
    {
        dot += left[i] * right[i];
        leftNorm += left[i] * left[i];
        rightNorm += right[i] * right[i];
    }

    if (leftNorm <= 0 || rightNorm <= 0)
    {
        return false;
    }

    similarity = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    return true;
}
```

- [ ] **Step 4: Wire entity `VECTOR` branch**

In `FindRelatedTextUnitFromEntitiesAsync`, replace the current `VECTOR` warning block:

```csharp
if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
{
    // VECTOR mode: use vector similarity selection (simplified implementation, use WEIGHT as fallback)
    _logger.LogWarning("VECTOR chunk pick method not fully implemented, falling back to WEIGHT");
    kgChunkPickMethod = "WEIGHT";
}
```

with:

```csharp
if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
{
    var numOfChunks = (int)(maxRelatedChunks * sortedEntities.Count / 2.0);
    selectedChunkIds = await PickByVectorSimilarityAsync(
        sortedEntities.Select(entity => entity.SortedChunks).ToList(),
        numOfChunks,
        queryEmbedding,
        cancellationToken);

    if (selectedChunkIds.Count == 0)
    {
        _logger.LogWarning("No entity-related chunks selected by vector similarity, falling back to WEIGHT method");
        kgChunkPickMethod = "WEIGHT";
    }
    else
    {
        _logger.LogInformation(
            "Selecting {SelectedCount} from {TotalCount} entity-related chunks by vector similarity",
            selectedChunkIds.Count,
            totalEntityChunks);
    }
}
```

- [ ] **Step 5: Run the entity test and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet\Services\RetrievalContext\RetrievalContextService.cs tests\LightRAGNet.Tests\RetrievalContext\RetrievalContextVectorChunkParityTests.cs
git commit -m "feat: select entity chunks by vector similarity"
```

---

### Task 3: Add Relation VECTOR Selection and Entity Deduplication

**Files:**
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`

- [ ] **Step 1: Add failing relation vector selection test**

Add this test to `RetrievalContextVectorChunkParityTests`:

```csharp
[Fact]
public async Task BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsRelationChunksByCosineSimilarity()
{
    var harness = CreateHarness();
    SeedGlobalRelation(harness, relationSourceId: $"chunk-far{Sep}chunk-near{Sep}chunk-mid");
    await SeedTextChunksAsync(harness.TextChunks, [
        ("chunk-far", "far relation content", "docs/far.md"),
        ("chunk-near", "near relation content", "docs/near.md"),
        ("chunk-mid", "mid relation content", "docs/mid.md")
    ]);
    SeedChunkVectors(harness.VectorStore, [
        ("chunk-far", new[] { 0.0f, 1.0f }),
        ("chunk-near", new[] { 1.0f, 0.0f }),
        ("chunk-mid", new[] { 0.8f, 0.6f })
    ]);

    var result = await harness.Service.BuildQueryContextAsync(
        "relation question",
        new KeywordsResult { HighLevelKeywords = ["relation"] },
        new QueryParam
        {
            Mode = QueryMode.Global,
            EnableRerank = false,
            TopK = 5,
            MaxTotalTokens = 30000
        });

    result.Should().NotBeNull();
    GetChunkIds(result!).Should().Equal("chunk-near", "chunk-mid");
}
```

Add this helper inside the test class:

```csharp
private static void SeedGlobalRelation(TestHarness harness, string relationSourceId)
{
    harness.VectorStore.Seed("relationships", new VectorDocument
    {
        Id = "rel-alpha-beta",
        Metadata = new Dictionary<string, object>
        {
            ["src_id"] = "Alpha",
            ["tgt_id"] = "Beta"
        },
        Content = "Alpha Beta relation"
    });
    harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Alpha description"
    });
    harness.GraphStore.SeedNode("Beta", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Beta description"
    });
    harness.GraphStore.SeedEdge("Alpha", "Beta", new Dictionary<string, object>
    {
        ["keywords"] = "relation",
        ["description"] = "Alpha relates to Beta",
        ["weight"] = 1.0d,
        ["source_id"] = relationSourceId
    });
}
```

- [ ] **Step 2: Run relation test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsRelationChunksByCosineSimilarity --verbosity minimal
```

Expected: fail because relation `VECTOR` path still falls back to `WEIGHT`.

- [ ] **Step 3: Wire relation `VECTOR` branch**

In `FindRelatedTextUnitFromRelationsAsync`, replace the current `VECTOR` warning block:

```csharp
if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
{
    // VECTOR mode: use vector similarity selection (simplified implementation, use WEIGHT as fallback)
    _logger.LogWarning("VECTOR chunk pick method not fully implemented, falling back to WEIGHT");
    kgChunkPickMethod = "WEIGHT";
}
```

with:

```csharp
if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
{
    var numOfChunks = (int)(maxRelatedChunks * sortedRelations.Count / 2.0);
    selectedChunkIds = await PickByVectorSimilarityAsync(
        sortedRelations.Select(relation => relation.SortedChunks).ToList(),
        numOfChunks,
        queryEmbedding,
        cancellationToken);

    if (selectedChunkIds.Count == 0)
    {
        _logger.LogWarning("No relation-related chunks selected by vector similarity, falling back to WEIGHT method");
        kgChunkPickMethod = "WEIGHT";
    }
    else
    {
        _logger.LogInformation(
            "Selecting {SelectedCount} from {TotalCount} relation-related chunks by vector similarity",
            selectedChunkIds.Count,
            totalRelationChunks);
    }
}
```

- [ ] **Step 4: Run relation test and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsRelationChunksByCosineSimilarity --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Add entity/relation deduplication test**

Add this test to `RetrievalContextVectorChunkParityTests`:

```csharp
[Fact]
public async Task BuildQueryContextAsync_WhenRelationVectorChunksOverlapEntityChunks_ExcludesEntityChunks()
{
    var harness = CreateHarness();
    harness.VectorStore.Seed("relationships", new VectorDocument
    {
        Id = "rel-alpha-beta",
        Metadata = new Dictionary<string, object>
        {
            ["src_id"] = "Alpha",
            ["tgt_id"] = "Beta"
        },
        Content = "Alpha Beta relation"
    });
    harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Alpha description",
        ["source_id"] = "chunk-entity"
    });
    harness.GraphStore.SeedNode("Beta", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Beta description"
    });
    harness.GraphStore.SeedEdge("Alpha", "Beta", new Dictionary<string, object>
    {
        ["keywords"] = "relation",
        ["description"] = "Alpha relates to Beta",
        ["weight"] = 1.0d,
        ["source_id"] = $"chunk-entity{Sep}chunk-relation-far{Sep}chunk-relation-near"
    });
    await SeedTextChunksAsync(harness.TextChunks, [
        ("chunk-entity", "entity content", "docs/entity.md"),
        ("chunk-relation-far", "far relation content", "docs/far.md"),
        ("chunk-relation-near", "near relation content", "docs/near.md")
    ]);
    SeedChunkVectors(harness.VectorStore, [
        ("chunk-entity", new[] { 1.0f, 0.0f }),
        ("chunk-relation-far", new[] { 0.0f, 1.0f }),
        ("chunk-relation-near", new[] { 0.9f, 0.1f })
    ]);

    var result = await harness.Service.BuildQueryContextAsync(
        "relation question",
        new KeywordsResult { HighLevelKeywords = ["relation"] },
        new QueryParam
        {
            Mode = QueryMode.Global,
            EnableRerank = false,
            TopK = 5,
            MaxTotalTokens = 30000
        });

    result.Should().NotBeNull();
    GetChunkIds(result!).Should().Equal("chunk-entity", "chunk-relation-near", "chunk-relation-far");
    GetChunkIds(result!).Should().Contain("chunk-entity").Which.Should().Be("chunk-entity");
    GetChunkIds(result!).Should().OnlyHaveUniqueItems();
}
```

- [ ] **Step 6: Run deduplication test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenRelationVectorChunksOverlapEntityChunks_ExcludesEntityChunks --verbosity minimal
```

Expected: pass after relation `VECTOR` implementation. If it fails by returning `chunk-relation-far` before `chunk-relation-near`, inspect the relation vector branch and verify it calls `PickByVectorSimilarityAsync`.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet\Services\RetrievalContext\RetrievalContextService.cs tests\LightRAGNet.Tests\RetrievalContext\RetrievalContextVectorChunkParityTests.cs
git commit -m "feat: select relation chunks by vector similarity"
```

---

### Task 4: Preserve Fallback and WEIGHT Behavior

**Files:**
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs`

- [ ] **Step 1: Add fallback test for missing vectors**

Add this test to `RetrievalContextVectorChunkParityTests`:

```csharp
[Fact]
public async Task BuildQueryContextAsync_WhenChunkVectorMissing_FallsBackToWeightedPolling()
{
    var harness = CreateHarness();
    harness.VectorStore.Seed("entities", new VectorDocument
    {
        Id = "entity-alpha",
        Metadata = new Dictionary<string, object>
        {
            ["entity_name"] = "Alpha"
        },
        Content = "Alpha entity"
    });
    harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Alpha description",
        ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid"
    });
    await SeedTextChunksAsync(harness.TextChunks, [
        ("chunk-far", "far content", "docs/far.md"),
        ("chunk-near", "near content", "docs/near.md"),
        ("chunk-mid", "mid content", "docs/mid.md")
    ]);
    SeedChunkVectors(harness.VectorStore, [
        ("chunk-near", new[] { 1.0f, 0.0f })
    ]);

    var result = await harness.Service.BuildQueryContextAsync(
        "alpha question",
        new KeywordsResult { LowLevelKeywords = ["alpha"] },
        new QueryParam
        {
            Mode = QueryMode.Local,
            EnableRerank = false,
            TopK = 5,
            MaxTotalTokens = 30000
        });

    result.Should().NotBeNull();
    GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
    harness.VectorStore.GetByIdsCalls.Should().ContainSingle(call => call.Collection == "chunks");
}
```

- [ ] **Step 2: Run fallback test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~BuildQueryContextAsync_WhenChunkVectorMissing_FallsBackToWeightedPolling --verbosity minimal
```

Expected: pass. This test proves incomplete chunk vector data does not fail the query and degrades to `WEIGHT`.

- [ ] **Step 3: Add WEIGHT isolation test**

Add this test to `RetrievalContextVectorChunkParityTests`:

```csharp
[Fact]
public async Task BuildQueryContextAsync_WhenKgChunkPickMethodWeight_DoesNotReadChunkVectors()
{
    var harness = CreateHarness(new LightRAGOptions
    {
        KgChunkPickMethod = "WEIGHT",
        RelatedChunkNumber = 4
    });
    harness.VectorStore.Seed("entities", new VectorDocument
    {
        Id = "entity-alpha",
        Metadata = new Dictionary<string, object>
        {
            ["entity_name"] = "Alpha"
        },
        Content = "Alpha entity"
    });
    harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
    {
        ["entity_type"] = "Concept",
        ["description"] = "Alpha description",
        ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid"
    });
    await SeedTextChunksAsync(harness.TextChunks, [
        ("chunk-far", "far content", "docs/far.md"),
        ("chunk-near", "near content", "docs/near.md"),
        ("chunk-mid", "mid content", "docs/mid.md")
    ]);
    SeedChunkVectors(harness.VectorStore, [
        ("chunk-far", new[] { 0.0f, 1.0f }),
        ("chunk-near", new[] { 1.0f, 0.0f }),
        ("chunk-mid", new[] { 0.8f, 0.6f })
    ]);

    var result = await harness.Service.BuildQueryContextAsync(
        "alpha question",
        new KeywordsResult { LowLevelKeywords = ["alpha"] },
        new QueryParam
        {
            Mode = QueryMode.Local,
            EnableRerank = false,
            TopK = 5,
            MaxTotalTokens = 30000
        });

    result.Should().NotBeNull();
    GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
    harness.VectorStore.GetByIdsCalls.Should().BeEmpty();
}
```

- [ ] **Step 4: Run fallback and WEIGHT tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~BuildQueryContextAsync_WhenChunkVectorMissing_FallsBackToWeightedPolling|FullyQualifiedName~BuildQueryContextAsync_WhenKgChunkPickMethodWeight_DoesNotReadChunkVectors" --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Run all vector chunk parity tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RetrievalContextVectorChunkParityTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add tests\LightRAGNet.Tests\RetrievalContext\RetrievalContextVectorChunkParityTests.cs
git commit -m "test: cover vector chunk fallback behavior"
```

---

### Task 5: Verification and Scope Audit

**Files:**
- No planned production changes.
- Modify documentation only if implementation revealed a reviewed correction to `docs/superpowers/specs/2026-05-20-retrieval-context-vector-chunk-parity-design.md`.

- [ ] **Step 1: Run targeted retrieval context tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~RetrievalContext|FullyQualifiedName~KGSearchStrategyFactoryTests|FullyQualifiedName~ReferenceListBuilderTests|FullyQualifiedName~TokenBudgetPlannerTests|FullyQualifiedName~ChunkTokenLimiterTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 2: Run query tests that depend on retrieval context**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~LightRAGKeywordPolicyIntegrationTests|FullyQualifiedName~NaiveQueryServiceTests|FullyQualifiedName~QueryCache" --verbosity minimal
```

Expected: pass.

- [ ] **Step 3: Run lifecycle and deletion tests for accidental regression coverage**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~LightRAGLifecycleIntegrationTests|FullyQualifiedName~DocumentDeletionServiceTests|FullyQualifiedName~RagTaskProcessorServiceTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Run solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: all test projects pass.

- [ ] **Step 5: Run full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: `0 Error(s)`. If warnings exist from unrelated dependency state, record the exact warning count and message in the handoff.

- [ ] **Step 6: Audit scope boundaries**

Run:

```powershell
git diff --name-only HEAD~4..HEAD
```

Expected changed paths are limited to:

```text
src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs
tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextVectorChunkParityTests.cs
tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs
tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStoreTests.cs
```

Run:

```powershell
rg -n "VECTOR chunk pick method not fully implemented|falling back to WEIGHT\"\\)" src\LightRAGNet\Services\RetrievalContext tests\LightRAGNet.Tests\RetrievalContext
```

Expected:

- no `VECTOR chunk pick method not fully implemented` remains
- fallback log messages are only the intentional `No ... selected by vector similarity` or helper failure warnings

- [ ] **Step 7: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 8: Commit any verification/doc correction**

If no files changed during verification:

```powershell
git status --short
```

Expected: clean working tree.

If spec wording needed correction:

```powershell
git add docs\superpowers\specs\2026-05-20-retrieval-context-vector-chunk-parity-design.md
git commit -m "docs: clarify vector chunk parity scope"
```

---

## Plan Self-Review

Spec coverage:

- Entity `VECTOR` chunk selection by cosine similarity: Task 2.
- Relation `VECTOR` chunk selection by cosine similarity: Task 3.
- Relation selection excludes entity chunks: Task 3.
- Missing/incomplete vectors fallback to `WEIGHT`: Task 4.
- `WEIGHT` does not read chunk vectors: Task 4.
- No cache/index/delete/UI scope: Scope Guard and Task 5 audit.
- In-memory-only tests: Tasks 1-4.
- Verification against query/retrieval/lifecycle/deletion/server-adjacent tests: Task 5.

Placeholder scan:

- No forbidden placeholder terms or unspecified edge handling.
- Each code-changing task includes concrete code snippets, commands, and expected outcomes.

Type consistency:

- `InMemoryVectorStore.GetByIdsCalls` is introduced before retrieval tests assert it.
- Test helper uses existing `RetrievalContextService`, `InMemoryVectorStore`, `InMemoryGraphStore`, `InMemoryKvStore`, `FakeTokenizer`, `LightRAGOptions`, `KeywordsResult`, and `QueryParam`.
- Production helper uses existing `vectorStore`, `_logger`, and `CancellationToken` available in `RetrievalContextService`.
