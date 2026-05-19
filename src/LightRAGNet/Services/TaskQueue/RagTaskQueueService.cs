using System.Collections.Concurrent;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.Utilities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Task queue management service implementation
/// </summary>
public class RagTaskQueueService(
    IRagTaskStateStore stateStore,
    IMediator mediator,
    IRagTaskCancellationRegistry cancellationRegistry,
    ILogger<RagTaskQueueService> logger) : IRagTaskQueueService
{
    private readonly ConcurrentDictionary<string, RagTask> _tasks = new();
    private readonly PerKeyAsyncLock<string> _publishLocks = new();
    private readonly ConcurrentDictionary<string, byte> _terminalTaskIds = new();
    private readonly Dictionary<string, TaskLifecycleEntry> _taskLifecycles = [];
    private readonly object _taskLifecycleGate = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Lazy initialization: load tasks on first call
    private bool _tasksLoaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    internal Func<string, Task>? BeforeProgressPublishForTesting { get; set; }

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

    public async Task<string?> EnqueueTaskAsync(int documentId, string content, string filePath, CancellationToken cancellationToken = default)
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
            var hasActiveTask = _tasks.Values.Any(t =>
                t.DocumentId == documentId &&
                (t.Status == RagTaskStatus.Pending || t.Status == RagTaskStatus.Processing));

            if (hasActiveTask)
            {
                logger.LogWarning("Cannot enqueue index task for document {DocumentId}; active task exists.", documentId);
                return null;
            }

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

    public async Task<string?> EnqueueDeletionTaskAsync(
        int documentId,
        string ragDocumentId,
        string filePath,
        bool deleteLlmCache,
        CancellationToken cancellationToken = default)
    {
        await EnsureTasksLoadedAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        RagTask task;
        try
        {
            var hasActiveTask = _tasks.Values.Any(t =>
                t.DocumentId == documentId &&
                (t.Status == RagTaskStatus.Pending || t.Status == RagTaskStatus.Processing));

            if (hasActiveTask)
            {
                logger.LogWarning("Cannot enqueue deletion for document {DocumentId}; active task exists.", documentId);
                return null;
            }

            var taskId = HashUtils.ComputeMd5Hash(
                $"delete_{documentId}_{ragDocumentId}_{DateTime.UtcNow:O}",
                "task-");

            task = new RagTask
            {
                TaskId = taskId,
                DocumentId = documentId,
                RagDocumentId = ragDocumentId,
                FilePath = filePath,
                DeleteFilePath = filePath,
                DeleteLlmCache = deleteLlmCache,
                OperationType = RagTaskOperationType.DeleteDocument,
                Status = RagTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _tasks.TryAdd(taskId, task);
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }

        await PublishStatusChangedAsync(task, cancellationToken);
        return task.TaskId;
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
        using var lifecycleLease = RetainTaskLifecycle(taskId);
        await using var publishLease = await _publishLocks.LockAsync(taskId, cancellationToken);

        var canCleanupTerminalTombstone = false;
        try
        {
            canCleanupTerminalTombstone = await UpdateTaskStatusCoreAsync(taskId, status, errorMessage, cancellationToken);
        }
        finally
        {
            if (canCleanupTerminalTombstone)
            {
                lifecycleLease.RequestTerminalCleanup();
            }
        }
    }

    private async Task<bool> UpdateTaskStatusCoreAsync(
        string taskId,
        RagTaskStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        RagTask? task;
        RagTask taskToPublish;
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
                    return false;
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
                _terminalTaskIds.TryAdd(taskId, 0);
                // Remove completed tasks from memory (no longer need to keep)
                _tasks.TryRemove(taskId, out _);
                logger.LogInformation("Task completed/failed, removed from memory cache: {TaskId}, {OldStatus} -> {NewStatus}", taskId, oldStatus, status);
            }
            else
            {
                shouldSave = true;
                logger.LogInformation("Task status updated: {TaskId}, {OldStatus} -> {NewStatus}", taskId, oldStatus, status);
            }

            taskToPublish = CloneTaskForPublication(task);
        }
        finally
        {
            _lock.Release();
        }

        // Move file I/O operations outside the lock to avoid holding the lock for a long time
        if (shouldDelete)
        {
            // After task completion or failure, delete persistent state (only used for temporary task persistence and recovery)
            try
            {
                await stateStore.DeleteTaskStateAsync(task.TaskId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete terminal task state; saving terminal snapshot instead: {TaskId}",
                    task.TaskId);
                await stateStore.SaveTaskStateAsync(task, CancellationToken.None);
            }
        }
        else if (shouldSave)
        {
            // While task is in progress, save state for recovery
            await stateStore.SaveTaskStateAsync(task, cancellationToken);
        }

        await PublishStatusChangedAsync(taskToPublish, cancellationToken);
        return shouldDelete;
    }

    public async Task UpdateTaskProgressAsync(string taskId, TaskStage? stage, int? progress, CancellationToken cancellationToken = default)
    {
        using var lifecycleLease = RetainTaskLifecycle(taskId);
        RagTask? task;
        bool shouldSave;
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_tasks.TryGetValue(taskId, out task))
            {
                if (_terminalTaskIds.ContainsKey(taskId))
                {
                    return;
                }

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

        if (BeforeProgressPublishForTesting is not null)
        {
            await BeforeProgressPublishForTesting(taskId);
        }

        await using var publishLease = await _publishLocks.LockAsync(taskId, cancellationToken);
        RagTask? taskToPublish = null;
        var shouldDeleteStaleState = false;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var hasActiveTask = _tasks.TryGetValue(taskId, out var activeTask);
            if (hasActiveTask &&
                ReferenceEquals(activeTask, task) &&
                activeTask.Status is not (RagTaskStatus.Completed or RagTaskStatus.Failed))
            {
                taskToPublish = CloneTaskForPublication(activeTask);
            }
            else
            {
                shouldDeleteStaleState = !hasActiveTask;
            }
        }
        finally
        {
            _lock.Release();
        }

        if (taskToPublish is null)
        {
            if (shouldDeleteStaleState)
            {
                await stateStore.DeleteTaskStateAsync(taskId, CancellationToken.None);
            }

            logger.LogDebug(
                "Discarded stale progress update for terminal or replaced task {TaskId}: Stage={Stage}, Progress={Progress}",
                taskId,
                stage,
                progress);
            return;
        }

        await PublishStatusChangedAsync(taskToPublish, cancellationToken);
    }

    private static RagTask CloneTaskForPublication(RagTask task)
    {
        return new RagTask
        {
            TaskId = task.TaskId,
            DocumentId = task.DocumentId,
            RagDocumentId = task.RagDocumentId,
            OperationType = task.OperationType,
            DeleteLlmCache = task.DeleteLlmCache,
            DeleteFilePath = task.DeleteFilePath,
            Content = task.Content,
            FilePath = task.FilePath,
            Status = task.Status,
            CurrentStage = task.CurrentStage,
            Progress = task.Progress,
            ErrorMessage = task.ErrorMessage,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            Priority = task.Priority,
            RetryCount = task.RetryCount,
            MaxRetries = task.MaxRetries
        };
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
            _terminalTaskIds.TryRemove(taskId, out _);
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
            await mediator.Publish(new RagTaskStatusChangedEvent(CloneTaskForPublication(task)), cancellationToken);
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
            _terminalTaskIds.Clear();
            ClearIdleTaskLifecycles();
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

        var cancelledCount = cancellationRegistry.CancelActiveTasks();
        if (cancelledCount > 0)
        {
            logger.LogInformation("Cancellation requested for {Count} active processing tasks.", cancelledCount);
        }

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
                tasksToNotify.Add(CloneTaskForPublication(task));
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
            await PublishStatusChangedWithGateAsync(task, cancellationToken);
        }

        return stoppedCount;
    }

    private async Task PublishStatusChangedWithGateAsync(RagTask task, CancellationToken cancellationToken)
    {
        await using var publishLease = await _publishLocks.LockAsync(task.TaskId, cancellationToken);
        await PublishStatusChangedAsync(task, cancellationToken);
    }

    private TaskLifecycleLease RetainTaskLifecycle(string taskId)
    {
        lock (_taskLifecycleGate)
        {
            if (!_taskLifecycles.TryGetValue(taskId, out var entry))
            {
                entry = new TaskLifecycleEntry();
                _taskLifecycles.Add(taskId, entry);
            }

            entry.ReferenceCount++;
            return new TaskLifecycleLease(this, taskId, entry);
        }
    }

    private void RequestTerminalCleanup(string taskId, TaskLifecycleEntry entry)
    {
        lock (_taskLifecycleGate)
        {
            if (_taskLifecycles.TryGetValue(taskId, out var current) &&
                ReferenceEquals(current, entry))
            {
                entry.RemoveTerminalTombstoneWhenIdle = true;
                TryCleanupTaskLifecycleLocked(taskId, entry);
                return;
            }
        }

        _terminalTaskIds.TryRemove(taskId, out _);
    }

    private void ReleaseTaskLifecycle(string taskId, TaskLifecycleEntry entry)
    {
        lock (_taskLifecycleGate)
        {
            entry.ReferenceCount--;
            TryCleanupTaskLifecycleLocked(taskId, entry);
        }
    }

    private void ClearIdleTaskLifecycles()
    {
        lock (_taskLifecycleGate)
        {
            foreach (var (taskId, entry) in _taskLifecycles.ToArray())
            {
                entry.RemoveTerminalTombstoneWhenIdle = true;
                TryCleanupTaskLifecycleLocked(taskId, entry);
            }
        }
    }

    private void TryCleanupTaskLifecycleLocked(string taskId, TaskLifecycleEntry entry)
    {
        if (entry.ReferenceCount != 0 ||
            !_taskLifecycles.TryGetValue(taskId, out var current) ||
            !ReferenceEquals(current, entry))
        {
            return;
        }

        _taskLifecycles.Remove(taskId);
        if (entry.RemoveTerminalTombstoneWhenIdle)
        {
            _terminalTaskIds.TryRemove(taskId, out _);
        }
    }

    private sealed class TaskLifecycleEntry
    {
        public int ReferenceCount { get; set; }

        public bool RemoveTerminalTombstoneWhenIdle { get; set; }
    }

    private sealed class TaskLifecycleLease : IDisposable
    {
        private readonly RagTaskQueueService _owner;
        private readonly string _taskId;
        private readonly TaskLifecycleEntry _entry;
        private int _disposed;

        public TaskLifecycleLease(
            RagTaskQueueService owner,
            string taskId,
            TaskLifecycleEntry entry)
        {
            _owner = owner;
            _taskId = taskId;
            _entry = entry;
        }

        public void RequestTerminalCleanup()
        {
            _owner.RequestTerminalCleanup(_taskId, _entry);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseTaskLifecycle(_taskId, _entry);
            }
        }
    }
}
