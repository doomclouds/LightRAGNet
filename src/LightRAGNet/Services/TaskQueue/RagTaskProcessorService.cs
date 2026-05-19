using LightRAGNet.Models;
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

        // Subscribe to progress update events
        EventHandler<TaskState> progressHandler = (sender, state) =>
        {
            if (state.DocId == task.RagDocumentId)
            {
                // Only update progress in document chunking stage (Total > 0)
                // Other stages (Total == 0) don't update progress, only update stage
                if (state.Total > 0)
                {
                    var progress = (int)(state.Current * 100.0 / state.Total);
                    
                    _ = taskQueue.UpdateTaskProgressAsync(
                        task.TaskId,
                        state.Stage,
                        progress,
                        taskCancellationToken);
                }
                else
                {
                    // When Total == 0, only update stage, don't update progress (pass null)
                    _ = taskQueue.UpdateTaskProgressAsync(
                        task.TaskId,
                        state.Stage,
                        null, // Don't update progress
                        taskCancellationToken);
                }
            }
        };

        lightRAG.TaskStateChanged += progressHandler;

        try
        {
            if (task.OperationType == RagTaskOperationType.DeleteDocument)
            {
                await ProcessDeleteTaskAsync(task, lightRAG, taskCancellationToken);
                return;
            }

            // Call RAG processing
            var docId = await lightRAG.InsertAsync(
                task.Content,
                task.RagDocumentId,
                task.FilePath,
                taskCancellationToken);

            // Update task status to Completed
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
            lightRAG.TaskStateChanged -= progressHandler;
            cancellationRegistry.CompleteProcessingTask(task.TaskId);
        }
    }

    internal async Task ProcessDeleteTaskAsync(
        RagTask task,
        LightRAG lightRAG,
        CancellationToken cancellationToken)
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

            foreach (var task in tasks)
            {
                if (task.Status != RagTaskStatus.Processing) continue;
                
                // When service restarts, reset tasks being processed to Pending
                logger.LogInformation("Restoring task {TaskId}, status reset from Processing to Pending", task.TaskId);
                await taskQueue.UpdateTaskStatusAsync(task.TaskId, RagTaskStatus.Pending, cancellationToken: cancellationToken);
            }

            logger.LogInformation("Task restoration completed, restored {Count} tasks", tasks.Count(t => t.Status == RagTaskStatus.Processing));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while restoring task status");
        }
    }
}
