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
        LightRagCacheKeyBuilder? keyBuilder = null)
    {
        return new LightRagLlmCacheService(
            store,
            Options.Create(options ?? new LightRAGOptions()),
            keyBuilder ?? new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);
    }
}
