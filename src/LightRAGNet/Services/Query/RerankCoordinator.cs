using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.Query;

internal sealed class RerankCoordinator(
    IRerankService rerankService,
    RerankDocumentChunker chunker,
    IOptions<RerankChunkingOptions> options)
{
    private readonly RerankChunkingOptions _options = options.Value;

    public async Task<List<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0 || topN <= 0)
        {
            return [];
        }

        if (!_options.EnableChunking)
        {
            return await rerankService.RerankAsync(
                query,
                [.. documents],
                topN,
                cancellationToken);
        }

        var chunkingResult = chunker.Chunk(documents);
        if (!chunkingResult.WasChunked)
        {
            return await rerankService.RerankAsync(
                query,
                [.. documents],
                topN,
                cancellationToken);
        }

        var rerankResults = await rerankService.RerankAsync(
            query,
            chunkingResult.Documents,
            chunkingResult.Documents.Count,
            cancellationToken);

        var bestScoresByDocumentIndex = new Dictionary<int, float>();
        foreach (var rerankResult in rerankResults)
        {
            if (rerankResult.Index < 0 || rerankResult.Index >= chunkingResult.DocumentIndices.Count)
            {
                continue;
            }

            var documentIndex = chunkingResult.DocumentIndices[rerankResult.Index];
            if (!bestScoresByDocumentIndex.TryGetValue(documentIndex, out var bestScore)
                || rerankResult.RelevanceScore > bestScore)
            {
                bestScoresByDocumentIndex[documentIndex] = rerankResult.RelevanceScore;
            }
        }

        return bestScoresByDocumentIndex
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(topN)
            .Select(pair => new RerankResult
            {
                Index = pair.Key,
                RelevanceScore = pair.Value
            })
            .ToList();
    }
}
