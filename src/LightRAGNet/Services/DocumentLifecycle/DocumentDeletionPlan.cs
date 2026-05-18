namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentDeletionPlan
{
    public required string DocId { get; init; }
    public required string Workspace { get; init; }
    public bool Found { get; init; }
    public IReadOnlyList<string> ChunkIds { get; init; } = [];
    public IReadOnlyList<DocumentChunkSnapshot> ChunkSnapshots { get; init; } = [];
    public bool DeleteFullDocument { get; init; }
    public bool DeleteTextChunks { get; init; }
    public bool DeleteChunkVectors { get; init; }
    public bool DeleteDocumentGraphMetadata { get; init; }
    public bool DeleteLlmCache { get; init; }
}
