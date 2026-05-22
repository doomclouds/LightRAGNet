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

        var documents = await dbContext.MarkdownDocuments
            .Where(document =>
                document.ConversionStatus == DocumentConversionStatus.Queued &&
                document.RagStatus == DocumentIntakeStatus.Queued)
            .OrderBy(document => document.UploadTime)
            .Take(maxDocuments)
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            await ProcessDocumentAsync(document, cancellationToken);
        }

        return documents.Count;
    }

    private async Task ProcessDocumentAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        await ClaimDocumentAsync(document, cancellationToken);

        string markdown;
        try
        {
            markdown = await ConvertAndPersistMarkdownAsync(document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkConversionFailedAsync(document, GetSafeConversionErrorMessage(ex), cancellationToken);
            logger.LogWarning(ex, "Document conversion failed for document {DocumentId}.", document.Id);
            return;
        }

        await EnqueueRagIndexingAsync(document, markdown, cancellationToken);
    }

    private async Task ClaimDocumentAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        document.RagStatus = DocumentIntakeStatus.Processing;
        document.RagCurrentStage = "Converting";
        document.ConversionStatus = DocumentConversionStatus.Processing;
        document.ConversionStartedAt = now;
        document.ConversionCompletedAt = null;
        document.ConversionErrorMessage = null;
        document.RagErrorMessage = null;

        await dbContext.SaveChangesAsync(cancellationToken);
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

    private async Task EnqueueRagIndexingAsync(
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
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document RAG queue handoff failed for document {DocumentId}.", document.Id);
            taskId = null;
        }

        if (string.IsNullOrWhiteSpace(taskId))
        {
            document.RagStatus = DocumentIntakeStatus.Failed;
            document.RagCurrentStage = "Indexing";
            document.RagErrorMessage = RagQueueFailureMessage;
            document.ActiveRagTaskId = null;
        }
        else
        {
            document.RagStatus = DocumentIntakeStatus.Queued;
            document.RagCurrentStage = "Indexing";
            document.ActiveRagTaskId = taskId;
            document.RagProgress = 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
}
