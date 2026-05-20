# Indexing LLM Cache Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the old chunk-id indexing cache with Python-aligned `default:extract:{hash}` and `default:summary:{hash}` LLM cache contracts, with extract keys written to `text_chunks[].llm_cache_list`.

**Architecture:** Extend the existing `LightRagLlmCacheService` into a generic LLM prompt cache facade, then route entity extraction and summary calls through deterministic prompt builders/parsers. `extract` cache is chunk-owned and deletion-cleanable; `summary` cache is prompt-owned and remains global.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, existing `IKVStore`/`InMemoryKvStore`, existing `LightRAGJsonOptions`.

---

## Scope Guard

This plan implements the approved spec:

- Spec: `docs/superpowers/specs/2026-05-20-indexing-llm-cache-parity-design.md`
- No compatibility with legacy `chunk.Id -> ChunkResult` cache reads.
- No embedding cache.
- No KV storage conversion from keyed `.json` object files to JSONL files.
- JSONL is used only for summary prompt description lists in this phase.

## File Structure

Create:

- `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionPromptBuilder.cs`
  - Builds the system/user prompt pair currently hidden inside `DeepSeekLLMService`.
- `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionResultParser.cs`
  - Parses raw extract LLM text into `EntityExtractionResult`.
- `src/LightRAGNet/Services/KnowledgeGraphMerge/SummaryPromptBuilder.cs`
  - Builds Python-style summary prompt with JSONL description list.
- `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionPromptBuilderTests.cs`
- `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionResultParserTests.cs`
- `tests/LightRAGNet.Tests/KnowledgeGraphMerge/SummaryPromptBuilderTests.cs`

Modify:

- `src/LightRAGNet/LightRAGOptions.cs`
  - Add `EnableLlmCacheForEntityExtract`.
- `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`
  - Add `extract` and `summary` constants and key builders.
- `src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs`
  - Add optional `ChunkId`.
- `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
  - Add indexing prompt cache get/save helpers and canonical prompt logic.
- `src/LightRAGNet/Services/DocumentProcessing/Chunk.cs`
  - Add `LlmCacheKeys` to `ChunkResult`.
- `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
  - Remove legacy chunk-id cache and use `default:extract:{hash}`.
- `src/LightRAGNet/Services/KnowledgeGraphMerge/DescriptionMerger.cs`
  - Use `default:summary:{hash}` cache for LLM summaries.
- `src/LightRAGNet/Services/KnowledgeGraphMerge/KnowledgeGraphMergeService.cs`
  - Pass cache service into `DescriptionMerger`.
- `src/LightRAGNet/LightRAG.cs`
  - Write `llm_cache_list` when storing text chunks.
- Existing tests under:
  - `tests/LightRAGNet.Tests/QueryCache/`
  - `tests/LightRAGNet.Tests/DocumentProcessing/`
  - `tests/LightRAGNet.Tests/KnowledgeGraphMerge/`
  - `tests/LightRAGNet.Tests/DocumentDeletion/`
  - `tests/LightRAGNet.Tests/DocumentLifecycle/`

---

### Task 1: Extend Cache Key and Entry Contracts

**Files:**
- Modify: `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`
- Modify: `src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs`
- Modify: `src/LightRAGNet/LightRAGOptions.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagCacheKeyBuilderTests.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`

- [ ] **Step 1: Write failing key builder tests**

Add tests to `tests/LightRAGNet.Tests/QueryCache/LightRagCacheKeyBuilderTests.cs`:

```csharp
[Fact]
public void BuildExtractKey_UsesDefaultExtractPrefix()
{
    var builder = new LightRagCacheKeyBuilder();

    var key = builder.BuildExtractKey("user prompt\nsystem prompt");

    key.Should().StartWith("default:extract:");
    key.Split(':').Should().HaveCount(3);
    key.Split(':')[2].Should().HaveLength(64);
}

[Fact]
public void BuildSummaryKey_UsesDefaultSummaryPrefix()
{
    var builder = new LightRagCacheKeyBuilder();

    var key = builder.BuildSummaryKey("summary prompt");

    key.Should().StartWith("default:summary:");
    key.Split(':').Should().HaveCount(3);
    key.Split(':')[2].Should().HaveLength(64);
}

[Fact]
public void BuildExtractKey_WhenPromptChanges_ChangesHash()
{
    var builder = new LightRagCacheKeyBuilder();

    var first = builder.BuildExtractKey("prompt-a");
    var second = builder.BuildExtractKey("prompt-b");

    first.Should().NotBe(second);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRagCacheKeyBuilderTests --verbosity minimal
```

Expected: fail because `BuildExtractKey`, `BuildSummaryKey`, `ExtractCacheType`, and `SummaryCacheType` do not exist.

- [ ] **Step 3: Implement cache key builders**

In `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`, add constants and methods:

```csharp
public const string ExtractCacheType = "extract";
public const string SummaryCacheType = "summary";
public const string DefaultCacheMode = "default";

public string BuildExtractKey(string canonicalPrompt)
{
    return BuildFlattenedKey(
        DefaultCacheMode,
        ExtractCacheType,
        [Pair("prompt", canonicalPrompt)]);
}

public string BuildSummaryKey(string canonicalPrompt)
{
    return BuildFlattenedKey(
        DefaultCacheMode,
        SummaryCacheType,
        [Pair("prompt", canonicalPrompt)]);
}
```

Change `BuildFlattenedKey` to accept a string mode:

```csharp
private static string BuildFlattenedKey(
    string mode,
    string cacheType,
    IReadOnlyList<KeyValuePair<string, string>> parts)
{
    var canonical = string.Join(
        "\u001f",
        parts.Select(part => $"{part.Key.Length}:{part.Key}={part.Value.Length}:{part.Value}"));
    var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
    return $"{mode}:{cacheType}:{hash}";
}
```

Keep the existing `QueryMode` overload so query tests keep working:

```csharp
private static string BuildFlattenedKey(
    QueryMode mode,
    string cacheType,
    IReadOnlyList<KeyValuePair<string, string>> parts)
{
    return BuildFlattenedKey(mode.ToString(), cacheType, parts);
}
```

- [ ] **Step 4: Add entry contract failing test**

Add to `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`:

