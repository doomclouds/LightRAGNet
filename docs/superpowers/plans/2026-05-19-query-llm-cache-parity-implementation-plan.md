# Query LLM Cache Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded Python LightRAG parity for query-time keyword cache and non-streaming query answer cache while preventing stale RAG answers after document mutations.

**Architecture:** Add a focused `Services/QueryCache` unit that owns cache keys, cache value conversion, and workspace revision metadata over the existing `llm_cache` KV store. Keep `LightRAG` responsible for query control flow, but delegate all serialization and cache storage details to the cache service. Use workspace query revision in KG and Naive cache keys; skip query answer cache for streaming, prompt/context-only, and conversation-history queries.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, existing `IKVStore`, existing `LightRAGOptions`, existing `QueryParam` and `QueryMode`.

---

## File Structure

- Create `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`
  - Builds flattened `{mode}:{cache_type}:{sha256}` keys.
  - Normalizes scalar and list inputs deterministically.
  - Builds `metadata:query_revision:{workspace}` keys.
- Create `src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs`
  - Converts between typed cache entries and `Dictionary<string, object>` values used by `IKVStore`.
  - Reads string and numeric values from plain objects and `JsonElement`.
- Create `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
  - Reads and writes keyword cache entries.
  - Reads and writes query response cache entries.
  - Reads and bumps workspace query revision.
  - Swallows cache read/write failures with warnings so live query behavior remains available.
- Modify `src/LightRAGNet/LightRAGOptions.cs`
  - Add `EnableLlmCache`, `EnableQueryCache`, and `EnableKeywordCache`.
- Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Register `LightRagCacheKeyBuilder` and `LightRagLlmCacheService`.
- Modify `src/LightRAGNet/LightRAG.cs`
  - Inject `LightRagLlmCacheService`.
  - Use keyword cache for KG modes.
  - Use non-streaming query answer cache for KG, Naive, and Bypass.
  - Bump revision after successful insert and successful indexed delete.
- Modify `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
  - Bump revision after clear-all completes.
- Create tests under `tests/LightRAGNet.Tests/QueryCache/`
  - `LightRagCacheKeyBuilderTests.cs`
  - `LightRagLlmCacheServiceTests.cs`
  - `LightRAGKeywordCacheIntegrationTests.cs`
  - `LightRAGQueryCacheIntegrationTests.cs`
  - `LightRAGQueryRevisionTests.cs`
- Modify direct `new LightRAG(...)` construction sites in tests to pass the new cache service.

## Task 1: Cache Key And Entry Core

**Files:**
- Create: `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`
- Create: `src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagCacheKeyBuilderTests.cs`

- [ ] **Step 1: Write failing cache key tests**

Create `tests/LightRAGNet.Tests/QueryCache/LightRagCacheKeyBuilderTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRagCacheKeyBuilderTests
{
    [Fact]
    public void BuildRagQueryKey_SameInputs_ReturnsSameFlattenedKey()
    {
        var builder = new LightRagCacheKeyBuilder();
        var param = new QueryParam
        {
            Mode = QueryMode.Mix,
            ResponseType = "Multiple Paragraphs",
            TopK = 40,
            ChunkTopK = 20,
            MaxEntityTokens = 6000,
            MaxRelationTokens = 8000,
            MaxTotalTokens = 30000,
            UserPrompt = "answer briefly",
            EnableRerank = true
        };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["rag", "cache"],
            LowLevelKeywords = ["chunk", "entity"]
        };

        var first = builder.BuildRagQueryKey("workspace-a", 3, "What is cache?", param, keywords);
        var second = builder.BuildRagQueryKey("workspace-a", 3, "What is cache?", param, keywords);

        first.Should().Be(second);
        first.Should().StartWith("Mix:query:");
    }

    [Fact]
    public void BuildRagQueryKey_DifferentRevision_ReturnsDifferentKeys()
    {
        var builder = new LightRagCacheKeyBuilder();
        var param = new QueryParam { Mode = QueryMode.Naive };
        var keywords = new KeywordsResult();

        var before = builder.BuildRagQueryKey("workspace-a", 1, "question", param, keywords);
        var after = builder.BuildRagQueryKey("workspace-a", 2, "question", param, keywords);

        before.Should().NotBe(after);
    }

    [Fact]
    public void BuildBypassQueryKey_DoesNotUseWorkspaceRevision()
    {
        var builder = new LightRagCacheKeyBuilder();
        var param = new QueryParam
        {
            Mode = QueryMode.Bypass,
            ResponseType = "Multiple Paragraphs",
            UserPrompt = "tone"
        };

        var first = builder.BuildBypassQueryKey("question", param);
        var second = builder.BuildBypassQueryKey("question", param);

        first.Should().Be(second);
        first.Should().StartWith("Bypass:query:");
    }

    [Fact]
    public void BuildRagQueryKey_KeywordOrderChangesKey()
    {
        var builder = new LightRagCacheKeyBuilder();
        var param = new QueryParam { Mode = QueryMode.Mix };

        var first = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            param,
            new KeywordsResult { HighLevelKeywords = ["a", "b"] });
        var second = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            param,
            new KeywordsResult { HighLevelKeywords = ["b", "a"] });

        first.Should().NotBe(second);
    }

    [Fact]
    public void BuildRevisionKey_UsesWorkspaceMetadataKey()
    {
        var builder = new LightRagCacheKeyBuilder();

        builder.BuildRevisionKey("workspace-a").Should().Be("metadata:query_revision:workspace-a");
    }
}
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRagCacheKeyBuilderTests
```

Expected: FAIL because `LightRagCacheKeyBuilder` does not exist.

- [ ] **Step 3: Implement key builder**

