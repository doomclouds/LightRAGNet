using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.Query;

public sealed class RerankCoordinatorTests
{
    [Fact]
    public async Task RerankAsync_WhenChunkingDisabled_PassesOriginalDocumentsAndTopN()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 1, RelevanceScore = 0.8f }
        ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: false, maxTokensPerDocument: 2);
        var documents = new[] { "one two three", "four five six" };

        var results = await coordinator.RerankAsync("query", documents, topN: 1);

        results.Should().BeEquivalentTo(rerankService.ResultsToReturn);
        rerankService.Calls.Should().ContainSingle();
        rerankService.Calls[0].Documents.Should().Equal(documents);
        rerankService.Calls[0].TopN.Should().Be(1);
    }

    [Fact]
    public async Task RerankAsync_WhenChunkingEnabledWithoutActualChunk_PassesOriginalDocumentsAndTopN()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 0, RelevanceScore = 0.7f }
        ]);
        var coordinator = CreateCoordinator(rerankService, enableChunking: true, maxTokensPerDocument: 10);
        var documents = new[] { "one two", "three four" };

        var results = await coordinator.RerankAsync("query", documents, topN: 1);

        results.Should().BeEquivalentTo(rerankService.ResultsToReturn);
        rerankService.Calls.Should().ContainSingle();
        rerankService.Calls[0].Documents.Should().Equal(documents);
        rerankService.Calls[0].TopN.Should().Be(1);
    }

    [Fact]
    public async Task RerankAsync_WhenDocumentsAreChunked_PassesAllSubdocumentsAndProviderTopNIsSubdocumentCount()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 0, RelevanceScore = 0.3f }
        ]);
        var coordinator = CreateCoordinator(rerankService, maxTokensPerDocument: 3, overlapTokens: 1);

        await coordinator.RerankAsync("query", ["one two three four five", "alpha beta gamma delta"], topN: 1);

        rerankService.Calls.Should().ContainSingle();
        rerankService.Calls[0].Documents.Should().Equal(
            "one two three",
            "three four five",
            "alpha beta gamma",
            "gamma delta");
        rerankService.Calls[0].TopN.Should().Be(4);
    }

    [Fact]
    public async Task RerankAsync_WhenMultipleSubdocumentsMapToSameDocument_UsesMaxScore()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 0, RelevanceScore = 0.2f },
            new RerankResult { Index = 1, RelevanceScore = 0.9f },
            new RerankResult { Index = 2, RelevanceScore = 0.5f }
        ]);
        var coordinator = CreateCoordinator(rerankService, maxTokensPerDocument: 3, overlapTokens: 1);

        var results = await coordinator.RerankAsync("query", ["one two three four five", "short doc"], topN: 2);

        results.Select(ToScoreEntry).Should().Equal(
            (0, 0.9f),
            (1, 0.5f));
    }

    [Fact]
    public async Task RerankAsync_AppliesDocumentLevelTopNAfterAggregation()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 0, RelevanceScore = 0.6f },
            new RerankResult { Index = 1, RelevanceScore = 0.9f },
            new RerankResult { Index = 2, RelevanceScore = 0.8f },
            new RerankResult { Index = 3, RelevanceScore = 0.7f }
        ]);
        var coordinator = CreateCoordinator(rerankService, maxTokensPerDocument: 2, overlapTokens: 0);

        var results = await coordinator.RerankAsync("query", ["a b c d", "x y", "m n"], topN: 2);

        results.Select(ToScoreEntry).Should().Equal(
            (0, 0.9f),
            (1, 0.8f));
    }

    [Fact]
    public async Task RerankAsync_IgnoresInvalidSubdocumentIndexesAndDedupesOriginalDocuments()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 99, RelevanceScore = 1.0f },
            new RerankResult { Index = 0, RelevanceScore = 0.2f },
            new RerankResult { Index = 1, RelevanceScore = 0.8f },
            new RerankResult { Index = 1, RelevanceScore = 0.7f },
            new RerankResult { Index = 2, RelevanceScore = 0.5f },
            new RerankResult { Index = -1, RelevanceScore = 0.9f }
        ]);
        var coordinator = CreateCoordinator(rerankService, maxTokensPerDocument: 3, overlapTokens: 1);

        var results = await coordinator.RerankAsync("query", ["one two three four five", "short doc"], topN: 10);

        results.Select(ToScoreEntry).Should().Equal(
            (0, 0.8f),
            (1, 0.5f));
    }

    [Fact]
    public async Task RerankAsync_WhenDocumentsAreEmpty_ReturnsEmptyWithoutCallingProvider()
    {
        var rerankService = new RecordingRerankService([]);
        var coordinator = CreateCoordinator(rerankService);

        var results = await coordinator.RerankAsync("query", [], topN: 10);

        results.Should().BeEmpty();
        rerankService.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_WhenTopNIsNotPositive_ReturnsEmptyWithoutCallingProvider()
    {
        var rerankService = new RecordingRerankService([]);
        var coordinator = CreateCoordinator(rerankService);

        var results = await coordinator.RerankAsync("query", ["doc"], topN: 0);

        results.Should().BeEmpty();
        rerankService.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Type_IsNotPublic()
    {
        typeof(RerankCoordinator).IsPublic.Should().BeFalse();
    }

    private static RerankCoordinator CreateCoordinator(
        RecordingRerankService rerankService,
        bool enableChunking = true,
        int maxTokensPerDocument = 3,
        int overlapTokens = 1)
    {
        var options = Options.Create(new RerankChunkingOptions
        {
            EnableChunking = enableChunking,
            MaxTokensPerDocument = maxTokensPerDocument,
            OverlapTokens = overlapTokens
        });
        var tokenizer = new RoundTripWhitespaceTokenizer();

        return new RerankCoordinator(
            rerankService,
            new RerankDocumentChunker(tokenizer, options),
            options);
    }

    private static (int Index, float RelevanceScore) ToScoreEntry(RerankResult result)
    {
        return (result.Index, result.RelevanceScore);
    }

    private sealed class RecordingRerankService(List<RerankResult> resultsToReturn) : IRerankService
    {
        public List<RerankResult> ResultsToReturn { get; } = resultsToReturn;

        public List<RerankCall> Calls { get; } = [];

        public Task<List<RerankResult>> RerankAsync(
            string query,
            List<string> documents,
            int topN,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RerankCall(query, [.. documents], topN, cancellationToken));
            return Task.FromResult(ResultsToReturn);
        }
    }

    private sealed record RerankCall(
        string Query,
        List<string> Documents,
        int TopN,
        CancellationToken CancellationToken);

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
}
