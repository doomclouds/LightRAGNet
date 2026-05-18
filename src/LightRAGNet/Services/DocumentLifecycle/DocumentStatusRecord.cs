namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentStatusRecord
{
    public required string DocId { get; init; }
    public required string Workspace { get; init; }
    public DocumentLifecycleStatus Status { get; set; }
    public string ContentSummary { get; set; } = string.Empty;
    public int ContentLength { get; set; }
    public int ChunksCount { get; set; }
    public List<string> ChunksList { get; set; } = [];
    public List<DocumentChunkSnapshot> ChunkSnapshots { get; set; } = [];
    public string FilePath { get; set; } = "unknown_source";
    public string TrackId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
