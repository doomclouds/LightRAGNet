using FluentAssertions;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.Query;

public sealed class RerankDocumentChunkerTests
{
    [Fact]
    public void Chunk_WhenDocumentsAreShort_PreservesOneToOneMapping()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 10, overlapTokens: 2);

        var result = chunker.Chunk(["alpha beta", "gamma delta"]);

        result.Documents.Should().Equal("alpha beta", "gamma delta");
        result.DocumentIndices.Should().Equal(0, 1);
        result.WasChunked.Should().BeFalse();
    }

    [Fact]
    public void Chunk_WhenDocumentExceedsLimit_SplitsWithOverlap()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 4, overlapTokens: 1);

        var result = chunker.Chunk(["one two three four five six seven"]);

        result.Documents.Should().Equal("one two three four", "four five six seven");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenMultipleDocumentsExceedLimit_PreservesOriginalDocumentIndices()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 3, overlapTokens: 1);

        var result = chunker.Chunk(["a b c d e", "short", "x y z q"]);

        result.Documents.Should().Equal("a b c", "c d e", "short", "x y z", "z q");
        result.DocumentIndices.Should().Equal(0, 0, 1, 2, 2);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenOverlapIsGreaterThanOrEqualToMaxTokens_ClampsOverlapAndTerminates()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 2, overlapTokens: 5);

        var result = chunker.Chunk(["a b c"]);

        result.Documents.Should().Equal("a b", "b c");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenTokenizerExceedsLimitWithoutWordBoundaries_UsesCharacterFallback()
    {
        var chunker = CreateChunker(
            maxTokensPerDocument: 3,
            overlapTokens: 1,
            tokenizer: new CharacterCountingTokenizer());

        var result = chunker.Chunk(["abcdef"]);

        result.Documents.Should().Equal("abc", "cde", "ef");
        result.DocumentIndices.Should().Equal(0, 0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenInputIsEmpty_ReturnsEmptyResult()
    {
        var chunker = CreateChunker(maxTokensPerDocument: 10, overlapTokens: 2);

        var result = chunker.Chunk([]);

        result.Documents.Should().BeEmpty();
        result.DocumentIndices.Should().BeEmpty();
        result.WasChunked.Should().BeFalse();
    }

    private static RerankDocumentChunker CreateChunker(
        int maxTokensPerDocument,
        int overlapTokens,
        ITokenizer? tokenizer = null)
    {
        return new RerankDocumentChunker(
            tokenizer ?? new FakeTokenizer(),
            Options.Create(new RerankChunkingOptions
            {
                MaxTokensPerDocument = maxTokensPerDocument,
                OverlapTokens = overlapTokens
            }));
    }

    private sealed class CharacterCountingTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            return Enumerable.Range(0, text.Length).ToList();
        }

        public string Decode(List<int> tokens)
        {
            return new string('x', tokens.Count);
        }

        public int CountTokens(string text)
        {
            return text.Length;
        }
    }
}
