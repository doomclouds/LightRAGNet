namespace LightRAGNet.Models;

/// <summary>
/// Task stage enumeration
/// </summary>
public enum TaskStage
{
    /// <summary>
    /// Document chunking
    /// </summary>
    DocumentChunking,
    
    /// <summary>
    /// Processing chunks (vectorization and entity extraction)
    /// </summary>
    ProcessingChunks,
    
    /// <summary>
    /// Storing text chunks
    /// </summary>
    StoringTextChunks,
    
    /// <summary>
    /// Storing document chunk vectors
    /// </summary>
    StoringChunkVectors,
    
    /// <summary>
    /// Merging entities (Stage 1)
    /// </summary>
    MergingEntities,
    
    /// <summary>
    /// Merging relations (Stage 2)
    /// </summary>
    MergingRelations,
    
    /// <summary>
    /// Updating storage (Stage 3)
    /// </summary>
    UpdatingStorage,
    
    /// <summary>
    /// Storing full document
    /// </summary>
    StoringFullDocument,
    
    /// <summary>
    /// Persisting
    /// </summary>
    Persisting,
    
    /// <summary>
    /// Completed
    /// </summary>
    Completed
}

/// <summary>
/// Task state
/// </summary>
public class TaskState
{
    /// <summary>
    /// Task stage
    /// </summary>
    public TaskStage Stage { get; set; }
    
    /// <summary>
    /// Current progress
    /// </summary>
    public int Current { get; set; }
    
    /// <summary>
    /// Total tasks
    /// </summary>
    public int Total { get; set; }
    
    /// <summary>
    /// Stage description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Details (optional)
    /// </summary>
    public string? Details { get; set; }
    
    /// <summary>
    /// Document ID (if applicable)
    /// </summary>
    public string? DocId { get; set; }
    
    /// <summary>
    /// Whether completed
    /// </summary>
    public bool IsCompleted => Current >= Total && Total > 0;
    
    /// <summary>
    /// Progress percentage (0-100)
    /// If Total is 0, returns -1 to indicate no progress percentage display
    /// </summary>
    public double ProgressPercentage => Total > 0 ? (double)Current / Total * 100 : -1;
}

