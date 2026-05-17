using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.KnowledgeGraphMerge;
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
        await llmService.DidNotReceive().SummarizeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<List<string>>(),
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_WhenBelowForceThreshold_JoinsDescriptionsWithoutLlm()
    {
        var llmService = Substitute.For<ILLMService>();
        var merger = CreateMerger(llmService);

        var result = await merger.MergeAsync("entity", "Alice", ["first", "second"]);

        result.Description.Should().Be("first<SEP>second");
        result.LlmWasUsed.Should().BeFalse();
        await llmService.DidNotReceive().SummarizeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<List<string>>(),
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_WhenForceThresholdReached_UsesLlmSummary()
    {
        var llmService = Substitute.For<ILLMService>();
        var descriptions = new List<string> { "first", "second", "third" };
        llmService
            .SummarizeAsync(
                "entity",
                "Alice",
                Arg.Is<List<string>>(list => list.SequenceEqual(descriptions)),
                50,
                Arg.Any<float>(),
                Arg.Any<CancellationToken>())
            .Returns("summary");
        var merger = CreateMerger(llmService);

        var result = await merger.MergeAsync("entity", "Alice", descriptions);

        result.Description.Should().Be("summary");
        result.LlmWasUsed.Should().BeTrue();
        await llmService.Received(1).SummarizeAsync(
            "entity",
            "Alice",
            Arg.Is<List<string>>(list => list.SequenceEqual(descriptions)),
            50,
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());
    }

    private static DescriptionMerger CreateMerger(ILLMService llmService)
    {
        return new DescriptionMerger(
            llmService,
            new FakeTokenizer(),
            Options.Create(new LightRAGOptions
            {
                SummaryContextSize = 100,
                SummaryMaxTokens = 100,
                ForceLLMSummaryOnMerge = 3,
                SummaryLengthRecommended = 50
            }),
            NullLogger<DescriptionMerger>.Instance);
    }
}
