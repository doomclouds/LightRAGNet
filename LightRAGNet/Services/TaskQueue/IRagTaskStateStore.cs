using LightRAGNet.Models;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Task state persistence service interface
/// </summary>
public interface IRagTaskStateStore
{
    /// <summary>
    /// Save task state
    /// </summary>
    Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Load all task states
    /// </summary>
    Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Load specified task state
    /// </summary>
    Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete task state
    /// </summary>
    Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Save all task states (batch save)
    /// </summary>
    Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clear all task states
    /// </summary>
    Task ClearAllTasksAsync(CancellationToken cancellationToken = default);
}
