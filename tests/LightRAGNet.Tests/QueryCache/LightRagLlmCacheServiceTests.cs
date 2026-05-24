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

    [Fact]
    public async Task TryGetKeywordsAsync_WhenCacheHit_ReturnsKeywords()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var key = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "what is cache?");
        store.Seed(
            key,
            new LightRagCacheEntry(
                """{"high_level_keywords":["rag","cache"],"low_level_keywords":["chunk"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, "what is cache?");

        result.Should().NotBeNull();
        result!.HighLevelKeywords.Should().Equal("rag", "cache");
        result.LowLevelKeywords.Should().Equal("chunk");
    }

    [Fact]
    public async Task TryGetKeywordsAsync_WhenCacheMalformed_ReturnsNull()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var key = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "what is cache?");
        store.Seed(
            key,
            new LightRagCacheEntry(
                """{"highLevelKeywords":["wrong-shape"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, "what is cache?");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveKeywordsAsync_PersistsChinesePayloadWithoutUnicodeEscapes()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);

        await service.SaveKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            "请用100字简述采集流程",
            new KeywordsResult
            {
                HighLevelKeywords = ["采集流程", "简述"],
                LowLevelKeywords = ["100字"]
            });

        store.Items.Should().ContainSingle();
        var entry = store.Items.Values.Single();
        var payload = entry["return"].Should().BeOfType<string>().Subject;
        payload.Should().Contain("采集流程");
        payload.Should().Contain("100字");
        payload.Should().NotContain("\\u91C7");
    }

    [Fact]
    public async Task SaveAndGetQueryResponseAsync_RoundTripsResponse()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Mix,
            ResponseType = "Single Paragraph",
            TopK = 7,
            ChunkTopK = 3,
            MaxEntityTokens = 100,
            MaxRelationTokens = 200,
            MaxTotalTokens = 300,
            UserPrompt = "answer shortly",
            EnableRerank = false
        };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["rag"],
            LowLevelKeywords = ["cache"]
        };

        await service.SaveQueryResponseAsync("workspace-a", 2, "what is cache?", queryParam, keywords, "cached answer");
        var result = await service.TryGetQueryResponseAsync("workspace-a", 2, "what is cache?", queryParam, keywords);

        result.Should().Be("cached answer");
        store.Items.Should().ContainSingle();
        var entry = store.Items.Values.Single();
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.QueryCacheType);
        entry["original_prompt"].Should().Be("what is cache?");
        var queryParamSnapshot = entry["queryparam"].Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        queryParamSnapshot.Should().ContainKeys(
            "mode",
            "response_type",
            "top_k",
            "chunk_top_k",
            "max_entity_tokens",
            "max_relation_tokens",
            "max_total_tokens",
            "hl_keywords",
            "ll_keywords",
            "user_prompt",
            "enable_rerank",
            "workspace_query_revision");
        queryParamSnapshot["workspace_query_revision"].Should().Be(2L);
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenCacheHit_DoesNotCallFactory()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Mix,
            ResponseType = "Single Paragraph",
            TopK = 7
        };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["rag"],
            LowLevelKeywords = ["cache"]
        };
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 3, "what is cache?", queryParam, keywords);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "cached answer",
                LightRagCacheKeyBuilder.QueryCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);
        var factoryCalls = 0;

        var result = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            3,
            "what is cache?",
            queryParam,
            keywords,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("factory answer");
            });

        result.Value.Should().Be("cached answer");
        result.Hit.Should().BeTrue();
        result.Saved.Should().BeFalse();
        result.CacheKey.Should().Be(key);
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task TryGetExtractAsync_WhenCacheHit_ReturnsRawResponse()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical extract prompt";
        var key = keyBuilder.BuildExtractKey(canonicalPrompt);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.TryGetExtractAsync(canonicalPrompt);

        result.Should().Be("raw extract response");
    }

    [Fact]
    public async Task SaveExtractAsync_PersistsPythonStyleEntry()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(store, keyBuilder: keyBuilder);
        const string canonicalPrompt = "canonical extract prompt";

        var key = await service.SaveExtractAsync(canonicalPrompt, "raw extract response", "chunk-a");

        key.Should().Be(keyBuilder.BuildExtractKey(canonicalPrompt));
        store.Items.Should().ContainKey(key!);
        var entry = store.Items[key!];
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.ExtractCacheType);
        entry["chunk_id"].Should().Be("chunk-a");
        entry["original_prompt"].Should().Be(canonicalPrompt);
        entry["return"].Should().Be("raw extract response");
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenCacheMiss_CallsFactoryAndSavesKey()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(store, keyBuilder: keyBuilder);
        const string canonicalPrompt = "canonical extract prompt";
        var expectedKey = keyBuilder.BuildExtractKey(canonicalPrompt);
        var factoryCalls = 0;

        var result = await service.GetOrCreateExtractAsync(
            canonicalPrompt,
            "chunk-a",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("raw extract response");
            });

        result.Value.Should().Be("raw extract response");
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.CacheKey.Should().Be(expectedKey);
        factoryCalls.Should().Be(1);
        store.Items.Should().ContainKey(expectedKey);
    }

    [Fact]
    public async Task TryGetExtractAsync_WhenIndexingCacheDisabled_DoesNotReadStore()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical extract prompt";
        store.Seed(
            keyBuilder.BuildExtractKey(canonicalPrompt),
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(
            store,
            new LightRAGOptions
            {
                EnableLlmCache = true,
                EnableLlmCacheForEntityExtract = false
            },
            keyBuilder);

        var result = await service.TryGetExtractAsync(canonicalPrompt);

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

        var key = await service.SaveExtractAsync("canonical extract prompt", "raw extract response", "chunk-a");

        key.Should().BeNull();
        store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveSummaryAsync_PersistsChunkIdAsNull()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(store, keyBuilder: keyBuilder);
        const string canonicalPrompt = "canonical summary prompt";

        var key = await service.SaveSummaryAsync(canonicalPrompt, "summary result");

        key.Should().Be(keyBuilder.BuildSummaryKey(canonicalPrompt));
        store.Items.Should().ContainKey(key!);
        var entry = store.Items[key!];
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        entry["chunk_id"].Should().BeNull();
        entry["original_prompt"].Should().Be(canonicalPrompt);
        entry["return"].Should().Be("summary result");
    }

    [Fact]
    public async Task GetOrCreateSummaryAsync_WhenIndexingCacheDisabled_CallsFactoryWithoutSaving()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(
            store,
            new LightRAGOptions
            {
                EnableLlmCache = true,
                EnableLlmCacheForEntityExtract = false
            });
        var factoryCalls = 0;

        var result = await service.GetOrCreateSummaryAsync(
            "canonical summary prompt",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("summary result");
            });

        result.Value.Should().Be("summary result");
        result.CacheEnabled.Should().BeFalse();
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeFalse();
        result.CacheKey.Should().BeNull();
        factoryCalls.Should().Be(1);
        store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task TryGetSummaryAsync_WhenCacheTypeMismatch_ReturnsNull()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical summary prompt";
        store.Seed(
            keyBuilder.BuildSummaryKey(canonicalPrompt),
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.TryGetSummaryAsync(canonicalPrompt);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkspaceQueryRevisionAsync_WhenMissing_ReturnsZero()
    {
        var service = CreateService(new InMemoryKvStore());

        var result = await service.GetWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(0);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_IncrementsRevision()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);

        var first = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");
        var second = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        first.Should().Be(1);
        second.Should().Be(2);
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(2);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_WhenRevisionReadFails_DoesNotOverwriteExistingRevision()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var revisionKey = keyBuilder.BuildRevisionKey("workspace-a");
        store.Seed(revisionKey, new Dictionary<string, object>
        {
            ["revision"] = 5L,
            ["updated_at"] = "2026-05-19T00:00:00.0000000Z"
        });
        store.ThrowOnGetKey = revisionKey;
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(0);
        store.UpsertCalls.Should().BeEmpty();
        store.ThrowOnGetKey = null;
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(5);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_WhenRevisionWriteFails_ReturnsCurrentRevision()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var revisionKey = keyBuilder.BuildRevisionKey("workspace-a");
        store.Seed(revisionKey, new Dictionary<string, object>
        {
            ["revision"] = 2L,
            ["updated_at"] = "2026-05-19T00:00:00.0000000Z"
        });
        store.ThrowOnUpsertKey = revisionKey;
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(2);
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(2);
    }

    [Fact]
    public async Task TryGetQueryResponseAsync_WhenCacheDisabled_ReturnsNull()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var queryParam = new QueryParam { Mode = QueryMode.Mix };
        var keywords = new KeywordsResult();
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 1, "what is cache?", queryParam, keywords);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "cached answer",
                LightRagCacheKeyBuilder.QueryCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(
            store,
            new LightRAGOptions { EnableLlmCache = false },
            keyBuilder);

        var result = await service.TryGetQueryResponseAsync("workspace-a", 1, "what is cache?", queryParam, keywords);

        result.Should().BeNull();
    }

    private static LightRagLlmCacheService CreateService(
        InMemoryKvStore store,
        LightRAGOptions? options = null,
        LightRagCacheKeyBuilder? keyBuilder = null,
        ICacheMetricsRecorder? recorder = null)
    {
        return new LightRagLlmCacheService(
            store,
            Options.Create(options ?? new LightRAGOptions()),
            keyBuilder ?? new LightRagCacheKeyBuilder(),
            recorder ?? new NoopCacheMetricsRecorder(),
            NullLogger<LightRagLlmCacheService>.Instance);
    }

    private sealed class NoopCacheMetricsRecorder : ICacheMetricsRecorder
    {
        public Task RecordReadAsync(
            string workspace,
            string cacheType,
            string outcome,
            string? mode,
            TimeSpan duration,
            TimeSpan? factoryDuration,
            string? cacheKey,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordSaveAsync(
            string workspace,
            string cacheType,
            string? mode,
            TimeSpan duration,
            string? cacheKey,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordClearAsync(
            string workspace,
            string cacheType,
            TimeSpan duration,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
