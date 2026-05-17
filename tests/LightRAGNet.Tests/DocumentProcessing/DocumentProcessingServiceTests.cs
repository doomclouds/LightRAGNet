using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class DocumentProcessingServiceTests
{
    [Fact]
    public void ChunkDocument_TrimsContentBeforeTokenization()
    {
        var service = CreateService(chunkSize: 10, overlap: 2);

        var chunks = service.ChunkDocument("  alpha beta  ", "doc-1", "file.md");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("t1 t2");
        chunks[0].Tokens.Should().Be(2);
        chunks[0].FullDocId.Should().Be("doc-1");
        chunks[0].FilePath.Should().Be("file.md");
    }

    [Fact]
    public void ChunkDocument_UsesSlidingTokenWindowWithOverlap()
    {
        var service = CreateService(chunkSize: 4, overlap: 1);

        var chunks = service.ChunkDocument(
            "one two three four five six seven eight",
            "doc-1");

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 2);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t7 t8");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void ChunkDocument_MergesTinyTrailingFragmentIntoPreviousChunk()
    {
        var service = CreateService(chunkSize: 4, overlap: 1);

        var chunks = service.ChunkDocument(
            "one two three four five six seven eight nine ten",
            "doc-1");

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 5);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t1 t2 t3 t4 t10");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void ChunkDocument_SplitsByCharacter()
    {
        var service = CreateService(chunkSize: 3, overlap: 1);

        var chunks = service.ChunkDocument(
            "alpha beta|gamma delta epsilon zeta|eta",
            "doc-1",
            splitByCharacter: "|");

        chunks.Should().HaveCount(4);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(2, 3, 2, 1);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "t1 t2 t3",
            "t3 t4",
            "eta");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void ChunkDocument_WhenSplitByCharacterOnlyAndSegmentExceedsLimit_Throws()
    {
        var service = CreateService(chunkSize: 2, overlap: 1);

        var act = () => service.ChunkDocument(
            "alpha beta gamma|delta",
            "doc-1",
            splitByCharacter: "|",
            splitByCharacterOnly: true);

        act.Should().Throw<InvalidOperationException>();
    }

    private static DocumentProcessingService CreateService(int chunkSize, int overlap)
    {
        return new DocumentProcessingService(
            Substitute.For<ILLMService>(),
            Substitute.For<IEmbeddingService>(),
            new FakeTokenizer(),
            Substitute.For<IKVStore>(),
            Options.Create(new LightRAGOptions
            {
                ChunkTokenSize = chunkSize,
                ChunkOverlapTokenSize = overlap
            }),
            NullLogger<DocumentProcessingService>.Instance);
    }
}
