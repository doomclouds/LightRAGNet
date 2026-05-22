using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Services.TaskQueue;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed class DocumentConversionProcessor(
    AppDbContext dbContext,
    IDocumentArtifactStore artifactStore,
    IDocumentMarkdownConverter converter,
    IRagTaskQueueService ragTaskQueue,
    ILogger<DocumentConversionProcessor> logger)
{
    private const string EmptyMarkdownMessage = "Document conversion produced empty Markdown.";
    private const string GenericConversionFailureMessage = "Document conversion failed.";
    private const string RagQueueFailureMessage = "Document could not be queued for indexing.";
    private const string ConversionTool = "ManagedCode.MarkItDown";
    private const string ConversionToolVersion = "10.0.7";

    public async Task<int> ProcessNextBatchAsync(int maxDocuments, CancellationToken cancellationToken = default)
    {
        if (maxDocuments <= 0)
        {
            return 0;
        }

        var candidates = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Where(document =>
                (document.ConversionStatus == DocumentConversionStatus.Queued &&
                 document.RagStatus == DocumentIntakeStatus.Queued) ||
                (document.ConversionStatus == DocumentConversionStatus.Completed &&
                 document.RagStatus == DocumentIntakeStatus.Processing &&
                 document.ConvertedMarkdownPath != null &&
                 document.ConvertedMarkdownPath != string.Empty &&
                 document.ActiveRagTaskId == null))
            .OrderBy(document => document.UploadTime)
            .Take(maxDocuments)
            .Select(document => new DocumentConversionCandidate(
                document.Id,
                document.ConversionStatus == DocumentConversionStatus.Completed))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.IsInterruptedHandoff)
            {
                if (await ProcessInterruptedHandoffAsync(candidate.Id, cancellationToken))
                {
                    processed++;
                }

                continue;
            }

            if (!await TryClaimQueuedDocumentAsync(candidate.Id, cancellationToken))
            {
                continue;
            }

            var document = await dbContext.MarkdownDocuments.FindAsync([candidate.Id], cancellationToken);
            if (document is null)
            {
                continue;
            }

            await ProcessClaimedDocumentAsync(document, cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task ProcessClaimedDocumentAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        string markdown;
        try
        {
            markdown = await ConvertAndPersistMarkdownAsync(document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetClaimedDocumentAsync(document.Id);
            throw;
        }
        catch (Exception ex)
        {
            await MarkConversionFailedAsync(document, GetSafeConversionErrorMessage(ex), cancellationToken);
            logger.LogWarning(ex, "Document conversion failed for document {DocumentId}.", document.Id);
            return;
        }

        try
        {
            await QueueConvertedMarkdownForIndexingAsync(document, markdown, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task<bool> TryClaimQueuedDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var affectedRows = await dbContext.MarkdownDocuments
            .Where(document =>
                document.Id == documentId &&
                document.ConversionStatus == DocumentConversionStatus.Queued &&
                document.RagStatus == DocumentIntakeStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(document => document.RagStatus, DocumentIntakeStatus.Processing)
                .SetProperty(document => document.RagCurrentStage, "Converting")
                .SetProperty(document => document.ConversionStatus, DocumentConversionStatus.Processing)
                .SetProperty(document => document.ConversionStartedAt, now)
                .SetProperty(document => document.ConversionCompletedAt, (DateTime?)null)
                .SetProperty(document => document.ConversionErrorMessage, (string?)null)
                .SetProperty(document => document.RagErrorMessage, (string?)null),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<string> ConvertAndPersistMarkdownAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.OriginalFilePath))
        {
            throw new InvalidOperationException(GenericConversionFailureMessage);
        }

        var sourceFile = artifactStore.GetFileInfo(document.OriginalFilePath);
        var result = await converter.ConvertAsync(
            sourceFile,
            document.OriginalFileName ?? document.FileName,
            document.OriginalContentType,
            cancellationToken);

        var markdown = result.Markdown.Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException(EmptyMarkdownMessage);
        }

        var converted = await artifactStore.SaveConvertedMarkdownAsync(document.Id, markdown, cancellationToken);
        document.Content = markdown;
        document.ConvertedMarkdownPath = converted.RelativePath;
        document.ConvertedMarkdownHash = converted.Hash;
        document.ConversionStatus = DocumentConversionStatus.Completed;
        document.ConversionCompletedAt = DateTime.UtcNow;
        document.ConversionTool = ConversionTool;
        document.ConversionToolVersion = ConversionToolVersion;

        await dbContext.SaveChangesAsync(cancellationToken);
        return markdown;
    }

    private async Task<bool> ProcessInterruptedHandoffAsync(int documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.MarkdownDocuments
            .Where(document =>
                document.Id == documentId &&
                document.ConversionStatus == DocumentConversionStatus.Completed &&
                document.RagStatus == DocumentIntakeStatus.Processing &&
                document.ConvertedMarkdownPath != null &&
                document.ConvertedMarkdownPath != string.Empty &&
                document.ActiveRagTaskId == null)
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return false;
        }

        string markdown;
        try
        {
            markdown = await artifactStore.ReadConvertedMarkdownAsync(document.ConvertedMarkdownPath!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Converted document artifact could not be read for document {DocumentId}.", document.Id);
            await MarkRagHandoffFailedAsync(document, CancellationToken.None);
            return true;
        }

        document.Content = markdown;
        await QueueConvertedMarkdownForIndexingAsync(document, markdown, cancellationToken);
        return true;
    }

    private async Task QueueConvertedMarkdownForIndexingAsync(
        MarkdownDocument document,
        string markdown,
        CancellationToken cancellationToken)
    {
        string? taskId;
        try
        {
            taskId = await ragTaskQueue.EnqueueTaskAsync(
                document.Id,
                markdown,
                document.FileUrl ?? document.FileName,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var activeTask = await GetActiveIndexTaskByDocumentIdAsync(document.Id);
            if (activeTask is not null)
            {
                ApplyActiveTask(document, activeTask);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return;
            }

            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document RAG queue handoff failed for document {DocumentId}.", document.Id);
            taskId = null;
        }

        if (string.IsNullOrWhiteSpace(taskId))
        {
            var activeTask = await GetActiveIndexTaskByDocumentIdAsync(document.Id);
            if (activeTask is not null)
            {
                ApplyActiveTask(document, activeTask);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return;
            }

            ApplyRagHandoffFailure(document);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        document.RagStatus = DocumentIntakeStatus.Queued;
        document.RagCurrentStage = "Indexing";
        document.ActiveRagTaskId = taskId;
        document.RagProgress = 0;
        document.RagErrorMessage = null;

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<RagTask?> GetActiveIndexTaskByDocumentIdAsync(int documentId)
    {
        try
        {
            var task = await ragTaskQueue.GetTaskByDocumentIdAsync(documentId, CancellationToken.None);
            return task is
            {
                OperationType: RagTaskOperationType.IndexDocument,
                Status: RagTaskStatus.Pending or RagTaskStatus.Processing
            }
                ? task
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document RAG queue reconciliation failed for document {DocumentId}.", documentId);
            return null;
        }
    }

    private static void ApplyActiveTask(MarkdownDocument document, RagTask task)
    {
        document.RagStatus = task.Status switch
        {
            RagTaskStatus.Pending => DocumentIntakeStatus.Queued,
            RagTaskStatus.Processing => DocumentIntakeStatus.Processing,
            _ => document.RagStatus
        };
        document.RagCurrentStage = task.CurrentStage?.ToString() ?? "Indexing";
        document.ActiveRagTaskId = task.TaskId;
        document.RagProgress = task.Progress;
        document.RagErrorMessage = null;
    }

    private async Task MarkRagHandoffFailedAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        ApplyRagHandoffFailure(document);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyRagHandoffFailure(MarkdownDocument document)
    {
        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagCurrentStage = "Indexing";
        document.RagErrorMessage = RagQueueFailureMessage;
        document.ActiveRagTaskId = null;
    }

    private async Task ResetClaimedDocumentAsync(int documentId)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            var document = await dbContext.MarkdownDocuments.FindAsync([documentId], CancellationToken.None);
            if (document is null)
            {
                return;
            }

            document.RagStatus = DocumentIntakeStatus.Queued;
            document.RagCurrentStage = "Accepted";
            document.ConversionStatus = DocumentConversionStatus.Queued;
            document.ConversionStartedAt = null;
            document.ConversionCompletedAt = null;
            document.ConversionErrorMessage = null;
            document.RagErrorMessage = null;
            document.ActiveRagTaskId = null;

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reset cancelled document conversion claim for document {DocumentId}.", documentId);
        }
    }

    private async Task MarkConversionFailedAsync(
        MarkdownDocument document,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagCurrentStage = "Converting";
        document.ConversionStatus = DocumentConversionStatus.Failed;
        document.ConversionCompletedAt = DateTime.UtcNow;
        document.ConversionErrorMessage = errorMessage;
        document.RagErrorMessage = errorMessage;
        document.ActiveRagTaskId = null;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetSafeConversionErrorMessage(Exception exception)
    {
        return string.Equals(exception.Message, EmptyMarkdownMessage, StringComparison.Ordinal)
            ? EmptyMarkdownMessage
            : GenericConversionFailureMessage;
    }

    private sealed record DocumentConversionCandidate(int Id, bool IsInterruptedHandoff);
}
