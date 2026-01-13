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
    /// RAG processing progress (0-100)
    /// </summary>
    public int RagProgress { get; set; }
    
    /// <summary>
    /// Current RAG processing stage
    /// </summary>
    public string? RagCurrentStage { get; set; }
    
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
}
