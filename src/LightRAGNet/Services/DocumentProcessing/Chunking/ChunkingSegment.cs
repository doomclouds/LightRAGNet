namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class ChunkingSegment
{
    public string Content { get; init; } = string.Empty;
    public int Tokens { get; init; }
    public int Order { get; init; }
    public LightRagChunkingStrategy Strategy { get; init; }
    public SourceSpan? SourceSpan { get; init; }
    public ChunkHeading? Heading { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}

/// <summary>
/// Represents a half-open source interval in the original document.
/// </summary>
/// <param name="Start">Inclusive zero-based start offset.</param>
/// <param name="End">Exclusive zero-based end offset.</param>
public sealed record SourceSpan(int Start, int End);

public sealed record ChunkHeading(
    int Level,
    string Heading,
    IReadOnlyList<string> ParentHeadings);
