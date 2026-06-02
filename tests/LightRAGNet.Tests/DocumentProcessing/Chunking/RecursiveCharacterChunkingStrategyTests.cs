using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class RecursiveCharacterChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_WhenInputIsEmpty_ReturnsEmptyList()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var request = CreateRequest(string.Empty, chunkSize: 3, overlap: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkAsync_UsesParagraphSeparatorBeforeWeakerSeparators()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var body = "alpha beta\n\ngamma delta\n\neta theta";
        var request = CreateRequest(body, chunkSize: 3, overlap: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "gamma delta",
            "eta theta");
        chunks.Select(chunk => chunk.Tokens).Should().Equal(2, 2, 2);
    }

    [Fact]
    public async Task ChunkAsync_WhenLongSentenceExceedsLimit_FallsThroughToTokenWindows()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var request = CreateRequest(
            "alpha beta gamma delta eta theta iota kappa",
            chunkSize: 3,
            overlap: 1);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 3);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta gamma",
            "gamma delta eta",
            "eta theta iota",
            "iota kappa");
    }

    [Fact]
    public async Task ChunkAsync_MergesSmallPieces()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var body = "alpha\nbeta\ngamma";
        var request = CreateRequest(body, chunkSize: 10, overlap: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be(body);
    }

    [Fact]
    public async Task ChunkAsync_WhenParagraphPiecesAreSmall_MergesThem()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var body = "alpha\n\nbeta\n\ngamma";
        var request = CreateRequest(body, chunkSize: 10, overlap: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be(body);
        chunks[0].SourceSpan.Should().Be(new SourceSpan(0, body.Length));
    }

    [Fact]
    public async Task ChunkAsync_WhenParagraphPiecesCanFitTogether_MergesAcrossParagraphSeparators()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var body = "alpha one\n\nbeta two\n\ngamma three\n\ndelta four";
        var request = CreateRequest(body, chunkSize: 4, overlap: 0);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha one\n\nbeta two",
            "gamma three\n\ndelta four");
        chunks.Select(chunk => chunk.SourceSpan).Should().Equal(
            new SourceSpan(0, 19),
            new SourceSpan(21, body.Length));
    }

    [Fact]
    public async Task ChunkAsync_WhenTextRepeats_MovesSourceSpansForward()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var body = "alpha beta\n\nalpha beta\n\nalpha beta";
        var request = CreateRequest(body, chunkSize: 3, overlap: 0);

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

    [Fact]
    public async Task ChunkAsync_WhenManualSnapshotOverlapExceedsChunkSize_ClampsOverlap()
    {
        var strategy = new RecursiveCharacterChunkingStrategy();
        var request = CreateRequest(
            "alpha beta gamma delta eta",
            chunkSize: 2,
            overlap: 99,
            separators: []);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Should().HaveCountLessThan(10);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 2);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "beta gamma",
            "gamma delta",
            "delta eta");
    }

    private static ChunkingRequest CreateRequest(
        string content,
        int chunkSize,
        int overlap,
        IReadOnlyList<string>? separators = null)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.RecursiveCharacter,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, overlap, null, false),
            new RecursiveCharacterChunkingSnapshot(
                chunkSize,
                overlap,
                separators ?? ["\n\n", "\n", " ", ""]),
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
