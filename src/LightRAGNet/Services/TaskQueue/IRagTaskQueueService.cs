using LightRAGNet.Models;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Task queue management service interface
/// </summary>
public interface IRagTaskQueueService
{
    /// <summary>
    /// Add document to processing queue
    /// </summary>
    Task<string> EnqueueTaskAsync(int documentId, string content, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get next pending task
    /// </summary>
    Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all tasks
    /// </summary>
    Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get specified task
    /// </summary>
    Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get task by document ID
    /// </summary>
    Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Batch get tasks by document IDs
    /// </summary>
    Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(IEnumerable<int> documentIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update task status
    /// </summary>
    Task UpdateTaskStatusAsync(string taskId, RagTaskStatus status, string? errorMessage = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update task progress
    /// </summary>
    /// <param name="taskId">Task ID</param>
    /// <param name="stage">Current stage</param>
    /// <param name="progress">Progress value (0-100), if null then don't update progress, only update stage</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateTaskProgressAsync(string taskId, TaskStage? stage, int? progress, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adjust task priority (reorder)
    /// </summary>
    Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete task (only for Pending status)
    /// </summary>
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retry failed task
    /// </summary>
    Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clear all tasks
    /// </summary>
    Task ClearAllTasksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if there are tasks in progress
    /// </summary>
    Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop all tasks (mark tasks with Processing and Pending status as Failed)
    /// </summary>
    Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default);
}
