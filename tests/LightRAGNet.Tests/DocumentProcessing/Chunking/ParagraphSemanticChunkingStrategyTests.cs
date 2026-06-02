using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class ParagraphSemanticChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_DoesNotMergeAcrossTopLevelHeadings()
    {
        const string markdown = """
                                # A
                                alpha beta

                                # B
                                gamma delta
                                """;
        var strategy = CreateStrategy();

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 20),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCount(2);
        chunks.Select(chunk => chunk.Strategy).Should().AllBeEquivalentTo(LightRagChunkingStrategy.ParagraphSemantic);
        chunks[0].Heading.Should().BeEquivalentTo(new ChunkHeading(1, "A", []));
        chunks[1].Heading.Should().BeEquivalentTo(new ChunkHeading(1, "B", []));
    }

    [Fact]
    public async Task ChunkAsync_LongSingleBlockFallsBackToRecursive()
    {
        var markdown = "# Long\n" + string.Join(" ", Enumerable.Range(0, 14).Select(index => $"word{index}"));
        var strategy = CreateStrategy();

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 5),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.ParagraphSemantic);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 5);
        chunks.Select(chunk => chunk.Heading!.Heading).Should().Equal(
            "Long [part 1]",
            "Long [part 2]",
            "Long [part 3]");
    }

    [Fact]
    public async Task ChunkAsync_TableSplitsByRowsBeforeRecursiveFallback()
    {
        const string markdown = """
                                # Data
                                | A | B |
                                | - | - |
                                | alpha | beta |
                                | gamma | delta |
                                | eta | theta |
                                """;
        var strategy = CreateStrategy();

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 15),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.ParagraphSemantic);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 15);
        chunks.Should().OnlyContain(chunk => chunk.Content.StartsWith("| A | B |\n| - | - |", StringComparison.Ordinal));
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains("| alpha | beta |", StringComparison.Ordinal));
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains("| gamma | delta |", StringComparison.Ordinal));
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains("| eta | theta |", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChunkAsync_MergesSmallBlocksWithinSameHeading()
    {
        const string markdown = """
                                # A
                                alpha beta

                                | K | V |
                                | - | - |
                                | gamma | delta |

                                epsilon zeta
                                """;
        var strategy = CreateStrategy();

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 30),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].Heading.Should().BeEquivalentTo(new ChunkHeading(1, "A", []));
        chunks[0].Content.Should().Contain("alpha beta");
        chunks[0].Content.Should().Contain("| gamma | delta |");
        chunks[0].Content.Should().Contain("epsilon zeta");
    }

    [Fact]
    public async Task ChunkAsync_LongBlockSourceSpansMapBackToOriginalMarkdown()
    {
        var body = string.Join(" ", Enumerable.Range(0, 8).Select(index => $"word{index}"));
        var markdown = "# Long\n" + body;
        var bodyStart = markdown.IndexOf(body, StringComparison.Ordinal);
        var strategy = CreateStrategy();

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 3),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Select(chunk => chunk.SourceSpan).Should().NotContainNulls();
        chunks[0].SourceSpan!.Start.Should().Be(bodyStart);
        foreach (var chunk in chunks)
        {
            var span = chunk.SourceSpan!;
            span.Start.Should().BeGreaterThanOrEqualTo(bodyStart);
            markdown[span.Start..span.End].Should().Be(chunk.Content);
        }
    }

    private static ParagraphSemanticChunkingStrategy CreateStrategy()
    {
        return new ParagraphSemanticChunkingStrategy(new RecursiveCharacterChunkingStrategy());
    }

    private static ChunkingRequest CreateRequest(
        string content,
        int chunkSize,
        int overlap = 0,
        int minChunkTokenSize = 0)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.ParagraphSemantic,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, overlap, null, false),
            new RecursiveCharacterChunkingSnapshot(
                chunkSize,
                overlap,
                ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                SemanticVectorChunkingOptions.DefaultSentenceSplitRegex,
                true),
            new ParagraphSemanticChunkingSnapshot(chunkSize, overlap, minChunkTokenSize));

        return new ChunkingRequest(content, "doc-p", "paragraph.md", snapshot);
    }
}
