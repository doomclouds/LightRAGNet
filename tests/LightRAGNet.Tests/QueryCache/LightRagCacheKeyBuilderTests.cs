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
    public void BuildRagQueryKey_NullResponseType_DoesNotThrowAndUsesDefaultEquivalent()
    {
        var builder = new LightRagCacheKeyBuilder();
        var nullResponseTypeParam = new QueryParam { Mode = QueryMode.Mix, ResponseType = null! };
        var defaultResponseTypeParam = new QueryParam { Mode = QueryMode.Mix, ResponseType = "Multiple Paragraphs" };
        var keywords = new KeywordsResult();

        var nullResponseTypeKey = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            nullResponseTypeParam,
            keywords);
        var defaultResponseTypeKey = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            defaultResponseTypeParam,
            keywords);

        nullResponseTypeKey.Should().Be(defaultResponseTypeKey);
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
    public void BuildBypassQueryKey_NullResponseType_DoesNotThrowAndUsesDefaultEquivalent()
    {
        var builder = new LightRagCacheKeyBuilder();
        var nullResponseTypeParam = new QueryParam { Mode = QueryMode.Bypass, ResponseType = null! };
        var defaultResponseTypeParam = new QueryParam { Mode = QueryMode.Bypass, ResponseType = "Multiple Paragraphs" };

        var nullResponseTypeKey = builder.BuildBypassQueryKey("question", nullResponseTypeParam);
        var defaultResponseTypeKey = builder.BuildBypassQueryKey("question", defaultResponseTypeParam);

        nullResponseTypeKey.Should().Be(defaultResponseTypeKey);
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
    public void BuildRagQueryKey_KeywordSeparatorInsideItem_DoesNotCollideWithMultipleItems()
    {
        var builder = new LightRagCacheKeyBuilder();
        var param = new QueryParam { Mode = QueryMode.Mix };

        var first = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            param,
            new KeywordsResult { HighLevelKeywords = ["a\u001eb"] });
        var second = builder.BuildRagQueryKey(
            "workspace-a",
            1,
            "question",
            param,
            new KeywordsResult { HighLevelKeywords = ["a", "b"] });

        first.Should().NotBe(second);
    }

    [Fact]
    public void BuildRevisionKey_UsesWorkspaceMetadataKey()
    {
        var builder = new LightRagCacheKeyBuilder();

        builder.BuildRevisionKey("workspace-a").Should().Be("metadata:query_revision:workspace-a");
    }

    [Fact]
    public void LightRagCacheEntry_ToDictionaryRoundTripsNullableQueryParam()
    {
        var entry = new LightRagCacheEntry(
            "answer",
            "query",
            "prompt",
            new Dictionary<string, object?>
            {
                ["nullable"] = null,
                ["mode"] = "Mix"
            },
            123);

        LightRagCacheEntry.TryFromDictionary(entry.ToDictionary(), out var roundTripped).Should().BeTrue();

        roundTripped.QueryParam.Should().NotBeNull();
        roundTripped.QueryParam.Should().ContainKey("nullable");
        roundTripped.QueryParam!["nullable"].Should().BeNull();
        roundTripped.QueryParam["mode"].Should().Be("Mix");
    }
}
