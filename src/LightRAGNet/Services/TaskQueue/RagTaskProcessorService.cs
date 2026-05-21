using LightRAGNet.Models;
using LightRAGNet.Services.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Background task processing service
/// </summary>
public class RagTaskProcessorService(
    IRagTaskQueueService taskQueue,
    IRagTaskCancellationRegistry cancellationRegistry,
    IServiceScopeFactory scopeFactory,
    ILogger<RagTaskProcessorService> logger)
    : BackgroundService
{
    private static readonly TimeSpan DefaultTerminalProgressDrainTimeout = TimeSpan.FromSeconds(5);

    internal TimeSpan TerminalProgressDrainTimeoutForTesting { get; set; } = DefaultTerminalProgressDrainTimeout;

    internal Func<LightRAG, RagTask, CancellationToken, Task>? AfterProgressHandlerSubscribedForTesting { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RAG task processing service started");

        // Restore task status when service starts
        await RestoreTasksAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var task = await taskQueue.GetNextTaskAsync(stoppingToken);

                if (task == null)
                {
                    // No tasks, wait 5 seconds before retrying
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                // Process task
                await ProcessTaskAsync(task, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Task processing service is stopping");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing task");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("RAG task processing service stopped");
    }

    private async Task ProcessTaskAsync(RagTask task, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting to process task: {TaskId}, DocumentId: {DocumentId}", task.TaskId, task.DocumentId);
        var taskCancellationToken = cancellationRegistry.RegisterProcessingTask(task.TaskId, cancellationToken);

        // Update status to Processing
        await taskQueue.UpdateTaskStatusAsync(task.TaskId, RagTaskStatus.Processing, cancellationToken: taskCancellationToken);
        task.StartedAt = DateTime.UtcNow;

        // Create scope to get LightRAG service
        using var scope = scopeFactory.CreateScope();
        var lightRAG = scope.ServiceProvider.GetRequiredService<LightRAG>();
        var terminalDrainAttempted = false;
        var terminalDrainSucceeded = false;
        var progressHandlerSubscribed = false;
        await using var progressQueue = new AsyncEventDispatcher<TaskState>(
            async (state, token) =>
            {
                if (task.Status is RagTaskStatus.Completed or RagTaskStatus.Failed or RagTaskStatus.Cancelled)
                {
                    logger.LogDebug(
                        "Discarding late progress update for terminal task {TaskId}: Stage={Stage}, Current={Current}, Total={Total}",
                        task.TaskId,
                        state.Stage,
                        state.Current,
                        state.Total);
                    return;
                }

                var progress = state.Total > 0
                    ? (int)(state.Current * 100.0 / state.Total)
                    : (int?)null;

                await taskQueue.UpdateTaskProgressAsync(
                    task.TaskId,
                    state.Stage,
                    progress,
                    token);
            },
            logger,
            keySelector: _ => task.TaskId,
            cancellationToken: taskCancellationToken);

        EventHandler<TaskState> progressHandler = (sender, state) =>
        {
            if (state.DocId == task.RagDocumentId)
            {
                try
                {
                    // EnqueueLatestAsync completes after the event is accepted into the dispatcher channel.
                    _ = progressQueue.EnqueueLatestAsync(state, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to enqueue progress update for task {TaskId}: Stage={Stage}, Current={Current}, Total={Total}",
                        task.TaskId,
                        state.Stage,
                        state.Current,
                        state.Total);
                }
            }
        };

        void UnsubscribeProgressHandler()
        {
            if (!progressHandlerSubscribed)
            {
                return;
            }

            lightRAG.TaskStateChanged -= progressHandler;
            progressHandlerSubscribed = false;
        }

        async Task DrainBeforeTerminalStatusAsync(string terminalStatus)
        {
            terminalDrainAttempted = true;
            terminalDrainSucceeded = await DrainProgressQueueBeforeTerminalStatusAsync(
                progressQueue,
                task,
                terminalStatus,
                CancellationToken.None);

            UnsubscribeProgressHandler();

            if (terminalDrainSucceeded)
            {
                terminalDrainSucceeded = await DrainProgressQueueBeforeTerminalStatusAsync(
                    progressQueue,
                    task,
                    $"{terminalStatus} after unsubscribe",
                    CancellationToken.None);
            }
        }

        lightRAG.TaskStateChanged += progressHandler;
        progressHandlerSubscribed = true;

        try
        {
            if (AfterProgressHandlerSubscribedForTesting is not null)
            {
                await AfterProgressHandlerSubscribedForTesting(lightRAG, task, taskCancellationToken);
            }

            if (task.OperationType == RagTaskOperationType.DeleteDocument)
            {
                await ProcessDeleteTaskAsync(
                    task,
                    lightRAG,
                    taskCancellationToken,
                    () => DrainBeforeTerminalStatusAsync(RagTaskStatus.Completed.ToString()));
                return;
            }

            // Call RAG processing
            var docId = await lightRAG.InsertAsync(
                task.Content,
                task.RagDocumentId,
                task.FilePath,
                taskCancellationToken);

            // Update task status to Completed
            await DrainBeforeTerminalStatusAsync(RagTaskStatus.Completed.ToString());

            task.RagDocumentId = docId;
            task.Status = RagTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.CurrentStage = TaskStage.Completed;

            await taskQueue.UpdateTaskStatusAsync(task.TaskId, RagTaskStatus.Completed, cancellationToken: taskCancellationToken);

            logger.LogInformation("Task processing completed: {TaskId}, RagDocumentId: {DocId}", task.TaskId, docId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation due to service shutdown, reset task to Pending so it can be retried after service restart
            logger.LogWarning("Task {TaskId} was cancelled due to service shutdown, reset to Pending status for retry after restart", task.TaskId);

            await DrainBeforeTerminalStatusAsync(RagTaskStatus.Pending.ToString());
            
            await taskQueue.UpdateTaskStatusAsync(
                task.TaskId,
                RagTaskStatus.Pending,
                null,
                CancellationToken.None); // Use CancellationToken.None because service may be shutting down
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation due to service shutdown, reset task to Pending
            logger.LogWarning(ex, "Task {TaskId} was cancelled due to service shutdown, reset to Pending status for retry after restart", task.TaskId);

            await DrainBeforeTerminalStatusAsync(RagTaskStatus.Pending.ToString());
            
            await taskQueue.UpdateTaskStatusAsync(
                task.TaskId,
                RagTaskStatus.Pending,
                null,
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Task {TaskId} was cancelled by task queue stop request.", task.TaskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while processing task {TaskId}", task.TaskId);

            // Update task status to Failed
            await DrainBeforeTerminalStatusAsync(RagTaskStatus.Failed.ToString());

            task.Status = RagTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTime.UtcNow;

            await taskQueue.UpdateTaskStatusAsync(
                task.TaskId,
                RagTaskStatus.Failed,
                ex.Message,
                taskCancellationToken);
        }
        finally
        {
            UnsubscribeProgressHandler();
            if (!terminalDrainAttempted || terminalDrainSucceeded)
            {
                await DrainProgressQueueBeforeTerminalStatusAsync(
                    progressQueue,
                    task,
                    "final cleanup",
                    CancellationToken.None);
            }
            else
            {
                logger.LogDebug(
                    "Skipping final progress drain for task {TaskId} because the terminal drain did not complete.",
                    task.TaskId);
            }

            cancellationRegistry.CompleteProcessingTask(task.TaskId);
        }
    }

    private async Task<bool> DrainProgressQueueBeforeTerminalStatusAsync(
        AsyncEventDispatcher<TaskState> progressQueue,
        RagTask task,
        string terminalStatus,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TerminalProgressDrainTimeoutForTesting);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await progressQueue.DrainAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Timed out after {Timeout} while draining progress updates for task {TaskId} before {TerminalStatus}; continuing terminal status update.",
                TerminalProgressDrainTimeoutForTesting,
                task.TaskId,
                terminalStatus);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "Progress drain was cancelled for task {TaskId} before {TerminalStatus}; continuing terminal status update.",
                task.TaskId,
                terminalStatus);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Progress drain failed for task {TaskId} before {TerminalStatus}; continuing terminal status update.",
                task.TaskId,
                terminalStatus);
            return false;
        }
    }

    internal async Task ProcessDeleteTaskAsync(
        RagTask task,
        LightRAG lightRAG,
        CancellationToken cancellationToken,
        Func<Task>? beforeTerminalStatusUpdate = null)
    {
        if (string.IsNullOrWhiteSpace(task.RagDocumentId))
        {
            throw new InvalidOperationException("Delete task requires RagDocumentId.");
        }

        var result = await lightRAG.DeleteDocumentAsync(
            task.RagDocumentId,
            task.DeleteLlmCache,
            cancellationToken);

        if (!result.Succeeded && !result.Found)
        {
            logger.LogWarning(
                "Delete task {TaskId} targeted missing RAG document {RagDocumentId}; treating deletion as completed.",
                task.TaskId,
                task.RagDocumentId);

            if (beforeTerminalStatusUpdate is not null)
            {
                await beforeTerminalStatusUpdate();
            }

            task.Status = RagTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.CurrentStage = TaskStage.Completed;

            await taskQueue.UpdateTaskStatusAsync(
                task.TaskId,
                RagTaskStatus.Completed,
                cancellationToken: cancellationToken);

            return;
        }

        if (!result.Succeeded)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "Document deletion failed."
                : result.Message;
            var stageSuffix = string.IsNullOrWhiteSpace(result.Stage)
                ? string.Empty
                : $" Stage: {result.Stage}.";

            throw new InvalidOperationException($"{message}{stageSuffix}");
        }

        if (beforeTerminalStatusUpdate is not null)
        {
            await beforeTerminalStatusUpdate();
        }

        task.Status = RagTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CurrentStage = TaskStage.Completed;

        await taskQueue.UpdateTaskStatusAsync(
            task.TaskId,
            RagTaskStatus.Completed,
            cancellationToken: cancellationToken);
    }

    private async Task RestoreTasksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tasks = await taskQueue.GetAllTasksAsync(cancellationToken);

            var interruptedTasks = tasks
                .Where(task => task.Status == RagTaskStatus.Processing)
                .ToList();

            foreach (var task in interruptedTasks)
            {
                const string errorMessage = "Task processing was interrupted by service shutdown or restart.";

                logger.LogWarning(
                    "Restoring task {TaskId}, status changed from Processing to Failed because processing was interrupted",
                    task.TaskId);
                await taskQueue.UpdateTaskStatusAsync(
                    task.TaskId,
                    RagTaskStatus.Failed,
                    errorMessage,
                    cancellationToken);
            }

            logger.LogInformation("Task restoration completed, restored {Count} tasks", interruptedTasks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while restoring task status");
        }
    }
}
