using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.Query;

public sealed record RerankChunkingResult(
    List<string> Documents,
    List<int> DocumentIndices,
    bool WasChunked);

public sealed class RerankDocumentChunker(
    ITokenizer tokenizer,
    IOptions<RerankChunkingOptions> options)
{
    private readonly RerankChunkingOptions _options = options.Value;

    public RerankChunkingResult Chunk(IReadOnlyList<string> documents)
    {
        var chunkedDocuments = new List<string>();
        var documentIndices = new List<int>();
        var wasChunked = false;

        if (documents.Count == 0)
        {
            return new RerankChunkingResult(chunkedDocuments, documentIndices, wasChunked);
        }

        var maxTokens = Math.Max(1, _options.MaxTokensPerDocument);
        var overlapTokens = Math.Clamp(_options.OverlapTokens, 0, maxTokens - 1);

        for (var documentIndex = 0; documentIndex < documents.Count; documentIndex++)
        {
            var document = documents[documentIndex];
            if (!_options.EnableChunking || tokenizer.CountTokens(document) <= maxTokens)
            {
                chunkedDocuments.Add(document);
                documentIndices.Add(documentIndex);
                continue;
            }

            wasChunked = true;
            AddTokenChunks(
                tokenizer.Encode(document),
                documentIndex,
                maxTokens,
                overlapTokens,
                chunkedDocuments,
                documentIndices);
        }

        return new RerankChunkingResult(chunkedDocuments, documentIndices, wasChunked);
    }

    private void AddTokenChunks(
        List<int> tokens,
        int documentIndex,
        int maxTokens,
        int overlapTokens,
        List<string> chunkedDocuments,
        List<int> documentIndices)
    {
        var step = maxTokens - overlapTokens;
        for (var start = 0; start < tokens.Count;)
        {
            var length = Math.Min(maxTokens, tokens.Count - start);
            chunkedDocuments.Add(tokenizer.Decode(tokens.GetRange(start, length)));
            documentIndices.Add(documentIndex);

            if (start + length >= tokens.Count)
            {
                break;
            }

            start += step;
        }
    }
}
