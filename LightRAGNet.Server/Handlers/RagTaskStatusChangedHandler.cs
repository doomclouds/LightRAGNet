using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace LightRAGNet.Server.Handlers;

/// <summary>
/// Task status change event handler - updates document status in database and pushes to frontend
/// </summary>
public class RagTaskStatusChangedHandler(
    IServiceScopeFactory scopeFactory,
    IHubContext<RagTaskHub> hubContext,
    ILogger<RagTaskStatusChangedHandler> logger) : INotificationHandler<RagTaskStatusChangedEvent>
{
    public async Task Handle(RagTaskStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        
        // 1. Update database status
        await UpdateDatabaseStatusAsync(task, cancellationToken);
        
        // 2. Push frontend update
        await NotifyFrontendAsync(task, cancellationToken);
    }

    private async Task UpdateDatabaseStatusAsync(RagTask task, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var document = await context.MarkdownDocuments.FindAsync([task.DocumentId], cancellationToken);
            if (document == null)
            {
                logger.LogWarning("Document not found: DocumentId={DocumentId}", task.DocumentId);
                return;
            }

            document.RagStatus = task.Status.ToString();
            document.RagErrorMessage = task.ErrorMessage;
            document.RagDocumentId = task.RagDocumentId;

            // Update progress in ProcessingChunks, MergingEntities, MergingRelations stages
            // Other stages don't update progress, keep previous value
            if (task.CurrentStage == TaskStage.ProcessingChunks || 
                task.CurrentStage == TaskStage.MergingEntities || 
                task.CurrentStage == TaskStage.MergingRelations)
            {
                document.RagProgress = task.Progress;
            }
            // If not in these stages, keep current progress value unchanged

            if (task.Status == RagTaskStatus.Completed)
            {
                document.IsInRagSystem = true;
                document.RagAddedTime = DateTime.UtcNow;
                // Don't clear progress on completion, but frontend will judge whether to display based on stage
            }

            await context.SaveChangesAsync(cancellationToken);
            
            logger.LogDebug("Document status updated: DocumentId={DocumentId}, Status={Status}, Progress={Progress}",
                task.DocumentId, task.Status, task.Progress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update document status: DocumentId={DocumentId}", task.DocumentId);
        }
    }

    private async Task NotifyFrontendAsync(RagTask task, CancellationToken cancellationToken)
    {
        try
        {
            var updateData = new
            {
                taskId = task.TaskId,
                documentId = task.DocumentId,
                status = task.Status.ToString(),
                progress = task.Progress,
                currentStage = task.CurrentStage?.ToString(),
                errorMessage = task.ErrorMessage,
                startedAt = task.StartedAt,
                completedAt = task.CompletedAt
            };

            // Push task status update to all clients listening to tasks
            await hubContext.Clients.Group("all-tasks").SendAsync(
                "TaskStatusUpdated",
                updateData,
                cancellationToken);

            // Push status update for specific task
            await hubContext.Clients.Group($"task-{task.TaskId}").SendAsync(
                "TaskStatusUpdated",
                updateData,
                cancellationToken);

            logger.LogInformation("Task status pushed to frontend: TaskId={TaskId}, Status={Status}, Progress={Progress}, Stage={Stage}", 
                task.TaskId, task.Status, task.Progress, task.CurrentStage?.ToString() ?? "null");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push task status to frontend: TaskId={TaskId}", task.TaskId);
        }
    }
}
