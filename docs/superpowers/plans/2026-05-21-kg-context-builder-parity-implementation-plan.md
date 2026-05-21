# KG Context Builder Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor KG query context construction into a focused builder that emits Python-style structured context, stable `reference_id` links, and token budgeting based on the final context shape.

**Architecture:** Keep `RetrievalContextService` responsible for retrieval orchestration, search strategy selection, and related chunk selection. Add `KgQueryContextBuilder` as the formatting and final token-budget boundary; it receives already retrieved `KGSearchResult` data, truncates entities/relations/chunks using the structured output shape, emits context text, and returns the filtered data needed for raw data.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NSubstitute, existing `LightRAGNet.Services.RetrievalContext` test doubles.

---

## Scope Check

This plan covers one subsystem: KG query context formatting and token budget consistency. It does not implement rerank parity, UI changes, cache key changes, provider integration tests, or prompt perfect parity.

## File Structure

- Create: `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`
  - Owns structured KG context formatting.
  - Owns final entity/relation/chunk truncation using the context shape it emits.
  - Returns a small build result with filtered entities, relations, chunks, and references.
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
  - Replace private `BuildContextString`, `GetEntityCountByTokens`, and `GetRelationCountByTokens` usage with `KgQueryContextBuilder`.
  - Stop applying the old filename-based chunk limiter before references are assigned.
  - Build raw data from builder-filtered data.
- Create: `tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs`
  - Unit coverage for structured context, reference ids, and final-shape token limiting.
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs`
  - Integration coverage that service context, raw data chunks, and raw data references use the same `reference_id` values.
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/TokenBudgetPlannerTests.cs`
  - Keep existing planner coverage; add only if task execution decides to expose a new planner call shape. Prefer builder tests for context-specific budgeting.

## Task 1: Add Structured Context Builder

**Files:**
- Create: `tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs`
- Create: `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`

- [ ] **Step 1: Write the failing builder format test**

Create `tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs` with this initial content:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class KgQueryContextBuilderTests
{
    [Fact]
    public void Build_EmitsStructuredJsonSectionsAndReferenceIds()
    {
        var builder = new KgQueryContextBuilder(new FakeTokenizer());
        var searchResult = new KGSearchResult
        {
            Entities =
            [
                new EntityData
                {
                    Name = "ALPHA",
                    Type = "concept",
                    Description = "Alpha description",
                    Rank = 2,
                    SourceId = "chunk-a",
                    FilePath = "docs/a.md"
                }
            ],
            Relations =
            [
                new RelationData
                {
                    SourceId = "ALPHA",
                    TargetId = "BETA",
                    Keywords = "depends on",
                    Description = "Alpha depends on Beta",
                    Rank = 3,
                    Weight = 2.5d,
                    RSourceId = "chunk-b"
                }
            ],
            Chunks =
            [
                new ChunkData
                {
                    ChunkId = "chunk-a",
                    Content = "Alpha chunk content",
                    FilePath = "docs/a.md"
                },
                new ChunkData
                {
                    ChunkId = "chunk-b",
                    Content = "Beta chunk content",
                    FilePath = "docs/b.md"
                }
            ]
        };

        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxTotalTokens = 30000,
                MaxEntityTokens = 1000,
                MaxRelationTokens = 1000
            },
            query: "alpha");

        result.Context.Should().Contain("Knowledge Graph Data (Entity):");
        result.Context.Should().Contain("""{"entity":"ALPHA","type":"concept","description":"Alpha description"}""");
        result.Context.Should().Contain("Knowledge Graph Data (Relationship):");
        result.Context.Should().Contain("""{"entity1":"ALPHA","entity2":"BETA","keywords":"depends on","description":"Alpha depends on Beta"}""");
        result.Context.Should().Contain("Document Chunks (Each entry has a reference_id refer to the `Reference Document List`):");
        result.Context.Should().Contain("""{"reference_id":"1","content":"Alpha chunk content"}""");
        result.Context.Should().Contain("""{"reference_id":"2","content":"Beta chunk content"}""");
        result.Context.Should().Contain("[1] docs/a.md");
        result.Context.Should().Contain("[2] docs/b.md");
        result.Chunks.Select(chunk => chunk.ReferenceId).Should().Equal("1", "2");
        result.References.Select(reference => reference.ReferenceId).Should().Equal("1", "2");
    }
}
```

- [ ] **Step 2: Run the new test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~KgQueryContextBuilderTests --verbosity minimal
```

