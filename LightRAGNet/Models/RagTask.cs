namespace LightRAGNet.Models;

/// <summary>
/// RAG task status enumeration
/// </summary>
public enum RagTaskStatus
{
    /// <summary>
    /// Pending
    /// </summary>
    Pending,
    
    /// <summary>
    /// Processing
    /// </summary>
    Processing,
    
    /// <summary>
    /// Completed
    /// </summary>
    Completed,
    
    /// <summary>
    /// Failed
    /// </summary>
    Failed
}

/// <summary>
/// RAG task model
/// </summary>
public class RagTask
{
    /// <summary>
    /// Unique identifier of the task
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    
    /// <summary>
    /// Document ID (database primary key)
    /// </summary>
    public int DocumentId { get; set; }
    
    /// <summary>
    /// Document ID in the RAG system
    /// </summary>
    public string? RagDocumentId { get; set; }
    
    /// <summary>
    /// Document content
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// File path
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Task status: Pending, Processing, Completed, or Failed
    /// </summary>
    public RagTaskStatus Status { get; set; } = RagTaskStatus.Pending;
    
    /// <summary>
    /// Current processing stage
    /// </summary>
    public TaskStage? CurrentStage { get; set; }
    
    /// <summary>
    /// Processing progress (0-100)
    /// </summary>
    public int Progress { get; set; }
    
    /// <summary>
    /// Error message
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Creation time
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Processing start time
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Completion time
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Queue priority (lower number means higher priority)
    /// </summary>
    public int Priority { get; set; }
    
    /// <summary>
    /// Retry count
    /// </summary>
    public int RetryCount { get; set; }
    
    /// <summary>
    /// Maximum retry count
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