Create `src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.QueryCache;

public sealed class LightRagCacheKeyBuilder
{
    public const string QueryCacheType = "query";
    public const string KeywordsCacheType = "keywords";
    public const string MetadataCacheType = "metadata";
    public const string DefaultLanguageMarker = "default";

    public string BuildKeywordKey(
        string workspace,
        QueryMode mode,
        string query,
        string? languageMarker = null)
    {
        return BuildFlattenedKey(
            mode,
            KeywordsCacheType,
            [
                Pair("workspace", workspace),
                Pair("query", query),
                Pair("language", languageMarker ?? DefaultLanguageMarker)
            ]);
    }

    public string BuildRagQueryKey(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords)
    {
        return BuildFlattenedKey(
            queryParam.Mode,
            QueryCacheType,
            [
                Pair("workspace", workspace),
                Pair("workspace_query_revision", workspaceQueryRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("query", query),
                Pair("response_type", queryParam.ResponseType),
                Pair("top_k", queryParam.TopK.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("chunk_top_k", queryParam.ChunkTopK.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("max_entity_tokens", queryParam.MaxEntityTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("max_relation_tokens", queryParam.MaxRelationTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("max_total_tokens", queryParam.MaxTotalTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Pair("high_level_keywords", JoinList(keywords.HighLevelKeywords)),
                Pair("low_level_keywords", JoinList(keywords.LowLevelKeywords)),
                Pair("user_prompt", queryParam.UserPrompt ?? string.Empty),
                Pair("enable_rerank", queryParam.EnableRerank ? "true" : "false")
            ]);
    }

    public string BuildBypassQueryKey(string query, QueryParam queryParam)
    {
        return BuildFlattenedKey(
            QueryMode.Bypass,
            QueryCacheType,
            [
                Pair("query", query),
                Pair("response_type", queryParam.ResponseType),
                Pair("user_prompt", queryParam.UserPrompt ?? string.Empty)
            ]);
    }

    public string BuildRevisionKey(string workspace)
    {
        return $"{MetadataCacheType}:query_revision:{workspace}";
    }

    private static KeyValuePair<string, string> Pair(string key, string value)
    {
        return new KeyValuePair<string, string>(key, value);
    }

    private static string JoinList(IEnumerable<string> values)
    {
        return string.Join("\u001e", values.Select(value => value.Trim()));
    }

    private static string BuildFlattenedKey(
        QueryMode mode,
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
}
```

- [ ] **Step 4: Implement cache entry conversion**

Create `src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightRAGNet.Services.QueryCache;

public sealed record LightRagCacheEntry(
    string ReturnValue,
    string CacheType,
    string OriginalPrompt,
    Dictionary<string, object?>? QueryParam,
    long CreateTime)
{
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["return"] = ReturnValue,
            ["cache_type"] = CacheType,
            ["original_prompt"] = OriginalPrompt,
            ["queryparam"] = QueryParam ?? new Dictionary<string, object?>(),
            ["create_time"] = CreateTime
        };
    }

    public static bool TryFromDictionary(
        Dictionary<string, object>? data,
        out LightRagCacheEntry entry)
    {
        entry = new LightRagCacheEntry(string.Empty, string.Empty, string.Empty, null, 0);
        if (data is null)
        {
            return false;
        }

        var returnValue = ReadString(data, "return");
        var cacheType = ReadString(data, "cache_type");
        if (string.IsNullOrEmpty(returnValue) || string.IsNullOrEmpty(cacheType))
        {
            return false;
        }

        entry = new LightRagCacheEntry(
            returnValue,
            cacheType,
            ReadString(data, "original_prompt"),
            ReadDictionary(data, "queryparam"),
            ReadInt64(data, "create_time"));
        return true;
    }

    private static string ReadString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
            JsonElement json => json.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static long ReadInt64(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number) => number,
            string text when long.TryParse(text, out var number) => number,
            _ => 0
        };
    }

    private static Dictionary<string, object?>? ReadDictionary(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object?> nullableDictionary)
        {
            return nullableDictionary;
        }

        if (value is Dictionary<string, object> dictionary)
        {
            return dictionary.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Object } json)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json.GetRawText());
        }

        return null;
    }
}
```

- [ ] **Step 5: Run cache key tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRagCacheKeyBuilderTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet/Services/QueryCache/LightRagCacheKeyBuilder.cs src/LightRAGNet/Services/QueryCache/LightRagCacheEntry.cs tests/LightRAGNet.Tests/QueryCache/LightRagCacheKeyBuilderTests.cs
git commit -m "feat: add query cache key builder"
```

## Task 2: Cache Service, Options, And DI

**Files:**
- Modify: `src/LightRAGNet/LightRAGOptions.cs`
- Create: `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`

- [ ] **Step 1: Write failing cache service tests**

Create `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRagLlmCacheServiceTests
{
    [Fact]
    public async Task TryGetKeywordsAsync_WhenCacheHit_ReturnsKeywords()
    {
        var store = new InMemoryKvStore();
        var builder = new LightRagCacheKeyBuilder();
        var key = builder.BuildKeywordKey("workspace-a", QueryMode.Mix, "question");
        store.Seed(key, new LightRagCacheEntry(
            JsonSerializer.Serialize(new
            {
                high_level_keywords = new[] { "rag" },
                low_level_keywords = new[] { "cache" }
            }),
            LightRagCacheKeyBuilder.KeywordsCacheType,
            "question",
            null,
            100).ToDictionary());
        var service = CreateService(store);

        var result = await service.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, "question");

        result.Should().NotBeNull();
        result!.HighLevelKeywords.Should().Equal("rag");
        result.LowLevelKeywords.Should().Equal("cache");
    }

    [Fact]
    public async Task TryGetKeywordsAsync_WhenCacheMalformed_ReturnsNull()
    {
        var store = new InMemoryKvStore();
        var builder = new LightRagCacheKeyBuilder();
        var key = builder.BuildKeywordKey("workspace-a", QueryMode.Mix, "question");
        store.Seed(key, new LightRagCacheEntry(
            "{bad json",
            LightRagCacheKeyBuilder.KeywordsCacheType,
            "question",
            null,
            100).ToDictionary());
        var service = CreateService(store);

        var result = await service.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, "question");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndGetQueryResponseAsync_RoundTripsResponse()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);
        var param = new QueryParam { Mode = QueryMode.Naive };
        var keywords = new KeywordsResult();

        await service.SaveQueryResponseAsync(
            "workspace-a",
            revision: 1,
            query: "question",
            param,
            keywords,
            response: "cached answer",
            originalPrompt: "question");
        var result = await service.TryGetQueryResponseAsync(
            "workspace-a",
            revision: 1,
            query: "question",
            param,
            keywords);

        result.Should().Be("cached answer");
    }

    [Fact]
    public async Task GetWorkspaceQueryRevisionAsync_WhenMissing_ReturnsZero()
    {
        var service = CreateService(new InMemoryKvStore());

        var revision = await service.GetWorkspaceQueryRevisionAsync("workspace-a");

        revision.Should().Be(0);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_IncrementsRevision()
    {
        var service = CreateService(new InMemoryKvStore());

        await service.BumpWorkspaceQueryRevisionAsync("workspace-a");
        await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        var revision = await service.GetWorkspaceQueryRevisionAsync("workspace-a");
        revision.Should().Be(2);
    }

    [Fact]
    public async Task TryGetQueryResponseAsync_WhenCacheDisabled_ReturnsNull()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store, new LightRAGOptions { EnableLlmCache = false });

        var result = await service.TryGetQueryResponseAsync(
            "workspace-a",
            revision: 1,
            query: "question",
            new QueryParam { Mode = QueryMode.Mix },
            new KeywordsResult());

        result.Should().BeNull();
    }

    private static LightRagLlmCacheService CreateService(
        InMemoryKvStore store,
        LightRAGOptions? options = null)
    {
        options ??= new LightRAGOptions { Workspace = "workspace-a" };
        return new LightRagLlmCacheService(
            store,
            Options.Create(options),
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);
    }
}
```

- [ ] **Step 2: Run the failing cache service tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRagLlmCacheServiceTests
```