Expected: FAIL because `KgQueryContextBuilder` does not exist.

- [ ] **Step 3: Add the minimal builder implementation**

Create `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class KgQueryContextBuilder(ITokenizer tokenizer)
{
    private const int ReferenceAndSafetyBufferTokens = 200;
    private readonly ReferenceListBuilder _referenceListBuilder = new();

    public KgQueryContextBuildResult Build(
        KGSearchResult searchResult,
        QueryParam queryParam,
        string query)
    {
        var entities = LimitEntities(searchResult.Entities, queryParam.MaxEntityTokens);
        var relations = LimitRelations(searchResult.Relations, queryParam.MaxRelationTokens);
        var chunks = LimitChunksByFinalContext(searchResult.Chunks, entities, relations, queryParam, query);
        var (references, chunksWithRefIds) = _referenceListBuilder.Build(chunks);
        var context = BuildContext(entities, relations, chunksWithRefIds, references);

        return new KgQueryContextBuildResult(
            context,
            entities,
            relations,
            chunksWithRefIds,
            references);
    }

    private List<EntityData> LimitEntities(IEnumerable<EntityData> entities, int maxTokens)
    {
        var result = new List<EntityData>();
        var currentTokens = 0;

        foreach (var entity in entities)
        {
            var tokens = tokenizer.CountTokens(SerializeEntity(entity));
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }

            result.Add(entity);
            currentTokens += tokens;
        }

        return result;
    }

    private List<RelationData> LimitRelations(IEnumerable<RelationData> relations, int maxTokens)
    {
        var result = new List<RelationData>();
        var currentTokens = 0;

        foreach (var relation in relations)
        {
            var tokens = tokenizer.CountTokens(SerializeRelation(relation));
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }

            result.Add(relation);
            currentTokens += tokens;
        }

        return result;
    }

    private List<ChunkData> LimitChunksByFinalContext(
        IEnumerable<ChunkData> chunks,
        IReadOnlyCollection<EntityData> entities,
        IReadOnlyCollection<RelationData> relations,
        QueryParam queryParam,
        string query)
    {
        var kgContextWithoutChunks = BuildContext(
            entities,
            relations,
            [],
            []);
        var availableChunkTokens =
            queryParam.MaxTotalTokens
            - tokenizer.CountTokens(query)
            - tokenizer.CountTokens(kgContextWithoutChunks)
            - ReferenceAndSafetyBufferTokens;

        if (availableChunkTokens <= 0)
        {
            return [];
        }

        var accepted = new List<ChunkData>();
        foreach (var chunk in chunks)
        {
            var candidate = accepted.Concat([chunk]).ToList();
            var (candidateReferences, candidateChunksWithRefIds) = _referenceListBuilder.Build(candidate);
            var candidateContext = BuildChunkAndReferenceContext(candidateChunksWithRefIds, candidateReferences);
            if (tokenizer.CountTokens(candidateContext) > availableChunkTokens)
            {
                break;
            }

            accepted.Add(chunk);
        }

        return accepted;
    }

    private static string BuildContext(
        IReadOnlyCollection<EntityData> entities,
        IReadOnlyCollection<RelationData> relations,
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        var parts = new List<string>();

        if (entities.Count > 0)
        {
            parts.Add($"""
                       Knowledge Graph Data (Entity):

                       ```json
                       {string.Join('\n', entities.Select(SerializeEntity))}
                       ```
                       """);
        }

        if (relations.Count > 0)
        {
            parts.Add($"""
                       Knowledge Graph Data (Relationship):

                       ```json
                       {string.Join('\n', relations.Select(SerializeRelation))}
                       ```
                       """);
        }

        var chunkContext = BuildChunkAndReferenceContext(chunks, references);
        if (!string.IsNullOrWhiteSpace(chunkContext))
        {
            parts.Add(chunkContext);
        }

        return string.Join("\n\n", parts);
    }

    private static string BuildChunkAndReferenceContext(
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        if (chunks.Count == 0)
        {
            return string.Empty;
        }

        var chunkLines = chunks.Select(chunk => JsonSerializer.Serialize(new
        {
            reference_id = chunk.ReferenceId,
            content = chunk.Content
        }, LightRAGJsonOptions.HumanReadable));
        var referenceLines = references.Select(reference => $"[{reference.ReferenceId}] {reference.FilePath}");

        return $"""
                Document Chunks (Each entry has a reference_id refer to the `Reference Document List`):

                ```json
                {string.Join('\n', chunkLines)}
                ```

                Reference Document List (Each entry starts with a [reference_id] that corresponds to entries in the Document Chunks):

                ```
                {string.Join('\n', referenceLines)}
                ```
                """;
    }

    private static string SerializeEntity(EntityData entity)
    {
        return JsonSerializer.Serialize(new
        {
            entity = entity.Name,
            type = entity.Type,
            description = entity.Description
        }, LightRAGJsonOptions.HumanReadable);
    }

    private static string SerializeRelation(RelationData relation)
    {
        return JsonSerializer.Serialize(new
        {
            entity1 = relation.SourceId,
            entity2 = relation.TargetId,
            keywords = relation.Keywords,
            description = relation.Description
        }, LightRAGJsonOptions.HumanReadable);
    }
}

internal sealed record KgQueryContextBuildResult(
    string Context,
    List<EntityData> Entities,
    List<RelationData> Relations,
    List<ChunkData> Chunks,
    List<ReferenceItem> References);
```

