using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class DescriptionMergerTests
{
    [Fact]
    public async Task MergeAsync_WithSingleDescription_ReturnsItWithoutLlm()
    {
        var llmService = Substitute.For<ILLMService>();
        var merger = CreateMerger(llmService);

        var result = await merger.MergeAsync("entity", "Alice", ["single description"]);

        result.Description.Should().Be("single description");
        result.LlmWasUsed.Should().BeFalse();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
    }

    [Fact]
    public async Task MergeAsync_WhenBelowForceThreshold_JoinsDescriptionsWithoutLlm()
    {
        var llmService = Substitute.For<ILLMService>();
        var merger = CreateMerger(llmService);

        var result = await merger.MergeAsync("entity", "Alice", ["first", "second"]);

        result.Description.Should().Be("first<SEP>second");
        result.LlmWasUsed.Should().BeFalse();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
    }

    [Fact]
    public async Task MergeAsync_WhenForceThresholdReached_UsesLlmSummary()
    {
        var llmService = Substitute.For<ILLMService>();
        var descriptions = new List<string> { "first", "second", "third" };
        llmService
            .GenerateAsync(
                Arg.Is<string>(prompt =>
                    prompt.Contains("entity Name: Alice", StringComparison.Ordinal)
                    && prompt.Contains("{\"Description\":\"first\"}", StringComparison.Ordinal)),
                null,
                null,
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("summary");
        var merger = CreateMerger(llmService);

        var result = await merger.MergeAsync("entity", "Alice", descriptions);

        result.Description.Should().Be("summary");
        result.LlmWasUsed.Should().BeTrue();
        await llmService.Received(1).GenerateAsync(
            Arg.Any<string>(),
            null,
            null,
            0.3f,
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

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
        var merger = CreateMerger(llmService, store, keyBuilder);

        var result = await merger.MergeAsync("entity", "Alice", descriptions);

        result.Description.Should().Be("cached summary");
        result.LlmWasUsed.Should().BeTrue();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
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
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        entry["chunk_id"].Should().BeNull();
    }

    [Fact]
    public async Task MergeAsync_WhenSummaryCacheMiss_CleansThinkTagsBeforeSaving()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService.GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("<think>internal reasoning</think>\nclean summary");
        var store = new InMemoryKvStore();
        var merger = CreateMerger(llmService, store);

        var result = await merger.MergeAsync("entity", "Alice", ["first", "second", "third"]);

        result.Description.Should().Be("clean summary");
        var entry = store.Items.Values.Single();
        entry["return"].Should().Be("clean summary");
    }

    private static DescriptionMerger CreateMerger(
        ILLMService llmService,
        IKVStore? cacheStore = null,
        LightRagCacheKeyBuilder? keyBuilder = null)
    {
        cacheStore ??= new InMemoryKvStore();
        keyBuilder ??= new LightRagCacheKeyBuilder();
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
                keyBuilder,
                NullLogger<LightRagLlmCacheService>.Instance),
            NullLogger<DescriptionMerger>.Instance);
    }
}