Expected: FAIL because `LightRagLlmCacheService` and new options do not exist.

- [ ] **Step 3: Add cache options**

Modify `src/LightRAGNet/LightRAGOptions.cs` and add these properties near other top-level options:

```csharp
/// <summary>
/// Whether to enable all LLM cache reads and writes
/// </summary>
public bool EnableLlmCache { get; set; } = true;

/// <summary>
/// Whether to enable final query answer cache
/// </summary>
public bool EnableQueryCache { get; set; } = true;

/// <summary>
/// Whether to enable KG keyword extraction cache
/// </summary>
public bool EnableKeywordCache { get; set; } = true;
```

- [ ] **Step 4: Implement cache service**

Create `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.QueryCache;

public sealed class LightRagLlmCacheService(
    [FromKeyedServices(KVContracts.LLMCache)]
    IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    LightRagCacheKeyBuilder keyBuilder,
    ILogger<LightRagLlmCacheService> logger)
{
    private readonly LightRAGOptions _options = options.Value;

    public async Task<KeywordsResult?> TryGetKeywordsAsync(
        string workspace,
        QueryMode mode,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableLlmCache || !_options.EnableKeywordCache)
        {
            return null;
        }

        var key = keyBuilder.BuildKeywordKey(workspace, mode, query);
        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            if (!LightRagCacheEntry.TryFromDictionary(data, out var entry))
            {
                return null;
            }

            var keywords = JsonSerializer.Deserialize<KeywordCachePayload>(
                entry.ReturnValue,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (keywords is null)
            {
                return null;
            }

            return new KeywordsResult
            {
                HighLevelKeywords = keywords.HighLevelKeywords ?? [],
                LowLevelKeywords = keywords.LowLevelKeywords ?? []
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read keyword cache for query mode {Mode}.", mode);
            return null;
        }
    }

    public async Task SaveKeywordsAsync(
        string workspace,
        QueryMode mode,
        string query,
        KeywordsResult keywords,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableLlmCache || !_options.EnableKeywordCache)
        {
            return;
        }

        if (keywords.HighLevelKeywords.Count == 0 && keywords.LowLevelKeywords.Count == 0)
        {
            return;
        }

        var payload = new KeywordCachePayload
        {
            HighLevelKeywords = keywords.HighLevelKeywords,
            LowLevelKeywords = keywords.LowLevelKeywords
        };
        var entry = new LightRagCacheEntry(
            JsonSerializer.Serialize(payload),
            LightRagCacheKeyBuilder.KeywordsCacheType,
            query,
            QueryParam: null,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await SaveEntryAsync(keyBuilder.BuildKeywordKey(workspace, mode, query), entry, cancellationToken);
    }

    public async Task<string?> TryGetQueryResponseAsync(
        string workspace,
        long revision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableLlmCache || !_options.EnableQueryCache)
        {
            return null;
        }

        var key = queryParam.Mode == QueryMode.Bypass
            ? keyBuilder.BuildBypassQueryKey(query, queryParam)
            : keyBuilder.BuildRagQueryKey(workspace, revision, query, queryParam, keywords);

        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            return LightRagCacheEntry.TryFromDictionary(data, out var entry)
                ? entry.ReturnValue
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read query cache for query mode {Mode}.", queryParam.Mode);
            return null;
        }
    }

    public async Task SaveQueryResponseAsync(
        string workspace,
        long revision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        string response,
        string originalPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableLlmCache || !_options.EnableQueryCache || string.IsNullOrEmpty(response))
        {
            return;
        }

        var key = queryParam.Mode == QueryMode.Bypass
            ? keyBuilder.BuildBypassQueryKey(query, queryParam)
            : keyBuilder.BuildRagQueryKey(workspace, revision, query, queryParam, keywords);
        var entry = new LightRagCacheEntry(
            response,
            LightRagCacheKeyBuilder.QueryCacheType,
            originalPrompt,
            BuildQueryParamSnapshot(queryParam, keywords, revision),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await SaveEntryAsync(key, entry, cancellationToken);
    }

    public async Task<long> GetWorkspaceQueryRevisionAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var key = keyBuilder.BuildRevisionKey(workspace);
        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            return ReadRevision(data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read query cache revision for workspace {Workspace}.", workspace);
            return 0;
        }
    }

    public async Task<long> BumpWorkspaceQueryRevisionAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var current = await GetWorkspaceQueryRevisionAsync(workspace, cancellationToken);
        var next = current + 1;
        var data = new Dictionary<string, Dictionary<string, object>>
        {
            [keyBuilder.BuildRevisionKey(workspace)] = new()
            {
                ["revision"] = next,
                ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        try
        {
            await llmCacheStore.UpsertAsync(data, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to bump query cache revision for workspace {Workspace}.", workspace);
        }

        return next;
    }

    private async Task SaveEntryAsync(
        string key,
        LightRagCacheEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [key] = entry.ToDictionary()
            }, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save LLM cache entry {CacheKey}.", key);
        }
    }

    private static Dictionary<string, object?> BuildQueryParamSnapshot(
        QueryParam queryParam,
        KeywordsResult keywords,
        long revision)
    {
        return new Dictionary<string, object?>
        {
            ["mode"] = queryParam.Mode.ToString(),
            ["response_type"] = queryParam.ResponseType,
            ["top_k"] = queryParam.TopK,
            ["chunk_top_k"] = queryParam.ChunkTopK,
            ["max_entity_tokens"] = queryParam.MaxEntityTokens,
            ["max_relation_tokens"] = queryParam.MaxRelationTokens,
            ["max_total_tokens"] = queryParam.MaxTotalTokens,
            ["hl_keywords"] = keywords.HighLevelKeywords,
            ["ll_keywords"] = keywords.LowLevelKeywords,
            ["user_prompt"] = queryParam.UserPrompt ?? string.Empty,
            ["enable_rerank"] = queryParam.EnableRerank,
            ["workspace_query_revision"] = revision
        };
    }

    private static long ReadRevision(Dictionary<string, object>? data)
    {
        if (data is null || !data.TryGetValue("revision", out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number) => number,
            string text when long.TryParse(text, out var number) => number,
            _ => 0
        };
    }

    private sealed class KeywordCachePayload
    {
        [JsonPropertyName("high_level_keywords")]
        public List<string>? HighLevelKeywords { get; init; }

        [JsonPropertyName("low_level_keywords")]
        public List<string>? LowLevelKeywords { get; init; }
    }
}
```

