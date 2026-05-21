using System.Security.Cryptography;
using System.Text;
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
    public async Task<DocumentSubmissionResponse> SubmitTextDocumentsAsync(
        SubmitTextDocumentsRequest request,
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
                    FileUrl = CreateTextSourceUri(trackId, input.FileName),
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

    private static string CreateTrackId()
    {
        return $"track-{Guid.NewGuid():N}";
    }

    private static string CreateTextSourceUri(string trackId, string fileName)
    {
        return $"text://{trackId}/{Uri.EscapeDataString(fileName)}";
    }

    private static bool IsQueuedStatus(string? status)
    {
        return status is DocumentIntakeStatus.Queued or "Pending";
    }

    private static void MarkQueueFailed(MarkdownDocument document, string errorMessage)
    {
        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Document could not be queued."
            : errorMessage;
    }
}
