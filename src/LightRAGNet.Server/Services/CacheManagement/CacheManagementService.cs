using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheManagementService(
    ICacheMetricsStore metricsStore,
    CacheEntryInspector entryInspector,
    CacheClearPlanner clearPlanner,
    ILogger<CacheManagementService> logger)
{
    public async Task<CacheOverviewResponse> GetOverviewAsync(
        string? workspace,
        string? window,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var normalizedWindow = string.Equals(window, "7d", StringComparison.OrdinalIgnoreCase) ? "7d" : "24h";
        var to = DateTimeOffset.UtcNow;
        var from = normalizedWindow == "7d" ? to.AddDays(-7) : to.AddHours(-24);

        var metrics = await metricsStore.ReadAsync(from, to, cancellationToken);
        var workspaceMetrics = metrics
            .Where(metric => string.Equals(metric.Workspace, normalizedWorkspace, StringComparison.Ordinal))
            .ToList();
        var readMetrics = workspaceMetrics
            .Where(metric => string.Equals(metric.Operation, CacheMetricOperation.Read, StringComparison.Ordinal))
            .Where(metric => IsHitOrMiss(metric.Outcome))
            .ToList();
        var inventory = await entryInspector.InspectAsync(currentRevision: 0, cancellationToken);
        var summary = CreateSummary(normalizedWorkspace, normalizedWindow, from, to, readMetrics, inventory.Count);
        var families = CreateFamilies(readMetrics, inventory);
        var trend = CreateTrend(readMetrics, normalizedWindow);
        var insights = CreateInsights(summary, inventory);
        var clearPlans = clearPlanner.CreatePlans(inventory);
        var samples = inventory
            .Take(25)
            .Select(entry => new CacheEntrySampleDto(
                entry.KeyPrefix,
                entry.CacheType,
                entry.State,
                entry.ChunkId,
                entry.CreateTime))
            .ToList();

        return new CacheOverviewResponse(summary, families, trend, insights, clearPlans, samples);
    }

    public Task<CacheClearResponse> ClearAsync(
        CacheClearRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Cache clear requested for plan {PlanId}, but clear execution is not implemented in this task.",
            request.PlanId);

        return Task.FromResult(new CacheClearResponse(
            Success: false,
            Message: "Cache clear execution will be implemented after clear plan filtering.",
            PlanId: request.PlanId,
            ClearedCount: 0,
            Errors: ["clear execution is not implemented"]));
    }

    private static CacheSummaryDto CreateSummary(
        string workspace,
        string window,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<CacheMetricEvent> readMetrics,
        int inventoryEntryCount)
    {
        var hits = CountOutcome(readMetrics, CacheReadOutcome.Hit);
        var misses = CountOutcome(readMetrics, CacheReadOutcome.Miss);
        var totalReads = hits + misses;
        var averageMissFactoryDuration = AverageMissFactoryDuration(readMetrics);

        return new CacheSummaryDto(
            workspace,
            window,
            from,
            to,
            totalReads > 0,
            totalReads > 0 ? (double)hits / totalReads : null,
            totalReads,
            hits,
            misses,
            hits,
            EstimateLatencySavedMs(averageMissFactoryDuration, hits),
            inventoryEntryCount);
    }

    private static IReadOnlyList<CacheFamilyDto> CreateFamilies(
        IReadOnlyList<CacheMetricEvent> readMetrics,
        IReadOnlyList<CacheInventoryEntry> inventory)
    {
        var names = readMetrics
            .Select(metric => metric.CacheType)
            .Concat(inventory.Select(entry => entry.CacheType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return names
            .Select(name =>
            {
                var familyReads = readMetrics
                    .Where(metric => string.Equals(metric.CacheType, name, StringComparison.Ordinal))
                    .ToList();
                var hits = CountOutcome(familyReads, CacheReadOutcome.Hit);
                var misses = CountOutcome(familyReads, CacheReadOutcome.Miss);
                var reads = hits + misses;
                var averageMissFactoryDuration = AverageMissFactoryDuration(familyReads);

                return new CacheFamilyDto(
                    name,
                    inventory.Count(entry => string.Equals(entry.CacheType, name, StringComparison.Ordinal)),
                    reads,
                    hits,
                    misses,
                    reads > 0,
                    reads > 0 ? (double)hits / reads : null,
                    hits,
                    EstimateLatencySavedMs(averageMissFactoryDuration, hits));
            })
            .ToList();
    }

    private static IReadOnlyList<CacheTrendPointDto> CreateTrend(
        IReadOnlyList<CacheMetricEvent> readMetrics,
        string window)
    {
        return readMetrics
            .GroupBy(metric => window == "7d"
                ? new DateTimeOffset(metric.Timestamp.UtcDateTime.Date, TimeSpan.Zero)
                : new DateTimeOffset(
                    metric.Timestamp.UtcDateTime.Year,
                    metric.Timestamp.UtcDateTime.Month,
                    metric.Timestamp.UtcDateTime.Day,
                    metric.Timestamp.UtcDateTime.Hour,
                    0,
                    0,
                    TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var metrics = group.ToList();
                var hits = CountOutcome(metrics, CacheReadOutcome.Hit);
                var misses = CountOutcome(metrics, CacheReadOutcome.Miss);
                var reads = hits + misses;

                return new CacheTrendPointDto(
                    group.Key,
                    reads,
                    hits,
                    misses,
                    reads > 0 ? (double)hits / reads : null);
            })
            .ToList();
    }

    private static IReadOnlyList<CacheInsightDto> CreateInsights(
        CacheSummaryDto summary,
        IReadOnlyList<CacheInventoryEntry> inventory)
    {
        var insights = new List<CacheInsightDto>();
        if (!summary.Measured)
        {
            insights.Add(new CacheInsightDto(
                "info",
                "No measured reads",
                "Cache hit rate will appear after read metrics are recorded."));
        }

        var staleQueryCount = inventory.Count(entry =>
            string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && string.Equals(entry.State, "old revision", StringComparison.Ordinal));
        if (staleQueryCount > 0)
        {
            insights.Add(new CacheInsightDto(
                "warning",
                "Stale query cache",
                $"{staleQueryCount} query cache entries use older workspace revisions."));
        }

        return insights;
    }

    private static bool IsHitOrMiss(string? outcome)
    {
        return string.Equals(outcome, CacheReadOutcome.Hit, StringComparison.Ordinal)
            || string.Equals(outcome, CacheReadOutcome.Miss, StringComparison.Ordinal);
    }

    private static int CountOutcome(IEnumerable<CacheMetricEvent> metrics, string outcome)
    {
        return metrics.Count(metric => string.Equals(metric.Outcome, outcome, StringComparison.Ordinal));
    }

    private static double? AverageMissFactoryDuration(IEnumerable<CacheMetricEvent> metrics)
    {
        var durations = metrics
            .Where(metric => string.Equals(metric.Outcome, CacheReadOutcome.Miss, StringComparison.Ordinal))
            .Select(metric => metric.FactoryDurationMs)
            .Where(duration => duration is not null)
            .Select(duration => duration!.Value)
            .ToList();

        return durations.Count == 0 ? null : durations.Average();
    }

    private static long? EstimateLatencySavedMs(double? averageMissFactoryDuration, int hits)
    {
        return averageMissFactoryDuration is null
            ? null
            : Convert.ToInt64(Math.Round(averageMissFactoryDuration.Value * hits, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeWorkspace(string? workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }
}
