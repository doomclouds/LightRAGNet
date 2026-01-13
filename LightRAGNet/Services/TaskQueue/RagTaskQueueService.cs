using System.Collections.Concurrent;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Task queue management service implementation
/// </summary>
public class RagTaskQueueService(
    IRagTaskStateStore stateStore,
    IMediator mediator,
    ILogger<RagTaskQueueService> logger) : IRagTaskQueueService
{
    private readonly ConcurrentDictionary<string, RagTask> _tasks = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Lazy initialization: load tasks on first call
    private bool _tasksLoaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private async Task EnsureTasksLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_tasksLoaded) return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_tasksLoaded) return;

            await LoadTasksFromStoreAsync(cancellationToken);
            _tasksLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string> EnqueueTaskAsync(int documentId, string content, string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync(cancellationToken);

        var taskId = HashUtils.ComputeMd5Hash($"{documentId}_{content}_{DateTime.UtcNow:O}", "task-");
        var ragDocumentId = HashUtils.ComputeMd5Hash(content, "doc-");
        
        var task = new RagTask
        {
            TaskId = taskId,
            DocumentId = documentId,
            RagDocumentId = ragDocumentId,
            Content = content,
            FilePath = filePath,
            Status = RagTaskStatus.Pending,
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _tasks.TryAdd(taskId, task);
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
            logger.LogInformation("Task added to queue: {TaskId}, DocumentId: {DocumentId}", taskId, documentId);
        }
        finally
        {
            _lock.Release();
        }

        await PublishStatusChangedAsync(task, cancellationToken);
        return taskId;
    }

    public async Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Sort by priority, same priority sorted by creation time
            return _tasks.Values
                .Where(t => t.Status == RagTaskStatus.Pending)
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .FirstOrDefault();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _tasks.Values
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                return task;
            }
            
            // If not in memory, load from persistent storage
            return await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var tasks = await GetTasksByDocumentIdsAsync([documentId], cancellationToken);
        return tasks.GetValueOrDefault(documentId);
    }

    public async Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(IEnumerable<int> documentIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<int, RagTask>();
        var documentIdList = documentIds.ToList();
        
        if (documentIdList.Count == 0)
        {
            return result;
        }

        await EnsureTasksLoadedAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Batch search from memory
            var documentIdSet = documentIdList.ToHashSet();
            foreach (var task in _tasks.Values)
            {
                if (documentIdSet.Contains(task.DocumentId))
                {
                    result[task.DocumentId] = task;
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    public async Task UpdateTaskStatusAsync(string taskId, RagTaskStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        RagTask? task;
        var shouldDelete = false;
        var shouldSave = false;
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_tasks.TryGetValue(taskId, out task))
            {
                // Try to load from persistent storage
                task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
                if (task == null)
                {
                    logger.LogWarning("Task does not exist: {TaskId}", taskId);
                    return;
                }
                _tasks.TryAdd(taskId, task);
            }

            var oldStatus = task.Status;
            task.Status = status;
            task.ErrorMessage = errorMessage;

            if (status == RagTaskStatus.Processing && task.StartedAt == null)
            {
                task.StartedAt = DateTime.UtcNow;
            }

            if (status is RagTaskStatus.Completed or RagTaskStatus.Failed)
            {
                task.CompletedAt = DateTime.UtcNow;
                shouldDelete = true;
                // Remove completed tasks from memory (no longer need to keep)
                _tasks.TryRemove(taskId, out _);
                logger.LogInformation("Task completed/failed, removed from memory cache: {TaskId}, {OldStatus} -> {NewStatus}", taskId, oldStatus, status);
            }
            else
            {
                shouldSave = true;
                logger.LogInformation("Task status updated: {TaskId}, {OldStatus} -> {NewStatus}", taskId, oldStatus, status);
            }
        }
        finally
        {
            _lock.Release();
        }

        // Move file I/O operations outside the lock to avoid holding the lock for a long time
        if (shouldDelete)
        {
            // After task completion or failure, delete persistent state (only used for temporary task persistence and recovery)
            await stateStore.DeleteTaskStateAsync(task.TaskId, cancellationToken);
        }
        else if (shouldSave)
        {
            // While task is in progress, save state for recovery
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
        }

        await PublishStatusChangedAsync(task, cancellationToken);
    }

    public async Task UpdateTaskProgressAsync(string taskId, TaskStage? stage, int? progress, CancellationToken cancellationToken = default)
    {
        RagTask? task;
        bool shouldSave;
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_tasks.TryGetValue(taskId, out task))
            {
                task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
                if (task == null)
                {
                    return;
                }
                _tasks.TryAdd(taskId, task);
            }

            // If task is completed or failed, no longer update progress (task is completed, no need to save state)
            if (task.Status is RagTaskStatus.Completed or RagTaskStatus.Failed)
            {
                return;
            }

            task.CurrentStage = stage;
            // Only update progress when progress is not null
            if (progress.HasValue)
            {
                task.Progress = Math.Clamp(progress.Value, 0, 100);
            }
            
            shouldSave = true;
        }
        finally
        {
            _lock.Release();
        }

        // Move file I/O operations outside the lock to avoid holding the lock for a long time
        if (shouldSave)
        {
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
        }

        // Publish status change event to notify frontend
        await PublishStatusChangedAsync(task, cancellationToken);
    }

    public async Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        RagTask? task;
        try
        {
            if (!_tasks.TryGetValue(taskId, out task))
            {
                task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
                if (task == null)
                {
                    throw new InvalidOperationException($"Task does not exist: {taskId}");
                }
                _tasks.TryAdd(taskId, task);
            }

            task.Priority = newPriority;
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
            logger.LogInformation("Task priority updated: {TaskId}, new priority: {Priority}", taskId, newPriority);
        }
        finally
        {
            _lock.Release();
        }

        await PublishStatusChangedAsync(task, cancellationToken);
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_tasks.TryGetValue(taskId, out var task))
            {
                task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
                if (task == null)
                {
                    return false;
                }
            }

            if (task.Status == RagTaskStatus.Processing)
            {
                logger.LogWarning("Cannot delete task being processed: {TaskId}", taskId);
                return false;
            }

            _tasks.TryRemove(taskId, out _);
            await stateStore.DeleteTaskStateAsync(taskId, cancellationToken);
            logger.LogInformation("Task deleted: {TaskId}", taskId);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        RagTask? task;
        try
        {
            if (!_tasks.TryGetValue(taskId, out task))
            {
                task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
                if (task == null)
                {
                    return false;
                }
                _tasks.TryAdd(taskId, task);
            }

            if (task.Status != RagTaskStatus.Failed)
            {
                logger.LogWarning("Can only retry failed tasks: {TaskId}, current status: {Status}", taskId, task.Status);
                return false;
            }

            if (task.RetryCount >= task.MaxRetries)
            {
                logger.LogWarning("Task has reached maximum retry count: {TaskId}, RetryCount: {RetryCount}, MaxRetries: {MaxRetries}",
                    taskId, task.RetryCount, task.MaxRetries);
                return false;
            }

            task.Status = RagTaskStatus.Pending;
            task.RetryCount++;
            task.ErrorMessage = null;
            task.StartedAt = null;
            task.CompletedAt = null;
            task.Progress = 0;
            task.CurrentStage = null;

            await stateStore.SaveTaskStateAsync(task, cancellationToken);
            logger.LogInformation("Task requeued: {TaskId}, retry count: {RetryCount}", taskId, task.RetryCount);
        }
        finally
        {
            _lock.Release();
        }

        await PublishStatusChangedAsync(task, cancellationToken);

        return true;
    }

    private async Task PublishStatusChangedAsync(RagTask task, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Publish(new RagTaskStatusChangedEvent(task), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish task status change event: {TaskId}", task.TaskId);
        }
    }

    private async Task LoadTasksFromStoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tasks = await stateStore.LoadAllTasksAsync(cancellationToken);
            foreach (var task in tasks)
            {
                _tasks.TryAdd(task.TaskId, task);
            }
            logger.LogInformation("Loaded {Count} tasks from persistent storage", tasks.Count);
        }
        catch (OperationCanceledException)
        {
            // Loading was cancelled, don't log error
            logger.LogDebug("Loading tasks from persistent storage was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tasks from persistent storage");
        }
    }

    public async Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _tasks.Clear();
            await stateStore.ClearAllTasksAsync(cancellationToken);
            _tasksLoaded = false; // Reset load flag, will reinitialize on next load
            logger.LogInformation("Cleared all tasks");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync();

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Check if there are tasks being processed in memory
            if (_tasks.Values.Any(t => t.Status == RagTaskStatus.Processing))
            {
                return true;
            }

            // If not in memory, check from persistent storage
            var allTasks = await stateStore.LoadAllTasksAsync(cancellationToken);
            return allTasks.Any(t => t.Status == RagTaskStatus.Processing);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync(cancellationToken);

        await _lock.WaitAsync(cancellationToken);
        var stoppedCount = 0;
        List<RagTask> tasksToNotify = [];
        try
        {
            var tasksToStop = _tasks.Values
                .Where(t => t.Status == RagTaskStatus.Processing || t.Status == RagTaskStatus.Pending)
                .ToList();

            foreach (var task in tasksToStop)
            {
                task.Status = RagTaskStatus.Failed;
                task.ErrorMessage = "Task stopped (when clearing data)";
                task.CompletedAt = DateTime.UtcNow;
                
                // Save state
                await stateStore.SaveTaskStateAsync(task, cancellationToken);
                stoppedCount++;
                tasksToNotify.Add(task);
            }

            logger.LogInformation("Stopped {Count} tasks (Processing and Pending status)", stoppedCount);
        }
        finally
        {
            _lock.Release();
        }

        // Publish status change events
        foreach (var task in tasksToNotify)
        {
            await PublishStatusChangedAsync(task, cancellationToken);
        }

        return stoppedCount;
    }
}
