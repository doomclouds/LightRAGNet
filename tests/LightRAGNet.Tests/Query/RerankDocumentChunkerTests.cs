using FluentAssertions;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.Query;

public sealed class RerankDocumentChunkerTests
{
    [Fact]
    public void Types_AreNotPublic()
    {
        typeof(RerankChunkingOptions).IsPublic.Should().BeFalse();
        typeof(RerankChunkingResult).IsPublic.Should().BeFalse();
        typeof(RerankDocumentChunker).IsPublic.Should().BeFalse();
    }

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
        var tokenizer = new RoundTripWhitespaceTokenizer();
        var chunker = CreateChunker(maxTokensPerDocument: 4, overlapTokens: 1, tokenizer: tokenizer);

        var result = chunker.Chunk(["one two three four five six seven"]);

        result.Documents.Should().Equal("one two three four", "four five six seven");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
        result.Documents.Should().OnlyContain(chunk => tokenizer.CountTokens(chunk) <= 4);
    }

    [Fact]
    public void Chunk_WhenMultipleDocumentsExceedLimit_PreservesOriginalDocumentIndices()
    {
        var chunker = CreateChunker(
            maxTokensPerDocument: 3,
            overlapTokens: 1,
            tokenizer: new RoundTripWhitespaceTokenizer());

        var result = chunker.Chunk(["a b c d e", "short", "x y z q"]);

        result.Documents.Should().Equal("a b c", "c d e", "short", "x y z", "z q");
        result.DocumentIndices.Should().Equal(0, 0, 1, 2, 2);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenOverlapIsGreaterThanOrEqualToMaxTokens_ClampsOverlapAndTerminates()
    {
        var chunker = CreateChunker(
            maxTokensPerDocument: 2,
            overlapTokens: 5,
            tokenizer: new RoundTripWhitespaceTokenizer());

        var result = chunker.Chunk(["a b c"]);

        result.Documents.Should().Equal("a b", "b c");
        result.DocumentIndices.Should().Equal(0, 0);
        result.WasChunked.Should().BeTrue();
    }

    [Fact]
    public void Chunk_WhenTokenizerUsesSubwordTokens_ProducesChunksWithinTokenBudget()
    {
        var tokenizer = new DuplicateCharacterTokenizer();
        var chunker = CreateChunker(
            maxTokensPerDocument: 4,
            overlapTokens: 2,
            tokenizer: tokenizer);

        var result = chunker.Chunk(["abcdef"]);

        result.Documents.Should().Equal("ab", "bc", "cd", "de", "ef");
        result.DocumentIndices.Should().Equal(0, 0, 0, 0, 0);
        result.WasChunked.Should().BeTrue();
        result.Documents.Should().OnlyContain(chunk => tokenizer.CountTokens(chunk) <= 4);
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

    private sealed class RoundTripWhitespaceTokenizer : ITokenizer
    {
        private readonly Dictionary<string, int> _tokenIdsByWord = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _wordsByTokenId = [];
        private int _nextTokenId = 1;

        public List<int> Encode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(GetTokenId)
                .ToList();
        }

        public string Decode(List<int> tokens)
        {
            return string.Join(' ', tokens.Select(token => _wordsByTokenId[token]));
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }

        private int GetTokenId(string word)
        {
            if (_tokenIdsByWord.TryGetValue(word, out var tokenId))
            {
                return tokenId;
            }

            tokenId = _nextTokenId++;
            _tokenIdsByWord[word] = tokenId;
            _wordsByTokenId[tokenId] = word;
            return tokenId;
        }
    }

    private sealed class DuplicateCharacterTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            return text.SelectMany(character => Enumerable.Repeat((int)character, 2)).ToList();
        }

        public string Decode(List<int> tokens)
        {
            return new string(tokens.Chunk(2).Select(tokenPair => (char)tokenPair[0]).ToArray());
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }
    }
}
