using FluentAssertions;
using LightRAGNet;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class FixedTokenChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_UsesSlidingTokenWindowWithOverlap()
    {
        var strategy = new FixedTokenChunkingStrategy();
        var request = CreateRequest(
            "one two three four five six seven eight",
            chunkSize: 4,
            overlap: 1);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t7 t8");
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 2);
        chunks.Select(chunk => chunk.Order).Should().Equal(0, 1, 2);
        chunks.Select(chunk => chunk.Strategy).Should().AllBeEquivalentTo(LightRagChunkingStrategy.FixedToken);
    }

    [Fact]
    public async Task ChunkAsync_WhenSplitByCharacterOnlyAndSegmentExceedsLimit_Throws()
    {
        var strategy = new FixedTokenChunkingStrategy();
        var request = CreateRequest(
            "alpha beta gamma|delta",
            chunkSize: 2,
            overlap: 1,
            splitByCharacter: "|",
            splitByCharacterOnly: true);

        Func<Task> act = () => strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChunkDocumentAsync_MapsSegmentsToChunks()
    {
        var service = new LightRagChunkingService(
            [new FixedTokenChunkingStrategy()],
            new FakeTokenizer(),
            Options.Create(new LightRAGOptions
            {
                ChunkTokenSize = 4,
                ChunkOverlapTokenSize = 1
            }),
            NullLogger<LightRagChunkingService>.Instance);

        var chunks = await service.ChunkDocumentAsync(
            "one two three four five",
            "doc-1",
            "file.md");

        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.Content).Should().Equal("t1 t2 t3 t4", "t4 t5");
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 2);
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1);
        chunks.Select(chunk => chunk.FullDocId).Should().AllBeEquivalentTo("doc-1");
        chunks.Select(chunk => chunk.FilePath).Should().AllBeEquivalentTo("file.md");
        chunks.Select(chunk => chunk.Id).Should().OnlyContain(id => id.StartsWith("chunk-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChunkDocumentAsync_FiltersBlankSegmentsAndReindexesChunks()
    {
        var service = new LightRagChunkingService(
            [new FixedTokenChunkingStrategy()],
            new FakeTokenizer(),
            Options.Create(new LightRAGOptions
            {
                ChunkTokenSize = 4,
                ChunkOverlapTokenSize = 1,
                Chunking = new LightRagChunkingOptions
                {
                    FixedToken = new FixedTokenChunkingOptions
                    {
                        SplitByCharacter = "|"
                    }
                }
            }),
            NullLogger<LightRagChunkingService>.Instance);

        var chunks = await service.ChunkDocumentAsync(
            "alpha||beta",
            "doc-1",
            "file.md");

        chunks.Select(chunk => chunk.Content).Should().Equal("alpha", "beta");
        chunks.Select(chunk => chunk.Tokens).Should().Equal(1, 1);
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1);
        chunks.Should().OnlyContain(chunk => !string.IsNullOrWhiteSpace(chunk.Content));
    }

    private static ChunkingRequest CreateRequest(
        string content,
        int chunkSize,
        int overlap,
        string? splitByCharacter = null,
        bool splitByCharacterOnly = false)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.FixedToken,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, overlap, splitByCharacter, splitByCharacterOnly),
            new RecursiveCharacterChunkingSnapshot(chunkSize, overlap, ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(2000, overlap, 0));

        return new ChunkingRequest(content, "doc-1", "file.md", snapshot);
    }
}
