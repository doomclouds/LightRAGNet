using FluentAssertions;
using LightRAGNet.Services.KnowledgeGraphMerge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class SourceIdsLimiterTests
{
    [Fact]
    public void ApplyLimit_WithFifoMethod_KeepsNewestIds()
    {
        var limiter = CreateLimiter("FIFO");

        var limitedIds = limiter.ApplyLimit(["a", "b", "c"], 2);

        limitedIds.Should().Equal("b", "c");
    }

    [Fact]
    public void ApplyLimit_WithKeepMethod_KeepsOldestIds()
    {
        var limiter = CreateLimiter("KEEP");

        var limitedIds = limiter.ApplyLimit(["a", "b", "c"], 2);

        limitedIds.Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplyLimit_WithNonPositiveLimit_ReturnsEmpty(int maxLimit)
    {
        var limiter = CreateLimiter("FIFO");

        var limitedIds = limiter.ApplyLimit(["a", "b", "c"], maxLimit);

        limitedIds.Should().BeEmpty();
    }

    [Fact]
    public void ComputeTruncationInfo_WhenNoTruncation_ReturnsEmpty()
    {
        var limiter = CreateLimiter("FIFO");
        var ids = new List<string> { "a", "b" };

        var truncationInfo = limiter.ComputeTruncationInfo(ids, ids);

        truncationInfo.Should().BeEmpty();
    }

    private static SourceIdsLimiter CreateLimiter(string sourceIdsLimitMethod)
    {
        return new SourceIdsLimiter(
            Options.Create(new LightRAGOptions
            {
                SourceIdsLimitMethod = sourceIdsLimitMethod
            }),
            NullLogger<SourceIdsLimiter>.Instance);
    }
}
