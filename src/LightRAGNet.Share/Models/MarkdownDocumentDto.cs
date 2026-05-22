namespace LightRAGNet.Share.Models;

/// <summary>
/// Markdown document data transfer object
/// </summary>
public class MarkdownDocumentDto
{
    /// <summary>
    /// Document ID
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// File name
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Document content (only returned when getting a single document)
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// File size (bytes)
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// Upload time
    /// </summary>
    public DateTime UploadTime { get; set; }
    
    /// <summary>
    /// Last modified time
    /// </summary>
    public DateTime? LastModified { get; set; }
    
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
    /// RAG processing progress (0-100)
    /// </summary>
    public int RagProgress { get; set; }
    
    /// <summary>
    /// Current RAG processing stage
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
    /// Unique identifier of the document in the RAG system
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

    /// <summary>
    /// Original uploaded file name before conversion
    /// </summary>
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// Original uploaded file content type
    /// </summary>
    public string? OriginalContentType { get; set; }

    /// <summary>
    /// Hash value of the original uploaded file
    /// </summary>
    public string? OriginalContentHash { get; set; }

    /// <summary>
    /// Hash value of the converted Markdown content
    /// </summary>
    public string? ConvertedMarkdownHash { get; set; }

    /// <summary>
    /// Document conversion status
    /// </summary>
    public string? ConversionStatus { get; set; }

    /// <summary>
    /// Document conversion error message
    /// </summary>
    public string? ConversionErrorMessage { get; set; }

    /// <summary>
    /// Time when document conversion started
    /// </summary>
    public DateTime? ConversionStartedAt { get; set; }

    /// <summary>
    /// Time when document conversion completed
    /// </summary>
    public DateTime? ConversionCompletedAt { get; set; }

    /// <summary>
    /// Tool used to convert the original file to Markdown
    /// </summary>
    public string? ConversionTool { get; set; }

    /// <summary>
    /// Version of the conversion tool
    /// </summary>
    public string? ConversionToolVersion { get; set; }
}
