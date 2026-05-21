using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Extensions;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services;

public sealed class DocumentIntakeService(
    AppDbContext context,
    IRagTaskQueueService taskQueueService,
    ILogger<DocumentIntakeService> logger)
{
    private const long MaxUploadFileSize = 10 * 1024 * 1024;

    public async Task<DocumentSubmissionResponse> SubmitTextDocumentsAsync(
        SubmitTextDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        return await SubmitDocumentsAsync(request, "text", cancellationToken);
    }

    private async Task<DocumentSubmissionResponse> SubmitDocumentsAsync(
        SubmitTextDocumentsRequest request,
        string sourceScheme,
        CancellationToken cancellationToken)
    {
        if (request.Documents.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(request));
        }

        if (request.Documents.Any(d =>
                string.IsNullOrWhiteSpace(d.FileName) ||
                string.IsNullOrWhiteSpace(d.Content)))
        {
            throw new ArgumentException("Every document requires a file name and content.", nameof(request));
        }

        var trackId = CreateTrackId();
        var now = DateTime.UtcNow;
        var documents = request.Documents
            .Select(input =>
            {
                var bytes = Encoding.UTF8.GetBytes(input.Content);
                return new MarkdownDocument
                {
                    FileName = input.FileName,
                    Content = input.Content,
                    FileSize = bytes.LongLength,
                    UploadTime = now,
                    FileUrl = CreateSourceUri(sourceScheme, trackId, input.FileName),
                    TrackId = trackId,
                    RagStatus = DocumentIntakeStatus.Queued,
                    RagCurrentStage = "Accepted",
                    IsInRagSystem = false,
                    RagProgress = 0,
                    FileHash = Convert.ToHexStringLower(SHA256.HashData(bytes))
                };
            })
            .ToList();

        context.MarkdownDocuments.AddRange(documents);
        await context.SaveChangesAsync(cancellationToken);

        var hasQueueFailure = false;
        foreach (var document in documents)
        {
            try
            {
                var taskId = await taskQueueService.EnqueueTaskAsync(
                    document.Id,
                    document.Content,
                    document.FileUrl ?? document.FileName,
                    cancellationToken);

                if (taskId is null)
                {
                    MarkQueueFailed(
                        document,
                        "Document could not be queued because an active task already exists.");
                    hasQueueFailure = true;
                    logger.LogWarning("Document intake queue rejected document {DocumentId}", document.Id);
                    continue;
                }

                document.ActiveRagTaskId = taskId;
            }
            catch (Exception ex)
            {
                MarkQueueFailed(document, ex.Message);
                hasQueueFailure = true;
                logger.LogWarning(ex, "Document intake queue failed for document {DocumentId}", document.Id);
            }
        }

        await context.SaveChangesAsync(hasQueueFailure ? CancellationToken.None : cancellationToken);

        return new DocumentSubmissionResponse
        {
            TrackId = trackId,
            Documents = documents.Select(d => d.ToDto()).ToList()
        };
    }

    public async Task<DocumentSubmissionResponse> SubmitUploadedFilesAsync(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException("At least one file is required.", nameof(files));
        }

        var documents = new List<TextDocumentInput>(files.Count);
        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                throw new ArgumentException("File cannot be empty.", nameof(files));
            }

            if (file.Length > MaxUploadFileSize)
            {
                throw new ArgumentException("File size cannot exceed 10MB.", nameof(files));
            }

            var extension = Path.GetExtension(file.FileName);
            if (!IsSupportedUploadExtension(extension))
            {
                throw new ArgumentException("Only .md, .markdown, or .txt files are supported.", nameof(files));
            }

            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            documents.Add(new TextDocumentInput
            {
                FileName = file.FileName,
                Content = await reader.ReadToEndAsync(cancellationToken)
            });
        }

        return await SubmitDocumentsAsync(
            new SubmitTextDocumentsRequest { Documents = documents },
            "upload",
            cancellationToken);
    }

    public async Task<DocumentTrackStatusResponse?> GetTrackStatusAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        var documents = await context.MarkdownDocuments
            .Where(d => d.TrackId == trackId)
            .OrderBy(d => d.UploadTime)
            .Select(d => d.ToDto())
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return null;
        }

        return new DocumentTrackStatusResponse
        {
            TrackId = trackId,
            TotalCount = documents.Count,
            QueuedCount = documents.Count(d => IsQueuedStatus(d.RagStatus)),
            ProcessingCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Processing),
            CompletedCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Completed),
            FailedCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Failed),
            CancelledCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Cancelled),
            Documents = documents
        };
    }

    public async Task<DocumentPipelineActionResult?> RetryDocumentAsync(
        int documentId,
        CancellationToken cancellationToken)
    {
        var document = await context.MarkdownDocuments.FindAsync([documentId], cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (!DocumentIntakeStatus.IsRetryable(document.RagStatus))
        {
            throw new InvalidOperationException("Document is not retryable.");
        }

        var taskId = await taskQueueService.EnqueueTaskAsync(
            document.Id,
            document.Content,
            document.FileUrl ?? document.FileName,
            cancellationToken);

        if (taskId is null)
        {
            throw new InvalidOperationException("Document could not be queued because an active task already exists.");
        }

        document.RagRetryCount++;
        document.RagErrorMessage = null;
        document.RagStatus = DocumentIntakeStatus.Queued;
        document.RagCurrentStage = "Accepted";
        document.RagProgress = 0;
        document.PipelineStartedAt = null;
        document.PipelineCompletedAt = null;
        document.PipelineCancelledAt = null;
        document.ActiveRagTaskId = taskId;
        await context.SaveChangesAsync(cancellationToken);

        return new DocumentPipelineActionResult
        {
            Accepted = true,
            DocumentId = document.Id,
            Status = DocumentIntakeStatus.Queued,
            Message = "Document retry has been queued."
        };
    }

    public async Task<DocumentPipelineActionResult?> CancelDocumentAsync(
        int documentId,
        CancellationToken cancellationToken)
    {
        var document = await context.MarkdownDocuments.FindAsync([documentId], cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (!DocumentIntakeStatus.IsCancellable(document.RagStatus))
        {
            throw new InvalidOperationException("Document is not cancellable.");
        }

        var cancelled = await CancelDocumentCoreAsync(document, cancellationToken);
        if (!cancelled)
        {
            throw new InvalidOperationException("Document could not be cancelled because the active queue task was not cancelled.");
        }

        await context.SaveChangesAsync(cancellationToken);

        return new DocumentPipelineActionResult
        {
            Accepted = true,
            DocumentId = document.Id,
            Status = DocumentIntakeStatus.Cancelled,
            Message = "Document pipeline has been cancelled."
        };
    }

    public async Task<int> CancelTrackAsync(string trackId, CancellationToken cancellationToken)
    {
        var documents = await context.MarkdownDocuments
            .Where(d => d.TrackId == trackId &&
                        (d.RagStatus == DocumentIntakeStatus.Queued ||
                         d.RagStatus == DocumentIntakeStatus.Processing ||
                         d.RagStatus == "Pending"))
            .ToListAsync(cancellationToken);

        var cancelledCount = 0;
        foreach (var document in documents)
        {
            if (await CancelDocumentCoreAsync(document, cancellationToken))
            {
                cancelledCount++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return cancelledCount;
    }

    private static string CreateTrackId()
    {
        return $"track-{Guid.NewGuid():N}";
    }

    private static string CreateSourceUri(string sourceScheme, string trackId, string fileName)
    {
        return $"{sourceScheme}://{trackId}/{Uri.EscapeDataString(fileName)}";
    }

    private static bool IsQueuedStatus(string? status)
    {
        return status is DocumentIntakeStatus.Queued or "Pending";
    }

    private async Task<bool> CancelDocumentCoreAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        var taskId = document.ActiveRagTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            var activeTask = await taskQueueService.GetTaskByDocumentIdAsync(document.Id, cancellationToken);
            if (activeTask is { Status: RagTaskStatus.Pending or RagTaskStatus.Processing })
            {
                taskId = activeTask.TaskId;
            }
        }

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            var queueCancelled = await taskQueueService.CancelTaskAsync(taskId, cancellationToken);
            if (!queueCancelled)
            {
                return false;
            }
        }
        else if (DocumentIntakeStatus.IsCancellable(document.RagStatus))
        {
            return false;
        }

        document.RagStatus = DocumentIntakeStatus.Cancelled;
        document.RagCurrentStage = DocumentIntakeStatus.Cancelled;
        document.PipelineCancelledAt = DateTime.UtcNow;
        document.ActiveRagTaskId = null;
        return true;
    }

    private static bool IsSupportedUploadExtension(string? extension)
    {
        return extension?.ToLowerInvariant() is ".md" or ".markdown" or ".txt";
    }

    private static void MarkQueueFailed(MarkdownDocument document, string errorMessage)
    {
        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Document could not be queued."
            : errorMessage;
    }
}