- [ ] **Step 4: Run the builder test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~KgQueryContextBuilderTests --verbosity minimal
```

Expected: PASS for `Build_EmitsStructuredJsonSectionsAndReferenceIds`.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs
git commit -m "feat: add kg query context builder"
```

Expected: commit succeeds.

## Task 2: Lock Builder Token Limits

**Files:**
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`

- [ ] **Step 1: Add failing tests for structured token limits**

Append these tests to `KgQueryContextBuilderTests`:

```csharp
[Fact]
public void Build_LimitsEntitiesUsingJsonContextShape()
{
    var builder = new KgQueryContextBuilder(new FakeTokenizer());
    var searchResult = new KGSearchResult
    {
        Entities =
        [
            new EntityData { Name = "ALPHA", Type = "concept", Description = "short" },
            new EntityData { Name = "BETA", Type = "concept", Description = "second" }
        ]
    };

    var result = builder.Build(
        searchResult,
        new QueryParam
        {
            MaxEntityTokens = 4,
            MaxRelationTokens = 1000,
            MaxTotalTokens = 30000
        },
        query: "alpha");

    result.Entities.Should().ContainSingle(entity => entity.Name == "ALPHA");
    result.Context.Should().Contain("""{"entity":"ALPHA","type":"concept","description":"short"}""");
    result.Context.Should().NotContain("BETA");
}

[Fact]
public void Build_LimitsRelationshipsUsingJsonContextShape()
{
    var builder = new KgQueryContextBuilder(new FakeTokenizer());
    var searchResult = new KGSearchResult
    {
        Relations =
        [
            new RelationData
            {
                SourceId = "ALPHA",
                TargetId = "BETA",
                Keywords = "depends",
                Description = "short"
            },
            new RelationData
            {
                SourceId = "BETA",
                TargetId = "GAMMA",
                Keywords = "blocks",
                Description = "second"
            }
        ]
    };

    var result = builder.Build(
        searchResult,
        new QueryParam
        {
            MaxEntityTokens = 1000,
            MaxRelationTokens = 5,
            MaxTotalTokens = 30000
        },
        query: "alpha");

    result.Relations.Should().ContainSingle(relation => relation.SourceId == "ALPHA");
    result.Context.Should().Contain("""{"entity1":"ALPHA","entity2":"BETA","keywords":"depends","description":"short"}""");
    result.Context.Should().NotContain("GAMMA");
}

