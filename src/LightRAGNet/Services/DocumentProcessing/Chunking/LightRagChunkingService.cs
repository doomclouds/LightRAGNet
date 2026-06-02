using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class LightRagChunkingService(
    IEnumerable<IChunkingStrategy> strategies,
    ITokenizer tokenizer,
    IOptions<LightRAGOptions> options,
    ILogger<LightRagChunkingService> logger)
{
    private readonly Dictionary<LightRagChunkingStrategy, IChunkingStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Strategy);

    public async Task<IReadOnlyList<Chunk>> ChunkDocumentAsync(
        string content,
        string docId,
        string filePath = "",
        LightRagChunkingSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        snapshot ??= options.Value.CreateChunkingSnapshot();

        if (!_strategies.TryGetValue(snapshot.Strategy, out var strategy))
        {
            throw new InvalidOperationException($"Chunking strategy '{snapshot.Strategy}' is not registered.");
        }

        var request = new ChunkingRequest(content, docId, filePath, snapshot);
        var segments = await strategy.ChunkAsync(request, tokenizer, cancellationToken);

        logger.LogDebug(
            "Chunked document {DocId} with strategy {Strategy}: {ChunkCount} chunks",
            docId,
            snapshot.Strategy,
            segments.Count);

        return segments
            .Select((segment, index) => new Chunk
            {
                Id = HashUtils.ComputeMd5Hash(segment.Content, "chunk-"),
                Content = segment.Content.Trim(),
                Tokens = segment.Tokens,
                ChunkOrderIndex = index,
                FullDocId = docId,
                FilePath = filePath
            })
            .ToList();
    }
}