```csharp
[Fact]
public void LightRagCacheEntry_ToDictionary_IncludesChunkIdWhenPresent()
{
    var entry = new LightRagCacheEntry(
        "raw extract response",
        LightRagCacheKeyBuilder.ExtractCacheType,
        "canonical prompt",
        null,
        123,
        "chunk-a");

    var data = entry.ToDictionary();

    data["return"].Should().Be("raw extract response");
    data["cache_type"].Should().Be("extract");
    data["chunk_id"].Should().Be("chunk-a");
    data["original_prompt"].Should().Be("canonical prompt");
    data["queryparam"].Should().BeNull();
    data["create_time"].Should().Be(123);
}

[Fact]
public void LightRagCacheEntry_TryFromDictionary_ReadsNullChunkId()
{
    var data = new Dictionary<string, object>
    {
        ["return"] = "summary",
        ["cache_type"] = LightRagCacheKeyBuilder.SummaryCacheType,
        ["chunk_id"] = null!,
        ["original_prompt"] = "summary prompt",
        ["queryparam"] = null!,
        ["create_time"] = 456
    };

    var ok = LightRagCacheEntry.TryFromDictionary(data, out var entry);

    ok.Should().BeTrue();
    entry.ChunkId.Should().BeNull();
    entry.CacheType.Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
}
```

- [ ] **Step 5: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRagLlmCacheServiceTests --verbosity minimal
```

Expected: fail because `LightRagCacheEntry` has no `ChunkId` constructor parameter/property and currently writes empty dictionaries for null query params.

- [ ] **Step 6: Implement entry contract and option**

Update `LightRagCacheEntry` record signature:

```csharp
public sealed record LightRagCacheEntry(
    string ReturnValue,
    string CacheType,
    string OriginalPrompt,
    Dictionary<string, object?>? QueryParam,
    long CreateTime,
    string? ChunkId = null)
```

Update `ToDictionary()`:

```csharp
return new Dictionary<string, object>
{
    ["return"] = ReturnValue,
    ["cache_type"] = CacheType,
    ["chunk_id"] = ChunkId!,
    ["original_prompt"] = OriginalPrompt,
    ["queryparam"] = QueryParam!,
    ["create_time"] = CreateTime
};
```

Update `TryFromDictionary()` to pass `ReadNullableString(data, "chunk_id")`, and add:

```csharp
private static string? ReadNullableString(Dictionary<string, object> data, string key)
{
    if (!data.TryGetValue(key, out var value) || value is null)
    {
        return null;
    }

    return value switch
    {
        string text when string.IsNullOrWhiteSpace(text) => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        JsonElement { ValueKind: JsonValueKind.String } json => string.IsNullOrWhiteSpace(json.GetString()) ? null : json.GetString(),
        JsonElement json => json.ToString(),
        _ => value.ToString()
    };
}
```

Add to `src/LightRAGNet/LightRAGOptions.cs` near the other cache options:

```csharp
/// <summary>
/// Enables indexing-stage LLM cache for entity extraction and graph summaries.
/// </summary>
public bool EnableLlmCacheForEntityExtract { get; set; } = true;
```

- [ ] **Step 7: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~LightRagCacheKeyBuilderTests|FullyQualifiedName~LightRagLlmCacheServiceTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add src\LightRAGNet\LightRAGOptions.cs src\LightRAGNet\Services\QueryCache\LightRagCacheKeyBuilder.cs src\LightRAGNet\Services\QueryCache\LightRagCacheEntry.cs tests\LightRAGNet.Tests\QueryCache\LightRagCacheKeyBuilderTests.cs tests\LightRAGNet.Tests\QueryCache\LightRagLlmCacheServiceTests.cs
git commit -m "feat: extend llm cache indexing contract"
```

---

### Task 2: Add Generic Indexing Prompt Cache Service Methods

**Files:**
- Modify: `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`

- [ ] **Step 1: Write failing tests for extract cache hit/save**

Add to `LightRagLlmCacheServiceTests.cs`:

```csharp
[Fact]
public async Task TryGetExtractAsync_WhenCacheHit_ReturnsRawResponse()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var canonicalPrompt = "user prompt\nsystem prompt";
    var key = keyBuilder.BuildExtractKey(canonicalPrompt);
    store.Seed(
        key,
        new LightRagCacheEntry(
            "entity<|#|>ALPHA<|#|>concept<|#|>desc\n<|COMPLETE|>",
            LightRagCacheKeyBuilder.ExtractCacheType,
            canonicalPrompt,
            null,
            123,
            "chunk-a").ToDictionary());
    var service = CreateService(store, keyBuilder: keyBuilder);

    var result = await service.TryGetExtractAsync(canonicalPrompt);

    result.Should().Be("entity<|#|>ALPHA<|#|>concept<|#|>desc\n<|COMPLETE|>");
}

[Fact]
public async Task SaveExtractAsync_PersistsPythonStyleEntry()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var service = CreateService(store, keyBuilder: keyBuilder);
    var canonicalPrompt = "用户问题\n系统提示";

    var key = await service.SaveExtractAsync(
        canonicalPrompt,
        "entity<|#|>ALPHA<|#|>concept<|#|>描述\n<|COMPLETE|>",
        "chunk-a");

    key.Should().Be(keyBuilder.BuildExtractKey(canonicalPrompt));
    store.Items.Should().ContainKey(key);
    var entry = store.Items[key];
    entry["cache_type"].Should().Be("extract");
    entry["chunk_id"].Should().Be("chunk-a");
    entry["original_prompt"].Should().Be(canonicalPrompt);
    entry["return"].Should().Be("entity<|#|>ALPHA<|#|>concept<|#|>描述\n<|COMPLETE|>");
}
```

- [ ] **Step 2: Write failing tests for disabled indexing cache**

Add:

```csharp
[Fact]
public async Task TryGetExtractAsync_WhenIndexingCacheDisabled_DoesNotReadStore()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var key = keyBuilder.BuildExtractKey("prompt");
    store.Seed(
        key,
        new LightRagCacheEntry("cached", "extract", "prompt", null, 123, "chunk-a").ToDictionary());
    var service = CreateService(
        store,
        new LightRAGOptions
        {
            EnableLlmCache = true,
            EnableLlmCacheForEntityExtract = false
        },
        keyBuilder);

    var result = await service.TryGetExtractAsync("prompt");

    result.Should().BeNull();
    store.GetByIdCalls.Should().BeEmpty();
}

[Fact]
public async Task SaveExtractAsync_WhenGlobalCacheDisabled_DoesNotWriteStore()
{
    var store = new InMemoryKvStore();
    var service = CreateService(
        store,
        new LightRAGOptions
        {
            EnableLlmCache = false,
            EnableLlmCacheForEntityExtract = true
        });

    var key = await service.SaveExtractAsync("prompt", "response", "chunk-a");

    key.Should().BeNull();
    store.Items.Should().BeEmpty();
}
```

- [ ] **Step 3: Write failing summary cache tests**

Add:

```csharp
[Fact]
public async Task SaveSummaryAsync_PersistsChunkIdAsNull()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var service = CreateService(store, keyBuilder: keyBuilder);
    var canonicalPrompt = "summary prompt";

    var key = await service.SaveSummaryAsync(canonicalPrompt, "summary result");

    key.Should().Be(keyBuilder.BuildSummaryKey(canonicalPrompt));
    var entry = store.Items[key!];
    entry["cache_type"].Should().Be("summary");
    entry["chunk_id"].Should().BeNull();
    entry["return"].Should().Be("summary result");
}

[Fact]
public async Task TryGetSummaryAsync_WhenCacheTypeMismatch_ReturnsNull()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var prompt = "summary prompt";
    store.Seed(
        keyBuilder.BuildSummaryKey(prompt),
        new LightRagCacheEntry("wrong", "extract", prompt, null, 123, "chunk-a").ToDictionary());
    var service = CreateService(store, keyBuilder: keyBuilder);

    var result = await service.TryGetSummaryAsync(prompt);

    result.Should().BeNull();
}
```

- [ ] **Step 4: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRagLlmCacheServiceTests --verbosity minimal
```

Expected: fail because `TryGetExtractAsync`, `SaveExtractAsync`, `TryGetSummaryAsync`, and `SaveSummaryAsync` do not exist.

- [ ] **Step 5: Implement service methods**

Add public methods to `LightRagLlmCacheService`:

```csharp
public Task<string?> TryGetExtractAsync(
    string canonicalPrompt,
    CancellationToken cancellationToken = default)
{
    return TryGetIndexingResponseAsync(
        keyBuilder.BuildExtractKey(canonicalPrompt),
        LightRagCacheKeyBuilder.ExtractCacheType,
        cancellationToken);
}

public Task<string?> SaveExtractAsync(
    string canonicalPrompt,
    string response,
    string chunkId,
    CancellationToken cancellationToken = default)
{
    return SaveIndexingResponseAsync(
        keyBuilder.BuildExtractKey(canonicalPrompt),
        canonicalPrompt,
        response,
        LightRagCacheKeyBuilder.ExtractCacheType,
        chunkId,
        cancellationToken);
}

public Task<string?> TryGetSummaryAsync(
    string canonicalPrompt,
    CancellationToken cancellationToken = default)
{
    return TryGetIndexingResponseAsync(
        keyBuilder.BuildSummaryKey(canonicalPrompt),
        LightRagCacheKeyBuilder.SummaryCacheType,
        cancellationToken);
}

public Task<string?> SaveSummaryAsync(
    string canonicalPrompt,
    string response,
    CancellationToken cancellationToken = default)
{
    return SaveIndexingResponseAsync(
        keyBuilder.BuildSummaryKey(canonicalPrompt),
        canonicalPrompt,
        response,
        LightRagCacheKeyBuilder.SummaryCacheType,
        null,
        cancellationToken);
}
```

Add helpers:

```csharp
private async Task<string?> TryGetIndexingResponseAsync(
    string key,
    string expectedCacheType,
    CancellationToken cancellationToken)
{
    if (!IsIndexingCacheEnabled())
    {
        return null;
    }

    try
    {
        var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
        if (!LightRagCacheEntry.TryFromDictionary(data, out var entry) ||
            !string.Equals(entry.CacheType, expectedCacheType, StringComparison.Ordinal))
        {
            return null;
        }

        return entry.ReturnValue;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Failed to read indexing LLM cache entry {CacheKey}.", key);
        return null;
    }
}

private async Task<string?> SaveIndexingResponseAsync(
    string key,
    string canonicalPrompt,
    string response,
    string cacheType,
    string? chunkId,
    CancellationToken cancellationToken)
{
    if (!IsIndexingCacheEnabled() || string.IsNullOrWhiteSpace(response))
    {
        return null;
    }

    try
    {
        var entry = new LightRagCacheEntry(
            response,
            cacheType,
            canonicalPrompt,
            null,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            chunkId);

        await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [key] = entry.ToDictionary()
        }, cancellationToken);
        await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
        return key;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Failed to save indexing LLM cache entry {CacheKey}.", key);
        return null;
    }
}

private bool IsIndexingCacheEnabled()
{
    return options.Value.EnableLlmCache && options.Value.EnableLlmCacheForEntityExtract;
}
```

- [ ] **Step 6: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRagLlmCacheServiceTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet\Services\QueryCache\LightRagLlmCacheService.cs tests\LightRAGNet.Tests\QueryCache\LightRagLlmCacheServiceTests.cs
git commit -m "feat: add indexing llm cache service methods"
```

---

### Task 3: Add Entity Extraction Prompt Builder and Parser

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionPromptBuilder.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionResultParser.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionPromptBuilderTests.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionResultParserTests.cs`

- [ ] **Step 1: Write failing prompt builder tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionPromptBuilderTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class EntityExtractionPromptBuilderTests
{
    [Fact]
    public void Build_CreatesSystemAndUserPrompts()
    {
        var prompt = EntityExtractionPromptBuilder.Build(
            "alpha beta",
            ["Person", "Concept"],
            maxEntities: 5,
            maxRelationships: 7);

        prompt.SystemPrompt.Should().Contain("---Role---");
        prompt.SystemPrompt.Should().Contain("Entity_types");
        prompt.SystemPrompt.Should().Contain("5 entities");
        prompt.SystemPrompt.Should().Contain("7 relationships");
        prompt.UserPrompt.Should().Contain("---Data to be Processed---");
        prompt.UserPrompt.Should().Contain("alpha beta");
        prompt.UserPrompt.Should().Contain("<|COMPLETE|>");
    }

    [Fact]
    public void CanonicalPrompt_JoinsUserThenSystemPrompt()
    {
        var prompt = new EntityExtractionPrompt(
            "user prompt",
            "system prompt");

        prompt.CanonicalPrompt.Should().Be("user prompt\nsystem prompt");
    }
}
```

