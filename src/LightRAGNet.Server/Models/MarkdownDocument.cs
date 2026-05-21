namespace LightRAGNet.Server.Models;

public class MarkdownDocument
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadTime { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
    
    // RAG related fields
    /// <summary>
    /// Whether the document has been added to the RAG system
    /// </summary>
    public bool IsInRagSystem { get; set; }
    
    /// <summary>
    /// Time when the document was added to the RAG system
    /// </summary>
    public DateTime? RagAddedTime { get; set; }
    
    /// <summary>
    /// RAG processing status: Pending, Processing, Completed, Failed, or null (not added to RAG system)
    /// </summary>
    public string? RagStatus { get; set; }

    /// <summary>
    /// Intake pipeline track identifier for the submission batch
    /// </summary>
    public string? TrackId { get; set; }

    /// <summary>
    /// Current intake pipeline stage
    /// </summary>
    public string? RagCurrentStage { get; set; }

    /// <summary>
    /// Active RAG task identifier currently processing this document
    /// </summary>
    public string? ActiveRagTaskId { get; set; }

    /// <summary>
    /// Time when the intake pipeline started processing the document
    /// </summary>
    public DateTime? PipelineStartedAt { get; set; }

    /// <summary>
    /// Time when the intake pipeline completed the document
    /// </summary>
    public DateTime? PipelineCompletedAt { get; set; }

    /// <summary>
    /// Time when the intake pipeline cancelled the document
    /// </summary>
    public DateTime? PipelineCancelledAt { get; set; }

    /// <summary>
    /// Number of retry attempts for the intake pipeline
    /// </summary>
    public int RagRetryCount { get; set; }
    
    /// <summary>
    /// RAG processing error message
    /// </summary>
    public string? RagErrorMessage { get; set; }
    
    /// <summary>
    /// RAG processing progress (0-100)
    /// </summary>
    public int RagProgress { get; set; }
    
    /// <summary>
    /// Unique identifier of the document in the RAG system (for subsequent retrieval and management)
    /// </summary>
    public string? RagDocumentId { get; set; }
    
    /// <summary>
    /// URL path of the saved Markdown file
    /// </summary>
    public string? FileUrl { get; set; }
    
    /// <summary>
    /// Hash value of the file content (for deduplication)
    /// </summary>
    public string? FileHash { get; set; }
}