- [ ] **Step 5: Register cache services**

Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`:

```csharp
using LightRAGNet.Services.QueryCache;
```

In the retrieval services region before `services.AddSingleton<LightRAG>();`, add:

```csharp
services.AddSingleton<LightRagCacheKeyBuilder>();
services.AddSingleton<LightRagLlmCacheService>();
```

- [ ] **Step 6: Run cache service tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRagLlmCacheServiceTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet/LightRAGOptions.cs src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs
git commit -m "feat: add query llm cache service"
```

## Task 3: Keyword Cache Integration

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: direct `new LightRAG(...)` test helpers found by `rg -n "new LightRAG\\(" tests src`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRAGKeywordCacheIntegrationTests.cs`

- [ ] **Step 1: Write failing keyword integration tests**

Create `tests/LightRAGNet.Tests/QueryCache/LightRAGKeywordCacheIntegrationTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRAGKeywordCacheIntegrationTests
{
    private const string NoContextMessage = "Sorry, I'm not able to provide an answer to that question.[no-context]";

    [Fact]
    public async Task QueryAsync_WhenKeywordCacheHit_SkipsKeywordExtraction()
    {
        var llmCacheStore = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var keywordKey = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "short question");
        llmCacheStore.Seed(keywordKey, new LightRagCacheEntry(
            JsonSerializer.Serialize(new
            {
                high_level_keywords = new[] { "topic" },
                low_level_keywords = new[] { "entity" }
            }),
            LightRagCacheKeyBuilder.KeywordsCacheType,
            "short question",
            null,
            100).ToDictionary());

        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

        var result = await rag.QueryAsync("short question", new QueryParam { Mode = QueryMode.Mix });

        result.Content.Should().Be(NoContextMessage);
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenKeywordCacheMiss_SavesExtractedKeywords()
    {
        var llmCacheStore = new InMemoryKvStore();
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync("short question", Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult
            {
                HighLevelKeywords = ["topic"],
                LowLevelKeywords = ["entity"]
            });
        var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

        await rag.QueryAsync("short question", new QueryParam { Mode = QueryMode.Mix });

        llmCacheStore.Items.Keys.Should().Contain(key => key.StartsWith("Mix:keywords:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryAsync_WhenSuppliedKeywords_SkipsKeywordCache()
    {
        var llmCacheStore = new InMemoryKvStore();
        var llmService = Substitute.For<ILLMService>();
        var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

        await rag.QueryAsync("short question", new QueryParam
        {
            Mode = QueryMode.Mix,
            HighLevelKeywords = ["supplied"],
            LowLevelKeywords = ["keywords"]
        });

        llmCacheStore.Items.Keys.Should().NotContain(key => key.Contains(":keywords:", StringComparison.Ordinal));
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    private static LightRAG CreateLightRag(
        ILLMService llmService,
        InMemoryKvStore? llmCacheStore = null)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        var tokenizer = new FakeTokenizer();
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        var vectorStore = Substitute.For<IVectorStore>();
        var graphStore = new InMemoryGraphStore();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        var fullEntitiesStore = new InMemoryKvStore();
        var fullRelationsStore = new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        llmCacheStore ??= new InMemoryKvStore();
        var statusStore = Substitute.For<IDocumentStatusStore>();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            options,
            NullLogger<DocumentLifecycleService>.Instance);
        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheStore,
            options,
            NullLogger<DocumentProcessingService>.Instance);
        var loggerFactory = NullLoggerFactory.Instance;
        var knowledgeGraphMergeService = new KnowledgeGraphMergeService(
            graphStore,
            vectorStore,
            embeddingService,
            llmService,
            tokenizer,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            options,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);
        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankService,
            tokenizer,
            textChunksStore,
            options,
            loggerFactory);
        var documentDeletionService = new DocumentDeletionService(
            vectorStore,
            graphStore,
            embeddingService,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            NullLogger<DocumentDeletionService>.Instance);
        var cacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);

        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(vectorStore, rerankService, tokenizer),
            cacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);
    }
}
```

- [ ] **Step 2: Run failing keyword integration tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGKeywordCacheIntegrationTests
```

