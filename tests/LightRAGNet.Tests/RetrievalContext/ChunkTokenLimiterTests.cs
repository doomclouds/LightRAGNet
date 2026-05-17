using FluentAssertions;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class ChunkTokenLimiterTests
{
    [Fact]
    public void Limit_PreservesChunksUntilTokenBudgetIsExceeded()
    {
        var limiter = new ChunkTokenLimiter(new FakeTokenizer());
        var chunks = new List<ChunkData>
        {
            new() { ChunkId = "chunk-1", Content = "one two", FilePath = "C:\\docs\\alpha.md" },
            new() { ChunkId = "chunk-2", Content = "three four five", FilePath = "C:\\docs\\beta.md" },
            new() { ChunkId = "chunk-3", Content = "six seven", FilePath = "C:\\docs\\gamma.md" }
        };

        var result = limiter.Limit(chunks, maxTokens: 5);

        result.Select(chunk => chunk.ChunkId).Should().Equal("chunk-1", "chunk-2");
    }

    [Fact]
    public void Limit_ReturnsEmptyWhenFirstChunkExceedsBudget()
    {
        var limiter = new ChunkTokenLimiter(new FakeTokenizer());
        var chunks = new List<ChunkData>
        {
            new() { ChunkId = "chunk-1", Content = "one two three", FilePath = "C:\\docs\\alpha.md" },
            new() { ChunkId = "chunk-2", Content = "four", FilePath = "C:\\docs\\beta.md" }
        };

        var result = limiter.Limit(chunks, maxTokens: 2);

        result.Should().BeEmpty();
    }
}
