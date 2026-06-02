using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class ChunkingUtilitiesTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percentile_WhenPercentileOutOfRange_Throws(double percentile)
    {
        var act = () => ChunkingUtilities.Percentile([1.0, 2.0], percentile);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("percentile");
    }
}