Expected: FAIL because `LightRAG` does not accept `LightRagLlmCacheService` and does not read keyword cache.

- [ ] **Step 3: Inject cache service into LightRAG**

Modify the using list in `src/LightRAGNet/LightRAG.cs`:

```csharp
using LightRAGNet.Services.QueryCache;
```

Modify the primary constructor so `LightRagLlmCacheService` is after `NaiveQueryService`:

```csharp
NaiveQueryService naiveQueryService,
LightRagLlmCacheService llmCacheService,
ITokenizer tokenizer,
```

- [ ] **Step 4: Add keyword cache helper**

Add this private method in `src/LightRAGNet/LightRAG.cs` near `QueryAsync`:

```csharp
private async Task<KeywordsResult> GetKeywordsForKgQueryAsync(
    string query,
    QueryParam queryParam,
    CancellationToken cancellationToken)
{
    if (queryParam.HighLevelKeywords.Count > 0 || queryParam.LowLevelKeywords.Count > 0)
    {
        return new KeywordsResult
        {
            HighLevelKeywords = queryParam.HighLevelKeywords,
            LowLevelKeywords = queryParam.LowLevelKeywords
        };
    }

    var workspace = documentLifecycleService.GetDefaultWorkspace();
    var cachedKeywords = await llmCacheService.TryGetKeywordsAsync(
        workspace,
        queryParam.Mode,
        query,
        cancellationToken);
    if (cachedKeywords is not null)
    {
        return cachedKeywords;
    }

    var extractedKeywords = await llmService.ExtractKeywordsAsync(
        query,
        cancellationToken: cancellationToken);
    await llmCacheService.SaveKeywordsAsync(
        workspace,
        queryParam.Mode,
        query,
        extractedKeywords,
        cancellationToken);

    return extractedKeywords;
}
```

- [ ] **Step 5: Replace keyword extraction block in QueryAsync**

In `LightRAG.QueryAsync`, replace the existing keyword extraction block with:

```csharp
var keywords = await GetKeywordsForKgQueryAsync(query, queryParam, cancellationToken);
```

Keep the existing `QueryKeywordPolicy.NormalizeForKg` block immediately after this line.

- [ ] **Step 6: Update direct LightRAG constructors**

Run:

```powershell
rg -n "new LightRAG\(" tests src
```

For each direct constructor helper, create a `LightRagLlmCacheService` from the same `llmCacheStore` and pass it after `NaiveQueryService`:

```csharp
var cacheService = new LightRagLlmCacheService(
    llmCacheStore,
    options,
    new LightRagCacheKeyBuilder(),
    NullLogger<LightRagLlmCacheService>.Instance);
```

Constructor argument insert:

```csharp
new NaiveQueryService(vectorStore, rerankService, tokenizer),
cacheService,
tokenizer,
```

Add this using to modified test files:

```csharp
using LightRAGNet.Services.QueryCache;
```

- [ ] **Step 7: Run keyword integration tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGKeywordCacheIntegrationTests
```

Expected: PASS.

- [ ] **Step 8: Run existing query mode tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~LightRAGKeywordPolicyIntegrationTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests
git commit -m "feat: cache kg query keywords"
```

## Task 4: Query Answer Cache Integration

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRAGQueryCacheIntegrationTests.cs`

- [ ] **Step 1: Write failing query answer cache tests**

Create `tests/LightRAGNet.Tests/QueryCache/LightRAGQueryCacheIntegrationTests.cs` with the tests below and include the complete `CreateLightRag` helper body from Task 3 in the same file so the test can be executed independently:

```csharp
[Fact]
public async Task QueryAsync_WhenBypassCacheHit_SkipsGenerateAsync()
{
    var llmCacheStore = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var queryParam = new QueryParam { Mode = QueryMode.Bypass };
    var key = keyBuilder.BuildBypassQueryKey("raw question", queryParam);
    llmCacheStore.Seed(key, new LightRagCacheEntry(
        "cached bypass answer",
        LightRagCacheKeyBuilder.QueryCacheType,
        "raw question",
        null,
        100).ToDictionary());
    var llmService = Substitute.For<ILLMService>();
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", queryParam);

    result.Content.Should().Be("cached bypass answer");
    await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
}

[Fact]
public async Task QueryAsync_WhenBypassCacheMiss_SavesGeneratedAnswer()
{
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            "raw question",
            Arg.Is<string?>(prompt => prompt == null),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("fresh answer");
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", new QueryParam { Mode = QueryMode.Bypass });

    result.Content.Should().Be("fresh answer");
    llmCacheStore.Items.Keys.Should().Contain(key => key.StartsWith("Bypass:query:", StringComparison.Ordinal));
}

[Fact]
public async Task QueryAsync_WhenNaiveCacheHit_SkipsGenerateAsync()
{
    var llmCacheStore = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var queryParam = new QueryParam { Mode = QueryMode.Naive, EnableRerank = false };
    var key = keyBuilder.BuildRagQueryKey(
        "workspace-a",
        0,
        "alpha question",
        queryParam,
        new KeywordsResult());
    llmCacheStore.Seed(key, new LightRagCacheEntry(
        "cached naive answer",
        LightRagCacheKeyBuilder.QueryCacheType,
        "alpha question",
        null,
        100).ToDictionary());
    var llmService = Substitute.For<ILLMService>();
    var vectorStore = CreateVectorStoreWithChunk();
    var rag = CreateLightRag(llmService, vectorStore: vectorStore, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("alpha question", queryParam);

    result.Content.Should().Be("cached naive answer");
    await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
}

[Fact]
public async Task QueryAsync_WhenStreaming_SkipsQueryAnswerCache()
{
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateStreamAsync(
            "raw question",
            Arg.Is<string?>(prompt => prompt == null),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(AsyncValues("streamed"));
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", new QueryParam
    {
        Mode = QueryMode.Bypass,
        Stream = true
    });

    result.IsStreaming.Should().BeTrue();
    llmCacheStore.Items.Keys.Should().NotContain(key => key.Contains(":query:", StringComparison.Ordinal));
}

[Fact]
public async Task QueryAsync_WhenConversationHistoryPresent_SkipsQueryAnswerCache()
{
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    llmService.GenerateAsync(
            "raw question",
            Arg.Is<string?>(prompt => prompt == null),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns("history answer");
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", new QueryParam
    {
        Mode = QueryMode.Bypass,
        ConversationHistory =
        [
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "earlier")
        ]
    });

    result.Content.Should().Be("history answer");
    llmCacheStore.Items.Keys.Should().NotContain(key => key.Contains(":query:", StringComparison.Ordinal));
}

