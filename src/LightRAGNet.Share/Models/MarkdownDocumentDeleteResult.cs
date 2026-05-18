namespace LightRAGNet.Share.Models;

public sealed class MarkdownDocumentDeleteResult
{
    public bool Accepted { get; init; }

    public bool DeletedImmediately { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public int DocumentId { get; init; }

    public string? RagDocumentId { get; init; }

    public string? TaskId { get; init; }
}
