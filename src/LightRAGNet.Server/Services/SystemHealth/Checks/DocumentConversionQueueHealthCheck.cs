using LightRAGNet.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class DocumentConversionQueueHealthCheck(IDbContextFactory<AppDbContext> dbContextFactory) : ISystemHealthCheck
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(30);

    public string Id => "document-conversion-queue";

    public string Name => "Conversion queue";

    public string Category => "Workers";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var dbContext = dbContextFactory.CreateDbContext();

        var documents = await dbContext.MarkdownDocuments
            .Select(document => new ConversionProbeDocument
            {
                ConversionStatus = document.ConversionStatus,
                UploadTime = document.UploadTime,
                LastModified = document.LastModified,
                ConversionStartedAt = document.ConversionStartedAt
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var queued = documents.Count(document => document.ConversionStatus == DocumentConversionStatus.Queued);
        var processing = documents.Count(document => document.ConversionStatus == DocumentConversionStatus.Processing);
        var failed = documents.Count(document => document.ConversionStatus == DocumentConversionStatus.Failed);
        var completed = documents.Count(document => document.ConversionStatus == DocumentConversionStatus.Completed);
        var notStarted = documents.Count(document => document.ConversionStatus == DocumentConversionStatus.NotStarted);
        var staleActive = documents.Count(document => IsStaleActive(document, now));
        var evidence = new Dictionary<string, object?>
        {
            ["total"] = documents.Count,
            ["notStarted"] = notStarted,
            ["queued"] = queued,
            ["processing"] = processing,
            ["completed"] = completed,
            ["failed"] = failed,
            ["staleActive"] = staleActive
        };

        if (failed > 0 || staleActive > 0)
        {
            return SystemHealthCheckResult.Degraded(
                Id,
                Name,
                Category,
                "Document conversion queue has failed or stale active conversions.",
                "Review failed conversions and stale queued or processing conversions; retry failed documents or restart the conversion worker if needed.",
                ["PDF/DOCX Conversion"],
                evidence);
        }

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Document conversion queue is healthy.",
            evidence);
    }

    private static bool IsStaleActive(ConversionProbeDocument document, DateTime now)
    {
        var activeSince = document.ConversionStatus switch
        {
            DocumentConversionStatus.Queued => document.LastModified ?? document.UploadTime,
            DocumentConversionStatus.Processing => document.ConversionStartedAt ?? document.LastModified ?? document.UploadTime,
            _ => (DateTime?)null
        };

        return activeSince.HasValue && now - activeSince.Value.ToUniversalTime() > StaleThreshold;
    }

    private sealed class ConversionProbeDocument
    {
        public string? ConversionStatus { get; init; }

        public DateTime UploadTime { get; init; }

        public DateTime? LastModified { get; init; }

        public DateTime? ConversionStartedAt { get; init; }
    }
}