[Fact]
public async Task QueryAsync_WhenOnlyNeedPrompt_SkipsQueryAnswerCache()
{
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", new QueryParam
    {
        Mode = QueryMode.Bypass,
        OnlyNeedPrompt = true
    });

    result.Content.Should().Be("raw question");
    llmCacheStore.Items.Keys.Should().NotContain(key => key.Contains(":query:", StringComparison.Ordinal));
    await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
}

[Fact]
public async Task QueryAsync_WhenOnlyNeedContext_SkipsQueryAnswerCache()
{
    var llmCacheStore = new InMemoryKvStore();
    var llmService = Substitute.For<ILLMService>();
    var rag = CreateLightRag(llmService, llmCacheStore: llmCacheStore);

    var result = await rag.QueryAsync("raw question", new QueryParam
    {
        Mode = QueryMode.Bypass,
        OnlyNeedContext = true
    });

    result.Content.Should().BeEmpty();
    llmCacheStore.Items.Keys.Should().NotContain(key => key.Contains(":query:", StringComparison.Ordinal));
    await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
}
```

Also add these helper methods to the same test file:

```csharp
private static InMemoryVectorStore CreateVectorStoreWithChunk()
{
    var vectorStore = new InMemoryVectorStore();
    vectorStore.Seed("chunks", new SearchResult
    {
        Id = "chunk-a",
        Content = "alpha chunk content",
        Metadata = new Dictionary<string, object>
        {
            ["file_path"] = "docs/a.md"
        }
    });
    return vectorStore;
}

private static async IAsyncEnumerable<string> AsyncValues(params string[] values)
{
    foreach (var value in values)
    {
        await Task.Yield();
        yield return value;
    }
}
```

- [ ] **Step 2: Run failing query cache tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryCacheIntegrationTests
```

Expected: FAIL because `LightRAG` does not read query answer cache.

- [ ] **Step 3: Add cache eligibility helper**

Add this private method to `src/LightRAGNet/LightRAG.cs`:

```csharp
private static bool CanUseQueryAnswerCache(QueryParam queryParam)
{
    return !queryParam.Stream
           && !queryParam.OnlyNeedContext
           && !queryParam.OnlyNeedPrompt
           && queryParam.ConversationHistory.Count == 0;
}
```

- [ ] **Step 4: Add KG query answer cache**

In the KG branch of `LightRAG.QueryAsync`, after `OnlyNeedPrompt` handling and before non-streaming `GenerateAsync`, insert:

```csharp
var workspace = documentLifecycleService.GetDefaultWorkspace();
var revision = await llmCacheService.GetWorkspaceQueryRevisionAsync(workspace, cancellationToken);

if (CanUseQueryAnswerCache(queryParam))
{
    var cachedResponse = await llmCacheService.TryGetQueryResponseAsync(
        workspace,
        revision,
        query,
        queryParam,
        keywords,
        cancellationToken);
    if (cachedResponse is not null)
    {
        return new QueryResult
        {
            Content = cachedResponse,
            RawData = contextResult.RawData
        };
    }
}
```

After the existing non-streaming `GenerateAsync` call and before returning, save:

```csharp
await llmCacheService.SaveQueryResponseAsync(
    workspace,
    revision,
    query,
    queryParam,
    keywords,
    response,
    query,
    cancellationToken);
```

- [ ] **Step 5: Add Naive query answer cache**

In `QueryNaiveAsync`, after `OnlyNeedPrompt` handling and before streaming/non-streaming generation, add:

```csharp
var workspace = documentLifecycleService.GetDefaultWorkspace();
var revision = await llmCacheService.GetWorkspaceQueryRevisionAsync(workspace, cancellationToken);
var noKeywords = new KeywordsResult();

if (CanUseQueryAnswerCache(queryParam))
{
    var cachedResponse = await llmCacheService.TryGetQueryResponseAsync(
        workspace,
        revision,
        query,
        queryParam,
        noKeywords,
        cancellationToken);
    if (cachedResponse is not null)
    {
        return new QueryResult
        {
            Content = cachedResponse,
            RawData = contextResult.RawData
        };
    }
}
```

After the existing non-streaming `GenerateAsync` call and before returning, save:

```csharp
await llmCacheService.SaveQueryResponseAsync(
    workspace,
    revision,
    query,
    queryParam,
    noKeywords,
    response,
    query,
    cancellationToken);
```

- [ ] **Step 6: Add Bypass query answer cache**

In `QueryBypassAsync`, after `OnlyNeedPrompt` handling and before streaming handling, add:

```csharp
if (CanUseQueryAnswerCache(queryParam))
{
    var cachedResponse = await llmCacheService.TryGetQueryResponseAsync(
        documentLifecycleService.GetDefaultWorkspace(),
        revision: 0,
        query,
        queryParam,
        new KeywordsResult(),
        cancellationToken);
    if (cachedResponse is not null)
    {
        return new QueryResult
        {
            Content = cachedResponse,
            RawData = rawData
        };
    }
}
```

After the existing non-streaming Bypass `GenerateAsync` call and before returning, save:

```csharp
await llmCacheService.SaveQueryResponseAsync(
    documentLifecycleService.GetDefaultWorkspace(),
    revision: 0,
    query,
    queryParam,
    new KeywordsResult(),
    response,
    query,
    cancellationToken);
```

