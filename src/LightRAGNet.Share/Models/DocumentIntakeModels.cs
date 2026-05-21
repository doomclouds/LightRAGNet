namespace LightRAGNet.Share.Models;

public sealed class SubmitTextDocumentsRequest
{
    public List<TextDocumentInput> Documents { get; set; } = [];
}

public sealed class TextDocumentInput
{
    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class DocumentSubmissionResponse
{
    public string TrackId { get; set; } = string.Empty;

    public List<MarkdownDocumentDto> Documents { get; set; } = [];
}

public sealed class DocumentTrackStatusResponse
{
    public string TrackId { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public int QueuedCount { get; set; }

    public int ProcessingCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public List<MarkdownDocumentDto> Documents { get; set; } = [];
}

public sealed class DocumentPipelineActionResult
{
    public bool Accepted { get; set; }

    public int DocumentId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}
