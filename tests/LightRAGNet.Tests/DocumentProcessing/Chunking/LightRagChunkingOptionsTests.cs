using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class LightRagChunkingOptionsTests
{
    [Fact]
    public void Normalize_WhenUnset_DefaultsToFixedToken()
    {
        var options = new LightRAGOptions();

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.FixedToken);
        snapshot.ChunkTokenSize.Should().Be(1200);
        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(100);
    }

    [Fact]
    public void Normalize_ParagraphSemantic_DefaultsToTwoThousandTokens()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 1200,
            Chunking = new LightRagChunkingOptions
            {
                Strategy = LightRagChunkingStrategy.ParagraphSemantic
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.ParagraphSemantic);
        snapshot.ParagraphSemantic.ChunkTokenSize.Should().Be(2000);
    }

    [Fact]
    public void Normalize_WhenOverlapExceedsChunkSize_ClampsOverlap()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 5,
            ChunkOverlapTokenSize = 12
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(4);
    }

    [Fact]
    public void Normalize_WhenChunkSizeIsZero_Throws()
    {
        var options = new LightRAGOptions { ChunkTokenSize = 0 };

        var act = () => options.CreateChunkingSnapshot();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ChunkTokenSize*greater than zero*");
    }
}