- [ ] **Step 7: Run query cache tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryCacheIntegrationTests
```

Expected: PASS.

- [ ] **Step 8: Run query test group**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~NaiveQueryServiceTests|FullyQualifiedName~LightRAGKeywordPolicyIntegrationTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests/QueryCache
git commit -m "feat: cache non-streaming query answers"
```

## Task 5: Workspace Revision Invalidation

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRAGQueryRevisionTests.cs`

- [ ] **Step 1: Write failing revision tests**

Create `tests/LightRAGNet.Tests/QueryCache/LightRAGQueryRevisionTests.cs` with the tests below and include a complete `CreateLightRag` helper in the same file:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRAGQueryRevisionTests
{
    [Fact]
    public async Task InsertAsync_WhenNewDocumentProcessed_BumpsQueryRevision()
    {
        var llmCacheStore = new InMemoryKvStore();
        var statusStore = new InMemoryDocumentStatusStore();
        var rag = CreateLightRag(
            llmCacheStore: llmCacheStore,
            statusStore: statusStore);
        var cacheService = CreateCacheService(llmCacheStore);

        await rag.InsertAsync("alpha beta gamma", filePath: "docs/a.md");

        var revision = await cacheService.GetWorkspaceQueryRevisionAsync("workspace-a");
        revision.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WhenProcessedDuplicate_DoesNotBumpQueryRevision()
    {
        var llmCacheStore = new InMemoryKvStore();
        var statusStore = new InMemoryDocumentStatusStore();
        var rag = CreateLightRag(
            llmCacheStore: llmCacheStore,
            statusStore: statusStore,
            tokenizer: new ThrowingTokenizer());
        var cacheService = CreateCacheService(llmCacheStore);
        await statusStore.UpsertAsync(new DocumentStatusRecord
        {
            Workspace = "workspace-a",
            DocId = "doc--duplicate",
            Status = DocumentLifecycleStatus.Processed,
            ContentSummary = "alpha beta gamma",
            ContentLength = "alpha beta gamma".Length,
            FilePath = "docs/a.md"
        });

        await rag.InsertAsync("alpha beta gamma", docId: "doc--duplicate", filePath: "docs/a.md");

        var revision = await cacheService.GetWorkspaceQueryRevisionAsync("workspace-a");
        revision.Should().Be(0);
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenIndexedDocumentDeleted_BumpsQueryRevision()
    {
        var llmCacheStore = new InMemoryKvStore();
        var statusStore = new InMemoryDocumentStatusStore();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        textChunksStore.Seed("chunk-a", new Dictionary<string, object> { ["content"] = "chunk text" });
        fullDocsStore.Seed("doc-1", new Dictionary<string, object>
        {
            ["content"] = "full doc",
            ["chunks_list"] = new List<object> { "chunk-a" }
        });
        await statusStore.UpsertAsync(new DocumentStatusRecord
        {
            Workspace = "workspace-a",
            DocId = "doc-1",
            Status = DocumentLifecycleStatus.Processed,
            ChunksList = ["chunk-a"],
            ChunksCount = 1
        });
        var rag = CreateLightRag(
            llmCacheStore: llmCacheStore,
            statusStore: statusStore,
            textChunksStore: textChunksStore,
            fullDocsStore: fullDocsStore);
        var cacheService = CreateCacheService(llmCacheStore);

        var result = await rag.DeleteDocumentAsync("doc-1");

        result.Succeeded.Should().BeTrue();
        var revision = await cacheService.GetWorkspaceQueryRevisionAsync("workspace-a");
        revision.Should().Be(1);
    }

    [Fact]
    public async Task QueryAsync_AfterRevisionBump_DoesNotUseOldNaiveCache()
    {
        var llmCacheStore = new InMemoryKvStore();
        var cacheService = CreateCacheService(llmCacheStore);
        var param = new QueryParam { Mode = QueryMode.Naive, EnableRerank = false };
        await cacheService.SaveQueryResponseAsync(
            "workspace-a",
            revision: 0,
            query: "alpha question",
            param,
            new KeywordsResult(),
            response: "old answer",
            originalPrompt: "alpha question");
        await cacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");
        var llmService = Substitute.For<ILLMService>();
        llmService.GenerateAsync(
                "alpha question",
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("fresh answer");
        var rag = CreateLightRag(
            llmService: llmService,
            llmCacheStore: llmCacheStore,
            vectorStore: CreateVectorStoreWithChunk());

        var result = await rag.QueryAsync("alpha question", param);

        result.Content.Should().Be("fresh answer");
    }
}
```

Add helper methods to the same file:

```csharp
private static LightRagLlmCacheService CreateCacheService(InMemoryKvStore llmCacheStore)
{
    return new LightRagLlmCacheService(
        llmCacheStore,
        Options.Create(new LightRAGOptions { Workspace = "workspace-a" }),
        new LightRagCacheKeyBuilder(),
        NullLogger<LightRagLlmCacheService>.Instance);
}

private sealed class ThrowingTokenizer : ITokenizer
{
    public List<int> Encode(string text)
    {
        throw new InvalidOperationException("Duplicate insert should not tokenize.");
    }

    public string Decode(List<int> tokens)
    {
        throw new InvalidOperationException("Duplicate insert should not decode.");
    }

    public int CountTokens(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
```

The `CreateLightRag` helper in this file must expose optional parameters for `statusStore`, `textChunksStore`, `fullDocsStore`, `vectorStore`, `llmService`, and `tokenizer`, then pass those values through to `DocumentLifecycleService`, `DocumentDeletionService`, `RetrievalContextService`, and the final `LightRAG` constructor.

- [ ] **Step 2: Run failing revision tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryRevisionTests
```

Expected: FAIL because insert/delete do not bump workspace query revision.

- [ ] **Step 3: Bump revision after successful insert**

In `LightRAG.InsertAsync`, immediately after `MarkProcessedAsync` succeeds, add:

```csharp
await llmCacheService.BumpWorkspaceQueryRevisionAsync(
    ingestion.Workspace,
    cancellationToken);
