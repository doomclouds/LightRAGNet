using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed record ChunkingRequest(
    string Content,
    string DocId,
    string FilePath,
    LightRagChunkingSnapshot Options);

public interface IChunkingStrategy
{
    LightRagChunkingStrategy Strategy { get; }

    Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken);
}
