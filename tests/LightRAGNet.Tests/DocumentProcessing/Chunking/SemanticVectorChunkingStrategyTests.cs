using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class SemanticVectorChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_WhenEmbeddingUnavailable_FallsBackToRecursive()
    {
        var strategy = new SemanticVectorChunkingStrategy(
            null,
            new RecursiveCharacterChunkingStrategy(),
            NullLogger<SemanticVectorChunkingStrategy>.Instance);
        var request = CreateRequest(
            "alpha beta gamma delta",
            chunkSize: 2,
            fallbackWhenEmbeddingUnavailable: true);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.RecursiveCharacter);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 2);
    }

    [Fact]
    public async Task ChunkAsync_WhenEmbeddingUnavailableAndFallbackDisabled_Throws()
    {
        var strategy = new SemanticVectorChunkingStrategy(
            null,
            new RecursiveCharacterChunkingStrategy(),
            NullLogger<SemanticVectorChunkingStrategy>.Instance);
        var request = CreateRequest(
            "alpha beta.",
            chunkSize: 10,
            fallbackWhenEmbeddingUnavailable: false);

        var act = () => strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Semantic vector chunking*embedding service*");
    }

    [Fact]
    public async Task ChunkAsync_WhenEmbeddingProviderFails_BubblesException()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<float[][]>>(_ => throw new InvalidOperationException("provider down"));
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest("Alpha one. Beta two.", chunkSize: 10);

        var act = () => strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("provider down");
    }

    [Fact]
    public async Task ChunkAsync_NumberOfChunksControlsBreakpointCount()
    {
        var embedding = CreateEmbedding(
        [
            [1f, 0f],
            [0.9f, 0.1f],
            [0f, 1f],
            [0.1f, 0.9f]
        ]);
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest(
            "A one. A two. B one. B two.",
            chunkSize: 100,
            numberOfChunks: 2,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.Strategy).Should().AllBeEquivalentTo(LightRagChunkingStrategy.SemanticVector);
    }

    [Fact]
    public async Task ChunkAsync_WhenSemanticGroupExceedsLimit_ResplitsWithRecursive()
    {
        var embedding = CreateEmbedding(
        [
            [1f, 0f],
            [0.99f, 0.01f],
            [0.98f, 0.02f]
        ]);
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest(
            "alpha beta. gamma delta. epsilon zeta.",
            chunkSize: 2,
            thresholdAmount: 100,
            recursiveOverlap: 1,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.RecursiveCharacter);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 2);
    }

    [Fact]
    public async Task ChunkAsync_WhenMinChunkTokenSizeIsSet_MergesTinyGroups()
    {
        var embedding = CreateEmbedding(
        [
            [1f, 0f],
            [0f, 1f],
            [1f, 0f]
        ]);
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest(
            "one. two. three four five.",
            chunkSize: 6,
            numberOfChunks: 3,
            minChunkTokenSize: 2,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.Content).Should().Equal("one. two.", "three four five.");
        chunks.Should().OnlyContain(chunk => chunk.Tokens >= 2);
    }

    [Fact]
    public async Task ChunkAsync_WhenChineseSentencesHaveNoSpaces_SplitsSentences()
    {
        var embeddedTexts = new List<string>();
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var texts = call.ArgAt<IEnumerable<string>>(0).ToList();
                embeddedTexts.AddRange(texts);
                return
                [
                    [1f, 0f],
                    [0.9f, 0.1f],
                    [0f, 1f],
                    [0.1f, 0.9f]
                ];
            });
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest(
            "甲。乙？丙！丁。",
            chunkSize: 100,
            numberOfChunks: 2,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        embeddedTexts.Should().Equal("甲。", "乙？", "丙！", "丁。");
        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.SourceSpan).Should().Equal(
            new SourceSpan(0, 4),
            new SourceSpan(4, 8));
    }

    [Fact]
    public async Task ChunkAsync_WhenTextRepeats_MovesSourceSpansForward()
    {
        var embedding = CreateEmbedding(
        [
            [1f, 0f],
            [0f, 1f],
            [1f, 0f]
        ]);
        var strategy = CreateStrategy(embedding);
        var body = "same. same. same.";
        var request = CreateRequest(
            body,
            chunkSize: 100,
            numberOfChunks: 3,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.SourceSpan).Should().NotContainNulls();
        chunks.Select(chunk => chunk.SourceSpan!.Start).Should().BeInAscendingOrder();
        foreach (var chunk in chunks)
        {
            var span = chunk.SourceSpan!;
            body[span.Start..span.End].Should().Be(chunk.Content);
        }
    }

    [Theory]
    [InlineData(SemanticVectorBreakpointThresholdType.Percentile, 50)]
    [InlineData(SemanticVectorBreakpointThresholdType.StandardDeviation, 0)]
    [InlineData(SemanticVectorBreakpointThresholdType.Interquartile, 0)]
    [InlineData(SemanticVectorBreakpointThresholdType.Gradient, 50)]
    public async Task ChunkAsync_SupportsBreakpointThresholdTypes(
        SemanticVectorBreakpointThresholdType thresholdType,
        double thresholdAmount)
    {
        var embedding = CreateEmbedding(
        [
            [1f, 0f],
            [0.95f, 0.05f],
            [0f, 1f],
            [0.05f, 0.95f]
        ]);
        var strategy = CreateStrategy(embedding);
        var request = CreateRequest(
            "A one. A two. B one. B two.",
            chunkSize: 100,
            thresholdType: thresholdType,
            thresholdAmount: thresholdAmount,
            bufferSize: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.SemanticVector);
    }

    private static SemanticVectorChunkingStrategy CreateStrategy(IEmbeddingService embedding)
    {
        return new SemanticVectorChunkingStrategy(
            embedding,
            new RecursiveCharacterChunkingStrategy(),
            NullLogger<SemanticVectorChunkingStrategy>.Instance);
    }

    private static IEmbeddingService CreateEmbedding(float[][] embeddings)
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(embeddings);
        return embedding;
    }

    private static ChunkingRequest CreateRequest(
        string content,
        int chunkSize,
        SemanticVectorBreakpointThresholdType thresholdType = SemanticVectorBreakpointThresholdType.Percentile,
        double? thresholdAmount = 80,
        int? numberOfChunks = null,
        int minChunkTokenSize = 0,
        int bufferSize = 1,
        int recursiveOverlap = 0,
        bool fallbackWhenEmbeddingUnavailable = true)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.SemanticVector,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, recursiveOverlap, null, false),
            new RecursiveCharacterChunkingSnapshot(
                chunkSize,
                recursiveOverlap,
                ["\n\n", "\n", "。", "！", "？", "；", "，", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                thresholdType,
                thresholdAmount,
                bufferSize,
                numberOfChunks,
                null,
                minChunkTokenSize,
                SemanticVectorChunkingOptions.DefaultSentenceSplitRegex,
                fallbackWhenEmbeddingUnavailable),
            new ParagraphSemanticChunkingSnapshot(2000, recursiveOverlap, 0));

        return new ChunkingRequest(content, "doc-v", "vector.md", snapshot);
    }
}