[Fact]
public void Build_WhenChunkBudgetCannotFitReferenceList_DropsChunks()
{
    var builder = new KgQueryContextBuilder(new FakeTokenizer());
    var searchResult = new KGSearchResult
    {
        Chunks =
        [
            new ChunkData
            {
                ChunkId = "chunk-a",
                Content = "alpha beta gamma delta epsilon",
                FilePath = "docs/a.md"
            }
        ]
    };

    var result = builder.Build(
        searchResult,
        new QueryParam
        {
            MaxEntityTokens = 1000,
            MaxRelationTokens = 1000,
            MaxTotalTokens = 205
        },
        query: "alpha");

    result.Chunks.Should().BeEmpty();
    result.References.Should().BeEmpty();
    result.Context.Should().NotContain("Document Chunks");
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~KgQueryContextBuilderTests --verbosity minimal
```

Expected: at least one new token-limit assertion fails if Task 1 implementation does not yet count the final structured shape strictly enough.

- [ ] **Step 3: Tighten token counting with section-aware helpers**

Modify `KgQueryContextBuilder` so entity and relation token checks count the final line plus section overhead when adding the first item. Replace `LimitEntities` and `LimitRelations` with:

```csharp
private List<EntityData> LimitEntities(IEnumerable<EntityData> entities, int maxTokens)
{
    return LimitBySection(
        entities,
        maxTokens,
        item => BuildEntitySection([item]),
        items => BuildEntitySection(items));
}

private List<RelationData> LimitRelations(IEnumerable<RelationData> relations, int maxTokens)
{
    return LimitBySection(
        relations,
        maxTokens,
        item => BuildRelationSection([item]),
        items => BuildRelationSection(items));
}

private List<T> LimitBySection<T>(
    IEnumerable<T> items,
    int maxTokens,
    Func<T, string> singleItemSectionFactory,
    Func<IReadOnlyCollection<T>, string> sectionFactory)
{
    var accepted = new List<T>();

    foreach (var item in items)
    {
        var candidate = accepted.Concat([item]).ToList();
        var candidateTokens = tokenizer.CountTokens(sectionFactory(candidate));
        if (candidateTokens > maxTokens)
        {
            if (accepted.Count == 0 && tokenizer.CountTokens(singleItemSectionFactory(item)) <= maxTokens)
            {
                accepted.Add(item);
            }

            break;
        }

        accepted.Add(item);
    }

    return accepted;
}
```

Add these section helper methods and update `BuildContext` to use them:

```csharp
private static string BuildEntitySection(IReadOnlyCollection<EntityData> entities)
{
    if (entities.Count == 0)
    {
        return string.Empty;
    }

    return $"""
            Knowledge Graph Data (Entity):

            ```json
            {string.Join('\n', entities.Select(SerializeEntity))}
            ```
            """;
}

private static string BuildRelationSection(IReadOnlyCollection<RelationData> relations)
{
    if (relations.Count == 0)
    {
        return string.Empty;
    }

    return $"""
            Knowledge Graph Data (Relationship):

            ```json
            {string.Join('\n', relations.Select(SerializeRelation))}
            ```
            """;
}
```

In `BuildContext`, replace inline entity and relation section construction with:

```csharp
var entitySection = BuildEntitySection(entities);
if (!string.IsNullOrWhiteSpace(entitySection))
{
    parts.Add(entitySection);
}

var relationSection = BuildRelationSection(relations);
if (!string.IsNullOrWhiteSpace(relationSection))
{
    parts.Add(relationSection);
}
```

- [ ] **Step 4: Run tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~KgQueryContextBuilderTests --verbosity minimal
```

Expected: all `KgQueryContextBuilderTests` pass.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git add src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs
git commit -m "test: cover kg context builder token limits"
```

Expected: commit succeeds.

## Task 3: Wire RetrievalContextService To Builder

**Files:**
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`

- [ ] **Step 1: Add failing service integration assertions**

In `BuildQueryContextAsync_WhenKgResultsExist_IncludesStructuredRawData`, after `result.Should().NotBeNull();`, add:

```csharp
result!.Context.Should().Contain("""{"entity":"Alpha","type":"Concept","description":"Alpha description"}""");
result.Context.Should().Contain("""{"entity1":"Alpha","entity2":"Beta","keywords":"depends on","description":"Alpha depends on Beta"}""");
result.Context.Should().Contain("""{"reference_id":"1","content":"chunk content"}""");
result.Context.Should().Contain("""{"reference_id":"2","content":"relationship chunk content"}""");
result.Context.Should().Contain("[1] docs/a.md");
result.Context.Should().Contain("[2] docs/b.md");
result.Context.Should().NotContain("Alpha (Concept): Alpha description");
result.Context.Should().NotContain("Alpha -> Beta: depends on - Alpha depends on Beta");
result.Context.Should().NotContain("[a.md]");
```

Because this line changes `result` to a nullable-unwrapped variable, remove the later duplicate null-forgiving expression in this method if the compiler reports it. The method should continue using `result.RawData`.

- [ ] **Step 2: Run service raw data test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RetrievalContextServiceRawDataTests --verbosity minimal
```

Expected: FAIL because the context still contains legacy text lines and filename-based chunk references.

- [ ] **Step 3: Replace service-local formatter with the builder**

In `RetrievalContextService`, add a field near the existing private fields:

```csharp
private readonly KgQueryContextBuilder _contextBuilder = new(tokenizer);
```

In `BuildQueryContextAsync`, replace:

```csharp
var context = BuildContextString(searchResult, queryParam);

var rawData = BuildRawData(searchResult, keywords, queryParam);
```

with:

```csharp
var contextResult = _contextBuilder.Build(searchResult, queryParam, query);
if (string.IsNullOrWhiteSpace(contextResult.Context))
{
    return null;
}

var rawData = BuildRawData(contextResult, keywords, queryParam);
```

Replace the return object assignment with:

```csharp
return new QueryContextResult
{
    Context = contextResult.Context,
    RawData = rawData
};
```

Change `BuildRawData` signature:

```csharp
private static Dictionary<string, object> BuildRawData(
    KgQueryContextBuildResult contextResult,
    KeywordsResult keywords,
    QueryParam queryParam)
```

Inside `BuildRawData`, replace `searchResult.Entities`, `searchResult.Relations`, `searchResult.Chunks`, and `searchResult.References` with `contextResult.Entities`, `contextResult.Relations`, `contextResult.Chunks`, and `contextResult.References`.

Delete the old private methods from `RetrievalContextService`:

```csharp
private string BuildContextString(KGSearchResult searchResult, QueryParam queryParam)
private int GetEntityCountByTokens(IEnumerable<EntityData> entities, int maxTokens)
private int GetRelationCountByTokens(IEnumerable<RelationData> relations, int maxTokens)
```

- [ ] **Step 4: Run service raw data test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RetrievalContextServiceRawDataTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Run retrieval context focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~RetrievalContext|FullyQualifiedName~KgQueryContextBuilder" --verbosity minimal
```

Expected: all retrieval context and builder tests pass.

- [ ] **Step 6: Commit Task 3**

Run:

```powershell
git add src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs
git commit -m "refactor: route kg context through builder"
```

Expected: commit succeeds.

## Task 4: Move Final Chunk Budgeting To Builder

**Files:**
- Modify: `tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`

- [ ] **Step 1: Add failing chunk budget test with reference overhead**

Append this test to `KgQueryContextBuilderTests`:

```csharp
[Fact]
public void Build_LimitsChunksUsingFinalChunkAndReferenceSections()
{
    var builder = new KgQueryContextBuilder(new FakeTokenizer());
    var searchResult = new KGSearchResult
    {
        Chunks =
        [
            new ChunkData
            {
                ChunkId = "chunk-a",
                Content = "alpha",
                FilePath = "docs/a.md"
            },
            new ChunkData
            {
                ChunkId = "chunk-b",
                Content = "beta beta beta beta beta beta beta beta beta beta",
                FilePath = "docs/b.md"
            }
        ]
    };

    var result = builder.Build(
        searchResult,
        new QueryParam
        {
            MaxEntityTokens = 1000,
            MaxRelationTokens = 1000,
            MaxTotalTokens = 220
        },
        query: "alpha");

    result.Chunks.Should().ContainSingle(chunk => chunk.ChunkId == "chunk-a");
    result.Context.Should().Contain("""{"reference_id":"1","content":"alpha"}""");
    result.Context.Should().Contain("[1] docs/a.md");
    result.Context.Should().NotContain("chunk-b");
    result.Context.Should().NotContain("docs/b.md");
}
```

- [ ] **Step 2: Run builder tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~KgQueryContextBuilderTests --verbosity minimal
```

Expected: FAIL if the builder does not yet count final chunk section plus reference list overhead precisely enough.

- [ ] **Step 3: Remove old pre-reference chunk limiting from service**

In `RetrievalContextService`, remove this field:

```csharp
private readonly ChunkTokenLimiter _chunkTokenLimiter = new(tokenizer);
```

In `PerformKGSearchAsync`, delete the old token-budget block:

```csharp
// Apply token limit (apply uniformly after merging all chunks)
// Reference Python version: after _merge_all_chunks, chunks will apply token limit in _build_context_str
// But for consistency, we apply the limit here
var tokenBudgetPlan = _tokenBudgetPlanner.Plan(
    maxTotalTokens: queryParam.MaxTotalTokens,
    systemPrompt: string.Empty,
    query: string.Empty,
    knowledgeGraphContext: string.Empty,
    reservedOutputTokens: queryParam.MaxEntityTokens + queryParam.MaxRelationTokens,
    safetyBufferTokens: 0);
var finalChunks = _chunkTokenLimiter.Limit(mergedChunks, tokenBudgetPlan.AvailableChunkTokens);
```

Then replace:

```csharp
var (references, chunksWithRefIds) = _referenceListBuilder.Build(finalChunks);
```

with:

```csharp
var (references, chunksWithRefIds) = _referenceListBuilder.Build(mergedChunks);
```

Keep `_referenceListBuilder` in service for this task so the search result still has references before builder wiring. If a later cleanup removes it, do it after tests are green.

- [ ] **Step 4: Ensure builder is the final chunk limiter**

In `KgQueryContextBuilder.LimitChunksByFinalContext`, keep the candidate loop that builds references before counting:

```csharp
var (candidateReferences, candidateChunksWithRefIds) = _referenceListBuilder.Build(candidate);
var candidateContext = BuildChunkAndReferenceContext(candidateChunksWithRefIds, candidateReferences);
if (tokenizer.CountTokens(candidateContext) > availableChunkTokens)
{
    break;
}
```

This loop is the required final-shape budget gate. Do not replace it with `ChunkTokenLimiter`, because `ChunkTokenLimiter` counts legacy filename-prefixed text and misses reference list overhead.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~KgQueryContextBuilder|FullyQualifiedName~RetrievalContext|FullyQualifiedName~ChunkTokenLimiter|FullyQualifiedName~TokenBudgetPlanner" --verbosity minimal
```

Expected: PASS. `ChunkTokenLimiterTests` should still pass because the helper remains available for existing callers, even if KG context no longer uses it.

- [ ] **Step 6: Commit Task 4**

Run:

```powershell
git add src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs
git commit -m "refactor: budget kg chunks by final context"
```

Expected: commit succeeds.

## Task 5: Full Regression And Close-Out

**Files:**
- Modify only if tests reveal a real regression:
  - `src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs`
  - `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`
  - focused tests under `tests/LightRAGNet.Tests/RetrievalContext/`

- [ ] **Step 1: Run the core focused verification**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~KgQueryContextBuilder|FullyQualifiedName~RetrievalContext|FullyQualifiedName~QueryCache|FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~NaiveQueryServiceTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 2: Run the full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS for `LightRAGNet.Tests`, `LightRAGNet.Server.Tests`, and `LightRAGNet.Web.Tests`.

- [ ] **Step 3: Run the full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: build succeeds with `0` errors. Treat new warnings as regressions and fix them before close-out.

- [ ] **Step 4: Review diff scope**

Run:

```powershell
git status --short
git diff --stat HEAD
git diff --name-only HEAD
```

Expected: changed files are limited to:

```text
src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs
src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs
tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs
tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs
```

If `TokenBudgetPlannerTests.cs` changed because execution added a planner-level assertion, include it in the commit. If unrelated files changed, inspect them and do not commit unrelated edits.

- [ ] **Step 5: Commit any final fixes**

If Step 1-4 required additional fixes after Task 4, run:

```powershell
git add src/LightRAGNet/Services/RetrievalContext/KgQueryContextBuilder.cs src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs tests/LightRAGNet.Tests/RetrievalContext/KgQueryContextBuilderTests.cs tests/LightRAGNet.Tests/RetrievalContext/RetrievalContextServiceRawDataTests.cs tests/LightRAGNet.Tests/RetrievalContext/TokenBudgetPlannerTests.cs
git commit -m "fix: stabilize kg context builder parity"
```

Expected: commit succeeds when there were final fixes. If no final fixes were needed, skip this commit and record that Task 4 commit is the final implementation commit.

- [ ] **Step 6: Run asset completion gate before final handoff**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "kg-context-builder-parity" --json
```

Expected: command completes and reports whether an archive is needed. If it reports missing archive coverage for completed spec+plan, create or update the archive before final close-out using the repository asset-compounding guidance.

## Self-Review

- Spec coverage:
  - Structured JSON entity, relationship, chunk sections are covered by Task 1 and Task 3.
  - `reference_id` consistency between context and raw data is covered by Task 1 and Task 3.
  - Entity/relation token limits using final JSON shape are covered by Task 2.
  - Chunk budget with reference list overhead is covered by Task 4.
  - No-context behavior is preserved by Task 3 returning `null` for empty builder context and by existing retrieval tests.
- Placeholder scan:
  - No unresolved marker text or unspecified test steps are present.
- Type consistency:
  - The plan defines `KgQueryContextBuilder`, `KgQueryContextBuildResult`, and reuses existing `KGSearchResult`, `EntityData`, `RelationData`, `ChunkData`, `ReferenceItem`, `QueryParam`, and `FakeTokenizer`.
  - `QueryContextResult` remains in `src/LightRAGNet/LightRAG.cs`; this plan does not move it.
