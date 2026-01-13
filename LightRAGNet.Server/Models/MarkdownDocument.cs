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
