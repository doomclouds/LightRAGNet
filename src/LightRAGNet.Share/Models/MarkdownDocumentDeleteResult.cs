namespace LightRAGNet.Share.Models;

public sealed class MarkdownDocumentDeleteResult
{
    public bool Accepted { get; init; }

    public int DocumentId { get; init; }

    public string? RagDocumentId { get; init; }

    public string? TaskId { get; init; }
}