- [ ] **Step 2: Write failing parser tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/EntityExtractionResultParserTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class EntityExtractionResultParserTests
{
    [Fact]
    public void Parse_ReadsEntitiesAndRelationships()
    {
        const string response = """
                                entity<|#|>Alpha<|#|>Concept<|#|>Alpha description
                                relation<|#|>Alpha<|#|>Beta<|#|>connects<|#|>Alpha relates to Beta<|#|>2.5
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 10, maxRelationships: 10);

        result.Entities.Should().ContainSingle();
        result.Entities[0].Name.Should().Be("Alpha");
        result.Entities[0].Type.Should().Be("concept");
        result.Entities[0].Description.Should().Be("Alpha description");
        result.Relationships.Should().ContainSingle();
        result.Relationships[0].SourceId.Should().Be("Alpha");
        result.Relationships[0].TargetId.Should().Be("Beta");
        result.Relationships[0].Keywords.Should().Be("connects");
        result.Relationships[0].Description.Should().Be("Alpha relates to Beta");
        result.Relationships[0].Weight.Should().Be(2.5f);
    }

    [Fact]
    public void Parse_RemovesThinkTagsBeforeParsing()
    {
        const string response = """
                                <think>internal reasoning</think>
                                entity<|#|>Alpha<|#|>Concept<|#|>Alpha description
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 10, maxRelationships: 10);

        result.Entities.Should().ContainSingle(entity => entity.Name == "Alpha");
    }

    [Fact]
    public void Parse_AppliesEntityAndRelationshipLimits()
    {
        const string response = """
                                entity<|#|>Alpha<|#|>Concept<|#|>Alpha description
                                entity<|#|>Beta<|#|>Concept<|#|>Beta description
                                relation<|#|>Alpha<|#|>Beta<|#|>connects<|#|>First relation
                                relation<|#|>Beta<|#|>Gamma<|#|>connects<|#|>Second relation
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 1, maxRelationships: 1);

        result.Entities.Should().ContainSingle(entity => entity.Name == "Alpha");
        result.Relationships.Should().ContainSingle(relation => relation.SourceId == "Alpha");
    }
}
```

- [ ] **Step 3: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~EntityExtractionPromptBuilderTests|FullyQualifiedName~EntityExtractionResultParserTests" --verbosity minimal
```

Expected: fail because the new classes do not exist.

- [ ] **Step 4: Implement prompt builder**

Create `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionPromptBuilder.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing;

public sealed record EntityExtractionPrompt(string UserPrompt, string SystemPrompt)
{
    public string CanonicalPrompt => string.Join(
        "\n",
        new[] { UserPrompt, SystemPrompt }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

internal static class EntityExtractionPromptBuilder
{
    public static EntityExtractionPrompt Build(
        string text,
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var systemPrompt = BuildSystemPrompt(entityTypes, maxEntities, maxRelationships);
        var userPrompt = BuildUserPrompt(text, entityTypes, maxEntities, maxRelationships);
        return new EntityExtractionPrompt(userPrompt, systemPrompt);
    }

    private static string BuildSystemPrompt(
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var entityTypesJson = JsonSerializer.Serialize(entityTypes, LightRAGJsonOptions.HumanReadable);
        return $"""
                ---Role---
                You are an expert knowledge graph extraction assistant.

                ---Goal---
                Given a text document that is potentially relevant to this activity and a list of entity types, identify all entities of those types from the text and all relationships among the identified entities.

                ---Entity_types---
                {entityTypesJson}

                ---Output Format---
                Use one line per entity or relationship:
                entity<|#|>entity_name<|#|>entity_type<|#|>entity_description
                relation<|#|>source_entity<|#|>target_entity<|#|>relationship_keywords<|#|>relationship_description<|#|>relationship_strength

                Output at most {maxEntities} entities and {maxRelationships} relationships.
                End the response with <|COMPLETE|>.
                """;
    }

    private static string BuildUserPrompt(
        string text,
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var entityTypesJson = JsonSerializer.Serialize(entityTypes, LightRAGJsonOptions.HumanReadable);
        return $"""
                ---Task---
                Extract entities and relationships from the input text in Data to be Processed below.

                ---Instructions---
                1. Strictly adhere to all format requirements for entity and relationship lists.
                2. Output only the extracted list of entities and relationships.
                3. Output <|COMPLETE|> as the final line.
                4. Use the same language as the input text.
                5. Extract a maximum of {maxEntities} entities and {maxRelationships} relationships.

                ---Data to be Processed---
                <Entity_types>
                {entityTypesJson}

                <Input Text>
                ```
                {text}
                ```

                <Output>
                """;
    }
}
```

- [ ] **Step 5: Implement parser**

Create `src/LightRAGNet/Services/DocumentProcessing/EntityExtractionResultParser.cs`:

```csharp
using System.Text.RegularExpressions;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing;

internal static partial class EntityExtractionResultParser
{
    public static EntityExtractionResult Parse(string response, int maxEntities, int maxRelationships)
    {
        var result = new EntityExtractionResult();
        var cleaned = RemoveThinkTags(response);
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Trim() == "<|COMPLETE|>")
            {
                break;
            }

            var parts = line.Split(["<|#|>"], StringSplitOptions.None);
            if (parts.Length >= 4 && parts[0].Trim() == "entity")
            {
                result.Entities.Add(new Entity
                {
                    Name = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    Type = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true).Replace(" ", "").ToLowerInvariant(),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[3])
                });
            }
            else if (parts.Length >= 5 && parts[0].Trim() == "relation")
            {
                result.Relationships.Add(new Relationship
                {
                    SourceId = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    TargetId = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true),
                    Keywords = TextUtils.SanitizeAndNormalizeText(parts[3], removeInnerQuotes: true),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[4]),
                    Weight = ParseWeight(parts)
                });
            }
        }

        result.Entities = result.Entities.Take(maxEntities).ToList();
        result.Relationships = result.Relationships.Take(maxRelationships).ToList();
        return result;
    }

    private static float ParseWeight(string[] parts)
    {
        if (parts.Length < 6)
        {
            return 1.0f;
        }

        var text = parts[5].Trim().Trim('"').Trim('\'');
        return float.TryParse(text, out var weight) ? weight : 1.0f;
    }

    private static string RemoveThinkTags(string text)
    {
        var withoutOrphanPrefix = OrphanThinkEndRegex().Replace(text, string.Empty);
        return ThinkBlockRegex().Replace(withoutOrphanPrefix, string.Empty).Trim();
    }

    [GeneratedRegex("^((?!<think>).)*?</think>", RegexOptions.Singleline)]
    private static partial Regex OrphanThinkEndRegex();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();
}
```

- [ ] **Step 6: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~EntityExtractionPromptBuilderTests|FullyQualifiedName~EntityExtractionResultParserTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\EntityExtractionPromptBuilder.cs src\LightRAGNet\Services\DocumentProcessing\EntityExtractionResultParser.cs tests\LightRAGNet.Tests\DocumentProcessing\EntityExtractionPromptBuilderTests.cs tests\LightRAGNet.Tests\DocumentProcessing\EntityExtractionResultParserTests.cs
git commit -m "feat: add entity extraction prompt contract"
```

---

### Task 4: Replace Document Processing Legacy Cache with Extract Cache

**Files:**
- Modify: `src/LightRAGNet/Services/DocumentProcessing/Chunk.cs`
- Modify: `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`

- [ ] **Step 1: Write failing extract cache miss test**

Add to `DocumentProcessingServiceTests.cs`:

```csharp
[Fact]
public async Task ProcessChunkAsync_WhenExtractCacheMiss_CallsGenerateAndStoresExtractCacheKey()
{
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("entity<|#|>Alpha<|#|>Concept<|#|>Alpha description\n<|COMPLETE|>");
    var embeddingService = Substitute.For<IEmbeddingService>();
    embeddingService.GenerateEmbeddingAsync("alpha content", Arg.Any<CancellationToken>())
        .Returns([1.0f, 0.5f]);
    var cacheStore = new InMemoryKvStore();
    var service = CreateService(
        llmService,
        embeddingService,
        cacheStore,
        new LightRAGOptions
        {
            EnableLlmCache = true,
            EnableLlmCacheForEntityExtract = true
        });

    var result = await service.ProcessChunkAsync(new Chunk
    {
        Id = "chunk-a",
        Content = "alpha content",
        FilePath = "alpha.md"
    });

    result.LlmCacheKeys.Should().ContainSingle(key => key.StartsWith("default:extract:", StringComparison.Ordinal));
    cacheStore.Items.Should().ContainKey(result.LlmCacheKeys.Single());
    cacheStore.Items[result.LlmCacheKeys.Single()]["cache_type"].Should().Be("extract");
    cacheStore.Items[result.LlmCacheKeys.Single()]["chunk_id"].Should().Be("chunk-a");
    result.Embedding.Should().Equal(1.0f, 0.5f);
    result.Entities.Should().ContainSingle(entity => entity.SourceId == "chunk-a" && entity.FilePath == "alpha.md");
}
```

- [ ] **Step 2: Write failing extract cache hit test**

Add:

```csharp
[Fact]
public async Task ProcessChunkAsync_WhenExtractCacheHit_DoesNotCallGenerateButStillGeneratesEmbedding()
{
    var prompt = EntityExtractionPromptBuilder.Build(
        "alpha content",
        ["Person", "Creature", "Organization", "Location", "Event", "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"],
        maxEntities: 45,
        maxRelationships: 60);
    var keyBuilder = new LightRagCacheKeyBuilder();
    var cacheKey = keyBuilder.BuildExtractKey(prompt.CanonicalPrompt);
    var cacheStore = new InMemoryKvStore();
    cacheStore.Seed(
        cacheKey,
        new LightRagCacheEntry(
            "entity<|#|>Alpha<|#|>Concept<|#|>Cached description\n<|COMPLETE|>",
            LightRagCacheKeyBuilder.ExtractCacheType,
            prompt.CanonicalPrompt,
            null,
            123,
            "chunk-a").ToDictionary());
    var llmService = Substitute.For<ILLMService>();
    var embeddingService = Substitute.For<IEmbeddingService>();
    embeddingService.GenerateEmbeddingAsync("alpha content", Arg.Any<CancellationToken>())
        .Returns([0.1f, 0.2f]);
    var service = CreateService(llmService, embeddingService, cacheStore);

    var result = await service.ProcessChunkAsync(new Chunk
    {
        Id = "chunk-a",
        Content = "alpha content",
        FilePath = "cached.md"
    });

    await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
    await embeddingService.Received(1).GenerateEmbeddingAsync("alpha content", Arg.Any<CancellationToken>());
    result.LlmCacheKeys.Should().ContainSingle().Which.Should().Be(cacheKey);
    result.Entities.Should().ContainSingle(entity =>
        entity.Name == "Alpha" &&
        entity.SourceId == "chunk-a" &&
        entity.FilePath == "cached.md");
}
```

- [ ] **Step 3: Write failing legacy cache ignored test**

Add:

```csharp
[Fact]
public async Task ProcessChunkAsync_IgnoresLegacyChunkIdCache()
{
    var cacheStore = new InMemoryKvStore();
    cacheStore.Seed("chunk-a", new Dictionary<string, object>
    {
        ["chunk_id"] = "chunk-a",
        ["embedding"] = new List<object> { 9.0f },
        ["entities"] = new List<object>(),
        ["relationships"] = new List<object>()
    });
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("<|COMPLETE|>");
    var embeddingService = Substitute.For<IEmbeddingService>();
    embeddingService.GenerateEmbeddingAsync("alpha content", Arg.Any<CancellationToken>())
        .Returns([1.0f]);
    var service = CreateService(llmService, embeddingService, cacheStore);

    var result = await service.ProcessChunkAsync(new Chunk { Id = "chunk-a", Content = "alpha content" });

    await llmService.ReceivedWithAnyArgs(1).GenerateAsync(default!, default, default, default, default, default);
    result.Embedding.Should().Equal(1.0f);
}
```

- [ ] **Step 4: Update test helper**

Replace the private `CreateService(int chunkSize, int overlap)` helper with overloads:

```csharp
private static DocumentProcessingService CreateService(int chunkSize, int overlap)
{
    return CreateService(
        Substitute.For<ILLMService>(),
        Substitute.For<IEmbeddingService>(),
        Substitute.For<IKVStore>(),
        new LightRAGOptions
        {
            ChunkTokenSize = chunkSize,
            ChunkOverlapTokenSize = overlap
        });
}

private static DocumentProcessingService CreateService(
    ILLMService llmService,
    IEmbeddingService embeddingService,
    IKVStore cacheStore,
    LightRAGOptions? options = null)
{
    return new DocumentProcessingService(
        llmService,
        embeddingService,
        new FakeTokenizer(),
        cacheStore,
        Options.Create(options ?? new LightRAGOptions
        {
            ChunkTokenSize = 10,
            ChunkOverlapTokenSize = 2
        }),
        new LightRagLlmCacheService(
            cacheStore,
            Options.Create(options ?? new LightRAGOptions()),
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance),
        NullLogger<DocumentProcessingService>.Instance);
}
```

Add missing `using` statements:

```csharp
using LightRAGNet.Core.Models;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.AI;
```

- [ ] **Step 5: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentProcessingServiceTests --verbosity minimal
```

Expected: fail because `ChunkResult.LlmCacheKeys` does not exist and `DocumentProcessingService` still uses legacy chunk-id cache and `ExtractEntitiesAsync`.

- [ ] **Step 6: Update ChunkResult**

In `src/LightRAGNet/Services/DocumentProcessing/Chunk.cs`, add:

```csharp
public List<string> LlmCacheKeys { get; set; } = [];
```

- [ ] **Step 7: Inject cache service**

Update `DocumentProcessingService` constructor:

```csharp
public class DocumentProcessingService(
    ILLMService llmService,
    IEmbeddingService embeddingService,
    ITokenizer tokenizer,
    [FromKeyedServices(KVContracts.LLMCache)]
    IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    LightRagLlmCacheService llmCacheService,
    ILogger<DocumentProcessingService> logger)
```

Keep `llmCacheStore` only if existing DI requires keyed binding for compatibility during this task; remove it from the constructor if the compiler shows it is unused and service registration still resolves.

- [ ] **Step 8: Replace ProcessChunkAsync cache logic**

Replace legacy cache read/write logic in `ProcessChunkAsync` with:

```csharp
var embedding = await embeddingService.GenerateEmbeddingAsync(
    chunk.Content,
    cancellationToken);

var entityTypes = _options.EntityTypes ??
[
    "Person", "Creature", "Organization", "Location", "Event",
    "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"
];
var maxEntities = _options.MaxEntitiesPerChunk > 0 ? _options.MaxEntitiesPerChunk : 45;
var maxRelationships = _options.MaxRelationshipsPerChunk > 0 ? _options.MaxRelationshipsPerChunk : 60;
var prompt = EntityExtractionPromptBuilder.Build(
    chunk.Content,
    entityTypes,
    maxEntities,
    maxRelationships);

var cacheKeys = new List<string>();
var rawExtraction = await llmCacheService.TryGetExtractAsync(
    prompt.CanonicalPrompt,
    cancellationToken);

if (rawExtraction is null)
{
    rawExtraction = await llmService.GenerateAsync(
        prompt.UserPrompt,
        prompt.SystemPrompt,
        temperature: 0.3f,
        cancellationToken: cancellationToken);
    var savedKey = await llmCacheService.SaveExtractAsync(
        prompt.CanonicalPrompt,
        rawExtraction,
        chunk.Id,
        cancellationToken);
    if (!string.IsNullOrWhiteSpace(savedKey))
    {
        cacheKeys.Add(savedKey);
    }
}
else
{
    cacheKeys.Add(new LightRagCacheKeyBuilder().BuildExtractKey(prompt.CanonicalPrompt));
}

var extractionResult = EntityExtractionResultParser.Parse(
    rawExtraction,
    maxEntities,
    maxRelationships);
```

Then keep the existing source/file/timestamp assignment logic and return:

```csharp
return new ChunkResult
{
    ChunkId = chunk.Id,
    Embedding = embedding,
    Entities = extractionResult.Entities,
    Relationships = extractionResult.Relationships,
    LlmCacheKeys = cacheKeys.Distinct(StringComparer.Ordinal).ToList()
};
```

Remove `SerializeChunkResult` and `DeserializeChunkResult` from `DocumentProcessingService`.

- [ ] **Step 9: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentProcessingServiceTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 10: Update lifecycle test that seeded legacy cache**

In `LightRAGLifecycleIntegrationTests.cs`, replace the legacy cache seed in the test that verifies re-index after missing vectors. Use real extract cache keys built from `EntityExtractionPromptBuilder` and store `LightRagCacheEntry` values.

The new seed helper:

```csharp
private static void SeedExtractCache(
    InMemoryKvStore store,
    string chunkId,
    string content,
    string response)
{
    var prompt = EntityExtractionPromptBuilder.Build(
        content,
        ["Person", "Creature", "Organization", "Location", "Event", "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"],
        45,
        60);
    var keyBuilder = new LightRagCacheKeyBuilder();
    store.Seed(
        keyBuilder.BuildExtractKey(prompt.CanonicalPrompt),
        new LightRagCacheEntry(
            response,
            LightRagCacheKeyBuilder.ExtractCacheType,
            prompt.CanonicalPrompt,
            null,
            123,
            chunkId).ToDictionary());
}
```

Use responses such as:

```csharp
"entity<|#|>Alpha<|#|>Concept<|#|>Alpha description\n<|COMPLETE|>"
```

- [ ] **Step 11: Run lifecycle targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRAGLifecycleIntegrationTests --verbosity minimal
```

Expected: pass after test helper update.

- [ ] **Step 12: Commit**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\Chunk.cs src\LightRAGNet\Services\DocumentProcessing\DocumentProcessingService.cs tests\LightRAGNet.Tests\DocumentProcessing\DocumentProcessingServiceTests.cs tests\LightRAGNet.Tests\DocumentLifecycle\LightRAGLifecycleIntegrationTests.cs
git commit -m "feat: use extract llm cache during indexing"
```

---

### Task 5: Write Extract Cache Keys to Text Chunks

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`

- [ ] **Step 1: Write failing integration test**

Add to `LightRAGLifecycleIntegrationTests.cs`:

```csharp
[Fact]
public async Task InsertAsync_WritesExtractCacheKeysToTextChunks()
{
    var textChunksStore = new InMemoryKvStore();
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("entity<|#|>Alpha<|#|>Concept<|#|>Alpha description\n<|COMPLETE|>");
    var rag = CreateLightRag(
        CreateLifecycleService(new InMemoryDocumentStatusStore()),
        textChunksStore: textChunksStore,
        llmCacheStore: llmCacheStore,
        llmService: llmService);

    await rag.InsertAsync("alpha beta gamma", docId: "doc-cache-list", filePath: "alpha.md");

    textChunksStore.Items.Should().NotBeEmpty();
    var chunk = textChunksStore.Items.Values.Single();
    chunk.Should().ContainKey("llm_cache_list");
    var cacheKeys = chunk["llm_cache_list"].Should().BeAssignableTo<List<object>>().Subject;
    cacheKeys.Should().ContainSingle(key => key.ToString()!.StartsWith("default:extract:", StringComparison.Ordinal));
    llmCacheStore.Items.Should().ContainKey(cacheKeys.Single().ToString()!);
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~InsertAsync_WritesExtractCacheKeysToTextChunks --verbosity minimal
```

Expected: fail because text chunks do not include `llm_cache_list`.

- [ ] **Step 3: Implement text chunk cache list write**

In `LightRAG.InsertAsync`, replace the `chunkData` projection with a result-aware projection:

```csharp
var chunkResultsById = chunkResults.ToDictionary(result => result.ChunkId, StringComparer.Ordinal);
var chunkData = chunks.ToDictionary(
    c => c.Id,
    c =>
    {
        var cacheKeys = chunkResultsById.TryGetValue(c.Id, out var result)
            ? result.LlmCacheKeys.Distinct(StringComparer.Ordinal).Cast<object>().ToList()
            : [];
        return new Dictionary<string, object>
        {
            ["content"] = c.Content,
            ["tokens"] = c.Tokens,
            ["chunk_order_index"] = c.ChunkOrderIndex,
            ["full_doc_id"] = c.FullDocId,
            ["file_path"] = c.FilePath,
            ["llm_cache_list"] = cacheKeys
        };
    });
```

- [ ] **Step 4: Run targeted test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~InsertAsync_WritesExtractCacheKeysToTextChunks --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Run deletion cache tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentDeletionServiceTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet\LightRAG.cs tests\LightRAGNet.Tests\DocumentLifecycle\LightRAGLifecycleIntegrationTests.cs
git commit -m "feat: persist extract cache references on chunks"
```

---

### Task 6: Add Summary Prompt Builder and Summary Cache

**Files:**
- Create: `src/LightRAGNet/Services/KnowledgeGraphMerge/SummaryPromptBuilder.cs`
- Modify: `src/LightRAGNet/Services/KnowledgeGraphMerge/DescriptionMerger.cs`
- Modify: `src/LightRAGNet/Services/KnowledgeGraphMerge/KnowledgeGraphMergeService.cs`
- Test: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/SummaryPromptBuilderTests.cs`
- Test: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/DescriptionMergerTests.cs`

- [ ] **Step 1: Write failing JSONL prompt test**

Create `tests/LightRAGNet.Tests/KnowledgeGraphMerge/SummaryPromptBuilderTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.KnowledgeGraphMerge;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class SummaryPromptBuilderTests
{
    [Fact]
    public void Build_UsesJsonLinesForDescriptions()
    {
        var prompt = SummaryPromptBuilder.Build(
            "entity",
            "Alpha",
            ["第一段", "第二段"],
            summaryLengthRecommended: 50);

        prompt.Should().Contain("{\"Description\":\"第一段\"}\n{\"Description\":\"第二段\"}");
        prompt.Should().NotContain("[\n");
        prompt.Should().Contain("entity Name: Alpha");
    }
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~SummaryPromptBuilderTests --verbosity minimal
```

Expected: fail because `SummaryPromptBuilder` does not exist.

- [ ] **Step 3: Implement summary prompt builder**

Create `src/LightRAGNet/Services/KnowledgeGraphMerge/SummaryPromptBuilder.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

internal static class SummaryPromptBuilder
{
    public static string Build(
        string descriptionType,
        string descriptionName,
        IReadOnlyCollection<string> descriptionList,
        int summaryLengthRecommended,
        string language = "English")
    {
        var descriptionsJsonl = string.Join(
            "\n",
            descriptionList.Select(description =>
                JsonSerializer.Serialize(new { Description = description }, LightRAGJsonOptions.Compact)));

        return $"""
                ---Role---
                You are a Knowledge Graph Specialist, proficient in data curation and synthesis.

                ---Task---
                Your task is to synthesize a list of descriptions of a given entity or relation into a single, comprehensive, and cohesive summary.

                ---Instructions---
                1. Input Format: The description list is provided in JSON Lines format, one JSON object per line.
                2. Output Format: The merged description will be returned as plain text, presented in multiple paragraphs.
                3. Comprehensiveness: The summary must integrate all key information from every provided description.
                4. Length Constraint: The summary's total length must not exceed {summaryLengthRecommended} tokens.
                5. Language: Write the summary in {language}.

                ---Input---
                {descriptionType} Name: {descriptionName}

                Description List:

                ```
                {descriptionsJsonl}
                ```

                ---Output---
                """;
    }
}
```

If `LightRAGJsonOptions.Compact` does not exist, add it in `src/LightRAGNet.Core/Utils/LightRAGJsonOptions.cs`:

```csharp
public static readonly JsonSerializerOptions Compact = new()
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
};
```

Add required `using System.Text.Encodings.Web;` if the file does not already have it.

- [ ] **Step 4: Write failing summary cache tests**

Extend `DescriptionMergerTests.cs` with a cache-backed helper and tests:

```csharp
[Fact]
public async Task MergeAsync_WhenSummaryCacheHit_DoesNotCallLlm()
{
    var llmService = Substitute.For<ILLMService>();
    var descriptions = new List<string> { "first", "second", "third" };
    var prompt = SummaryPromptBuilder.Build("entity", "Alice", descriptions, 50);
    var keyBuilder = new LightRagCacheKeyBuilder();
    var store = new InMemoryKvStore();
    store.Seed(
        keyBuilder.BuildSummaryKey(prompt),
        new LightRagCacheEntry(
            "cached summary",
            LightRagCacheKeyBuilder.SummaryCacheType,
            prompt,
            null,
            123).ToDictionary());
    var merger = CreateMerger(llmService, store);

    var result = await merger.MergeAsync("entity", "Alice", descriptions);

    result.Description.Should().Be("cached summary");
    result.LlmWasUsed.Should().BeTrue();
    await llmService.DidNotReceiveWithAnyArgs().SummarizeAsync(default!, default!, default!, default, default, default);
}

[Fact]
public async Task MergeAsync_WhenSummaryCacheMiss_SavesSummaryWithoutChunkId()
{
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("fresh summary");
    var store = new InMemoryKvStore();
    var merger = CreateMerger(llmService, store);

    var result = await merger.MergeAsync("entity", "Alice", ["first", "second", "third"]);

    result.Description.Should().Be("fresh summary");
    store.Items.Should().ContainSingle();
    var entry = store.Items.Values.Single();
    entry["cache_type"].Should().Be("summary");
    entry["chunk_id"].Should().BeNull();
}
```

Update `CreateMerger` helper:

```csharp
private static DescriptionMerger CreateMerger(
    ILLMService llmService,
    IKVStore? cacheStore = null)
{
    cacheStore ??= new InMemoryKvStore();
    var options = Options.Create(new LightRAGOptions
    {
        SummaryContextSize = 100,
        SummaryMaxTokens = 100,
        ForceLLMSummaryOnMerge = 3,
        SummaryLengthRecommended = 50
    });
    return new DescriptionMerger(
        llmService,
        new FakeTokenizer(),
        options,
        new LightRagLlmCacheService(
            cacheStore,
            options,
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance),
        NullLogger<DescriptionMerger>.Instance);
}
```

- [ ] **Step 5: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~SummaryPromptBuilderTests|FullyQualifiedName~DescriptionMergerTests" --verbosity minimal
```

Expected: fail because `DescriptionMerger` still calls `SummarizeAsync` directly and lacks cache service injection.

- [ ] **Step 6: Implement summary cache in DescriptionMerger**

Update constructor:

```csharp
internal class DescriptionMerger(
    ILLMService llmService,
    ITokenizer tokenizer,
    IOptions<LightRAGOptions> options,
    LightRagLlmCacheService llmCacheService,
    ILogger<DescriptionMerger> logger)
```

Add helper:

```csharp
private async Task<string> SummarizeWithCacheAsync(
    string descriptionType,
    string descriptionName,
    List<string> descriptions,
    CancellationToken cancellationToken)
{
    var prompt = SummaryPromptBuilder.Build(
        descriptionType,
        descriptionName,
        descriptions,
        _options.SummaryLengthRecommended);
    var cached = await llmCacheService.TryGetSummaryAsync(prompt, cancellationToken);
    if (cached is not null)
    {
        return cached;
    }

    var summary = await llmService.GenerateAsync(
        prompt,
        temperature: 0.3f,
        cancellationToken: cancellationToken);
    await llmCacheService.SaveSummaryAsync(prompt, summary, cancellationToken);
    return summary;
}
```

Replace direct calls to `llmService.SummarizeAsync(...)` with:

```csharp
var finalSummary = await SummarizeWithCacheAsync(
    descriptionType,
    descriptionName,
    currentList,
    cancellationToken);
```

and:

```csharp
var summary = await SummarizeWithCacheAsync(
    descriptionType,
    descriptionName,
    chunk,
    cancellationToken);
```

- [ ] **Step 7: Update KnowledgeGraphMergeService construction**

In `KnowledgeGraphMergeService`, resolve/inject `LightRagLlmCacheService` and pass it to `DescriptionMerger`.

If constructor injection is needed, add parameter:

```csharp
LightRagLlmCacheService llmCacheService,
```

Then create:

```csharp
var descriptionMerger = new DescriptionMerger(
    _llmService,
    _tokenizer,
    _options,
    _llmCacheService,
    _loggerFactory.CreateLogger<DescriptionMerger>());
```

- [ ] **Step 8: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~SummaryPromptBuilderTests|FullyQualifiedName~DescriptionMergerTests|FullyQualifiedName~KnowledgeGraphMerge" --verbosity minimal
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add src\LightRAGNet\Services\KnowledgeGraphMerge\SummaryPromptBuilder.cs src\LightRAGNet\Services\KnowledgeGraphMerge\DescriptionMerger.cs src\LightRAGNet\Services\KnowledgeGraphMerge\KnowledgeGraphMergeService.cs tests\LightRAGNet.Tests\KnowledgeGraphMerge\SummaryPromptBuilderTests.cs tests\LightRAGNet.Tests\KnowledgeGraphMerge\DescriptionMergerTests.cs
git commit -m "feat: cache graph summary llm responses"
```

---

### Task 7: Deletion and Regression Coverage

**Files:**
- Modify: `tests/LightRAGNet.Tests/DocumentDeletion/DocumentDeletionServiceTests.cs`
- Modify: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`
- Modify: `src/LightRAGNet.Example/CleanData.cs` only if old local cache cleanup needs an explicit helper

- [ ] **Step 1: Add deletion integration test for real extract key**

Add to `DocumentDeletionServiceTests.cs`:

```csharp
[Fact]
public async Task DeleteAsync_WhenDeleteLlmCacheTrue_DeletesPythonStyleExtractCacheKey()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    const string cacheKey = "default:extract:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object> { cacheKey } });
    fixture.LlmCache.Seed(
        cacheKey,
        new LightRagCacheEntry(
            "entity<|#|>Alpha<|#|>Concept<|#|>Description\n<|COMPLETE|>",
            LightRagCacheKeyBuilder.ExtractCacheType,
            "prompt",
            null,
            123,
            "chunk-a").ToDictionary());

    await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: true));

    fixture.LlmCache.DeleteCalls.SelectMany(call => call).Should().Contain(cacheKey);
}
```

- [ ] **Step 2: Add summary-not-linked deletion test**

Add:

```csharp
[Fact]
public async Task DeleteAsync_WhenSummaryCacheExistsButNotLinked_DoesNotDeleteSummaryCache()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    const string summaryKey = "default:summary:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object>() });
    fixture.LlmCache.Seed(
        summaryKey,
        new LightRagCacheEntry(
            "summary",
            LightRagCacheKeyBuilder.SummaryCacheType,
            "summary prompt",
            null,
            123).ToDictionary());

    await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: true));

    fixture.LlmCache.DeleteCalls.SelectMany(call => call).Should().NotContain(summaryKey);
    fixture.LlmCache.Items.Should().ContainKey(summaryKey);
}
```

- [ ] **Step 3: Run deletion tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentDeletionServiceTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Add re-index regression expectation**

In the existing lifecycle test for re-index after missing vectors, assert:

```csharp
await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
embeddingService.ReceivedCalls().Should().NotBeEmpty();
```

The exact `embeddingService` assertion should use the available substitute in that test. If the helper currently hides it, update the test to create and pass a local `IEmbeddingService` substitute so the assertion is direct:

```csharp
await embeddingService.Received().GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
```

- [ ] **Step 5: Run lifecycle tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~LightRAGLifecycleIntegrationTests --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add tests\LightRAGNet.Tests\DocumentDeletion\DocumentDeletionServiceTests.cs tests\LightRAGNet.Tests\DocumentLifecycle\LightRAGLifecycleIntegrationTests.cs
git commit -m "test: cover indexing cache deletion semantics"
```

---

### Task 8: Full Verification and Documentation Touch-Up

**Files:**
- Modify: `docs/superpowers/specs/2026-05-20-indexing-llm-cache-parity-design.md` only if implementation revealed a reviewed spec correction
- Modify: `README.md` only if public behavior/options need a short note

- [ ] **Step 1: Run focused cache tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessing|FullyQualifiedName~DescriptionMerger|FullyQualifiedName~SummaryPromptBuilder|FullyQualifiedName~DocumentDeletionServiceTests|FullyQualifiedName~LightRAGLifecycleIntegrationTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 2: Run full test suite**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: all test projects pass.

- [ ] **Step 3: Run full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 4: Search for legacy cache reads**

Run:

```powershell
rg -n "GetByIdAsync\\(cacheKey|cacheKey = chunk\\.Id|SerializeChunkResult|DeserializeChunkResult|\\[cacheKey\\] = cacheData" src tests
```

Expected: no references to legacy `chunk.Id -> ChunkResult` cache paths.

- [ ] **Step 5: Search for JSONL storage confusion**

Run:

```powershell
rg -n "jsonl|JSONL|Json Lines|llm_cache_list|default:extract|default:summary" docs\superpowers\specs src tests
```

Expected:

- summary prompt builder/tests mention JSONL
- keyed KV storage remains JSON object based
- extract cache keys appear in chunk/list/deletion tests
- summary cache keys do not appear in chunk `llm_cache_list` tests

- [ ] **Step 6: Run diff check**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 7: Commit verification/doc updates**

If Step 1-6 required no file changes:

```powershell
git status --short
```

Expected: clean working tree.

If README/spec wording changed:

```powershell
git add README.md docs\superpowers\specs\2026-05-20-indexing-llm-cache-parity-design.md
git commit -m "docs: clarify indexing cache behavior"
```

---

## Plan Self-Review

Spec coverage:

- Python-style `extract` and `summary` cache keys: Task 1 and Task 2.
- `EnableLlmCacheForEntityExtract`: Task 1 and Task 2.
- Remove legacy `chunk.Id -> ChunkResult`: Task 4 and Task 8.
- Entity extraction cache hit/miss: Task 4.
- `llm_cache_list` production: Task 5.
- Summary cache with JSONL prompt: Task 6.
- Summary cache not chunk-linked: Task 6 and Task 7.
- Deletion cleanup for extract cache only: Task 7.
- Full TDD and verification: every task has RED/GREEN commands; Task 8 covers full build/test/search.

Placeholder scan:

- No unfinished placeholder markers.
- No vague edge-case placeholders.
- Each code-changing task names exact files, test names, commands, expected failure/pass, and commit.

Type consistency:

- `LightRagCacheKeyBuilder.ExtractCacheType` and `SummaryCacheType` are introduced before use.
- `LightRagCacheEntry.ChunkId` is introduced before service and deletion tests use it.
- `ChunkResult.LlmCacheKeys` is introduced before `LightRAG.InsertAsync` reads it.
- `SummaryPromptBuilder.Build` signature is consistent between tests and implementation.
