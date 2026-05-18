namespace LightRAGNet.Web;

public sealed class MarkdownDocumentDeleteClientResult
{
    public bool Succeeded { get; init; }

    public bool DeletedImmediately { get; init; }

    public bool Accepted { get; init; }

    public bool Conflict { get; init; }

    public string? TaskId { get; init; }

    public string? ErrorMessage { get; init; }
}
