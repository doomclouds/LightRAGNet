using FluentAssertions;
using LightRAGNet.Services.RetrievalContext;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class ReferenceListBuilderTests
{
    [Fact]
    public void Build_AssignsSameReferenceIdToChunksWithSameFilePath()
    {
        var builder = new ReferenceListBuilder();
        var chunks = new List<ChunkData>
        {
            new() { ChunkId = "chunk-1", Content = "one", FilePath = "C:\\docs\\alpha.md" },
            new() { ChunkId = "chunk-2", Content = "two", FilePath = "C:\\docs\\beta.md" },
            new() { ChunkId = "chunk-3", Content = "three", FilePath = "C:\\docs\\alpha.md" },
            new() { ChunkId = "chunk-4", Content = "four", FilePath = "unknown_source" },
            new() { ChunkId = "chunk-5", Content = "five", FilePath = "" }
        };

        var (references, chunksWithRefIds) = builder.Build(chunks);

        references.Select(reference => reference.FilePath).Should().Equal(
            "C:\\docs\\alpha.md",
            "C:\\docs\\beta.md");
        references.Select(reference => reference.ReferenceId).Should().Equal("1", "2");
        chunksWithRefIds.Select(chunk => chunk.ReferenceId).Should().Equal("1", "2", "1", "", "");
    }

    [Fact]
    public void Build_DecodesFileNameFromUrl()
    {
        var fileName = ReferenceListBuilder.ExtractFileName(
            "https://example.com/docs/%E5%91%A8%E6%8A%A5%202026.md");

        fileName.Should().Be("周报 2026.md");
    }
}