```

This must stay after successful persistence and lifecycle `processed` marking so failed ingestion does not invalidate cache.

- [ ] **Step 4: Bump revision after successful indexed delete**

In `LightRAG.DeleteDocumentAsync`, replace the direct return from `documentDeletionService.DeleteAsync` with:

```csharp
var result = await documentDeletionService.DeleteAsync(
    new DocumentDeletionRequest(
        plan.Workspace,
        docId,
        plan.ChunkIds,
        plan.DeleteLlmCache),
    cancellationToken);

if (result.Succeeded && plan.Found)
{
    await llmCacheService.BumpWorkspaceQueryRevisionAsync(plan.Workspace, cancellationToken);
}

return result;
```

This leaves unknown-document idempotent deletes from bumping revision when there is no RAG metadata to invalidate.

- [ ] **Step 5: Bump revision after clear-all completion**

Modify `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`.

Add using:

```csharp
using LightRAGNet.Services.QueryCache;
```

After KV stores and JSON files have been cleared, before returning the clear-all response, add:

```csharp
try
{
    var cacheService = serviceProvider.GetRequiredService<LightRagLlmCacheService>();
    var lightragOptions = serviceProvider.GetRequiredService<IOptions<LightRAGOptions>>().Value;
    var workspace = string.IsNullOrWhiteSpace(lightragOptions.Workspace)
        ? "_"
        : lightragOptions.Workspace;
    await cacheService.BumpWorkspaceQueryRevisionAsync(workspace);
    results.Add("Bumped query cache revision");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to bump query cache revision after clear-all: {Error}", ex.Message);
}
```

If the method already has a `CancellationToken`, pass it into `BumpWorkspaceQueryRevisionAsync`.

- [ ] **Step 6: Run revision tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGQueryRevisionTests
```

Expected: PASS.

- [ ] **Step 7: Run deletion and lifecycle regression tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRAGLifecycleIntegrationTests|FullyQualifiedName~DocumentDeletionServiceTests|FullyQualifiedName~RagTaskProcessorServiceTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src/LightRAGNet/LightRAG.cs src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs tests/LightRAGNet.Tests/QueryCache/LightRAGQueryRevisionTests.cs
git commit -m "feat: invalidate query cache on document changes"
```

## Task 6: Final Verification And Closeout

**Files:**
- Modify if needed: `docs/superpowers/archives/INDEX.md`
- Create after implementation is accepted: `docs/superpowers/archives/2026-05/2026-05-19-query-llm-cache-parity-archives.md`
- Update problem/inbox assets only if the implementation produces reusable failure knowledge.

- [ ] **Step 1: Run full unit and server tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
```

Expected: all test projects PASS.

- [ ] **Step 2: Run build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx
```

Expected: build succeeds with `0` errors. Investigate any new warnings introduced by this feature.

- [ ] **Step 3: Verify no accidental cache scope expansion**

Run:

```powershell
rg -n "EnableLlmCache|EnableQueryCache|EnableKeywordCache|LightRagLlmCacheService|BuildRagQueryKey|BumpWorkspaceQueryRevisionAsync" src tests docs/superpowers/specs docs/superpowers/plans
```

Expected:

- cache options only appear in `LightRAGOptions`, tests, and cache service usage
- query answer cache usage only appears in `LightRAG`
- revision bump appears in insert/delete/clear-all paths
- no embedding/entity/summary cache implementation appears

- [ ] **Step 4: Run spec coverage check manually**

Review `docs/superpowers/specs/2026-05-19-query-llm-cache-parity-design.md` and confirm:

- keyword cache implemented
- KG, Naive, Bypass non-streaming query answer cache implemented
- streaming cache skipped
- conversation history cache skipped
- prompt/context-only cache skipped
- revision invalidation implemented for insert/delete/clear-all
- cache failures degrade to live behavior
- no out-of-scope cache families implemented

- [ ] **Step 5: Run asset completion gate**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "query llm cache parity" --json
```

Expected: command exits successfully and reports whether an archive/problem route is needed.

- [ ] **Step 6: Archive completed requirement if implementation is accepted**

Create `docs/superpowers/archives/2026-05/2026-05-19-query-llm-cache-parity-archives.md` with:

```markdown
# Query LLM Cache Parity

- Date: `2026-05-19`
- Topic slug: `query-llm-cache-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `query-cache`, `keywords-cache`, `workspace-revision`, `tdd`

## Summary

本轮交付为 LightRAGNet 查询阶段补齐 Python LightRAG 风格的 LLM cache：KG keyword extraction 可复用缓存，KG/Naive/Bypass non-streaming query answer 可复用缓存，同时通过 workspace query revision 避免文档插入、删除或 clear-all 后复用旧 RAG 答案。

## Delivered Scope

- Added deterministic flattened cache keys for `keywords`, `query`, and `metadata`.
- Added `LightRagLlmCacheService` over the existing `llm_cache` KV store.
- Added options to disable all LLM cache, query answer cache, or keyword cache independently.
- Cached KG keyword extraction results.
- Cached KG, Naive, and Bypass non-streaming query answers.
- Skipped query answer cache for streaming, prompt/context-only, and conversation-history queries.
- Bumped workspace query revision after successful insert, successful indexed delete, and clear-all.

## Verification Snapshot

- `dotnet test .\LightRAGNet.slnx`: record the final pass/fail count from Step 1.
- `dotnet build .\LightRAGNet.slnx`: record the final error and warning count from Step 2.

## Source Documents

- Spec: [query llm cache parity design](../../specs/2026-05-19-query-llm-cache-parity-design.md)
- Plan: [query llm cache parity implementation plan](../../plans/2026-05-19-query-llm-cache-parity-implementation-plan.md)

## Related Problems

- None at archive time unless the problem gate creates or updates one.
```

Record the verification summaries before saving the archive. The archive content above is the required closeout shape for the implementation task.

- [ ] **Step 7: Commit closeout assets**

```powershell
git add docs/superpowers/archives docs/superpowers/problems docs/superpowers/inbox
git commit -m "docs: archive query llm cache parity"
```

If no problem or inbox assets changed, stage only the archive paths.
