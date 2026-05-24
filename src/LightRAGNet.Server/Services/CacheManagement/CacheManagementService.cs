using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheManagementService(
    ICacheMetricsStore metricsStore,
    CacheEntryInspector entryInspector,
    LightRagLlmCacheService llmCacheService,
    CacheClearPlanner clearPlanner,
    ILogger<CacheManagementService> logger)
{
    private static readonly string[] FamilyOrder =
    [
        LightRagCacheKeyBuilder.QueryCacheType,
        LightRagCacheKeyBuilder.KeywordsCacheType,
        LightRagCacheKeyBuilder.ExtractCacheType,
        LightRagCacheKeyBuilder.SummaryCacheType
    ];

    public async Task<CacheOverviewResponse> GetOverviewAsync(
        string? workspace,
        string? window,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var normalizedWindow = string.Equals(window, "7d", StringComparison.OrdinalIgnoreCase) ? "7d" : "24h";
        var generatedAt = DateTimeOffset.UtcNow;
        var from = normalizedWindow == "7d" ? generatedAt.AddDays(-7) : generatedAt.AddHours(-24);

        var metrics = await metricsStore.ReadAsync(from, generatedAt, cancellationToken);
        var workspaceMetrics = metrics
            .Where(metric => string.Equals(metric.Workspace, normalizedWorkspace, StringComparison.Ordinal))
            .ToList();
        var readMetrics = workspaceMetrics
            .Where(metric => string.Equals(metric.Operation, CacheMetricOperation.Read, StringComparison.Ordinal))
            .Where(metric => IsHitOrMiss(metric.Outcome))
            .ToList();
        var currentRevision = await llmCacheService.GetWorkspaceQueryRevisionAsync(normalizedWorkspace, cancellationToken);
        var inventory = await entryInspector.InspectAsync(currentRevision, cancellationToken);
        var summary = CreateSummary(readMetrics, inventory);
        var families = CreateFamilies(readMetrics, inventory);
        var trend = CreateTrend(readMetrics, normalizedWindow);
        var insights = CreateInsights(summary, inventory);
        var clearPlan = clearPlanner.CreatePlans(inventory);
        var entrySamples = CreateEntrySamples(inventory, readMetrics);

        return new CacheOverviewResponse(
            normalizedWorkspace,
            normalizedWindow,
            generatedAt,
            summary,
            families,
            trend,
            insights,
            clearPlan,
            entrySamples);
    }

    public async Task<CacheClearResponse> ClearAsync(
        CacheClearRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedWorkspace = NormalizeWorkspace(request.Workspace);
        var currentRevision = await llmCacheService.GetWorkspaceQueryRevisionAsync(normalizedWorkspace, cancellationToken);
        var inventory = await entryInspector.InspectAsync(currentRevision, cancellationToken);
        var plan = clearPlanner
            .CreatePlans(inventory)
            .FirstOrDefault(item => string.Equals(item.Id, request.PlanId, StringComparison.Ordinal));

        if (plan is null)
        {
            return new CacheClearResponse(
                Succeeded: false,
                DeletedEntries: 0,
                CacheTypes: [],
                Message: $"Unknown cache clear plan '{request.PlanId}'.",
                RevisionAfter: currentRevision);
        }

        if (plan.RequiresConfirmation && !request.Confirm)
        {
            return new CacheClearResponse(
                Succeeded: false,
                DeletedEntries: 0,
                CacheTypes: plan.CacheTypes,
                Message: $"Cache clear plan '{request.PlanId}' requires confirmation.",
                RevisionAfter: currentRevision);
        }

        logger.LogInformation(
            "Cache clear requested for workspace {Workspace} plan {PlanId}, but clear execution is not implemented in this task.",
            normalizedWorkspace,
            request.PlanId);

        return new CacheClearResponse(
            Succeeded: false,
            DeletedEntries: 0,
            CacheTypes: plan.CacheTypes,
            Message: "Cache clear execution will be implemented in Task 6.",
            RevisionAfter: currentRevision);
    }

    private static CacheSummaryDto CreateSummary(
        IReadOnlyList<CacheMetricEvent> readMetrics,
        IReadOnlyList<CacheInventoryEntry> inventory)
    {
        var hits = CountOutcome(readMetrics, CacheReadOutcome.Hit);
        var misses = CountOutcome(readMetrics, CacheReadOutcome.Miss);
        var attempts = hits + misses;
        var averageMissFactoryDuration = AverageMissFactoryDuration(readMetrics);

        return new CacheSummaryDto(
            attempts > 0 ? (double)hits / attempts : null,
            hits,
            EstimateLatencySavedMs(averageMissFactoryDuration, hits),
            inventory.Count(entry => !string.Equals(entry.State, "current", StringComparison.Ordinal)),
            attempts > 0);
    }

    private static IReadOnlyList<CacheFamilyDto> CreateFamilies(
        IReadOnlyList<CacheMetricEvent> readMetrics,
        IReadOnlyList<CacheInventoryEntry> inventory)
    {
        return FamilyOrder
            .Select(cacheType =>
            {
                var familyReads = readMetrics
                    .Where(metric => string.Equals(metric.CacheType, cacheType, StringComparison.Ordinal))
                    .ToList();
                var hits = CountOutcome(familyReads, CacheReadOutcome.Hit);
                var misses = CountOutcome(familyReads, CacheReadOutcome.Miss);
                var attempts = hits + misses;
                var entryCount = inventory.Count(entry => string.Equals(entry.CacheType, cacheType, StringComparison.Ordinal));
                var averageMissFactoryDuration = AverageMissFactoryDuration(familyReads);

                return new CacheFamilyDto(
                    cacheType,
                    GetDisplayName(cacheType),
                    attempts > 0 ? (double)hits / attempts : null,
                    hits,
                    misses,
                    attempts,
                    entryCount,
                    GetValueLevel(cacheType),
                    GetRiskLevel(cacheType),
                    hits,
                    EstimateLatencySavedMs(averageMissFactoryDuration, hits),
                    CreateFamilyMessage(cacheType, attempts, entryCount));
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
                    reads > 0 ? (double)hits / reads : null,
                    hits);
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
                "No measured reads",
                "Cache hit rate will appear after read metrics are recorded.",
                "info"));
        }

        var staleQueryCount = inventory.Count(entry =>
            string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && string.Equals(entry.State, "old revision", StringComparison.Ordinal));
        if (staleQueryCount > 0)
        {
            insights.Add(new CacheInsightDto(
                "Query cache revision review",
                $"{staleQueryCount} query cache entries use older revisions.",
                "warning"));
        }

        return insights;
    }

    private static IReadOnlyList<CacheEntrySampleDto> CreateEntrySamples(
        IReadOnlyList<CacheInventoryEntry> inventory,
        IReadOnlyList<CacheMetricEvent> readMetrics)
    {
        var lastHits = readMetrics
            .Where(metric => string.Equals(metric.Outcome, CacheReadOutcome.Hit, StringComparison.Ordinal))
            .Where(metric => !string.IsNullOrWhiteSpace(metric.CacheKeyPrefix))
            .GroupBy(metric => metric.CacheKeyPrefix!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(metric => metric.Timestamp),
                StringComparer.Ordinal);

        return inventory
            .Take(25)
            .Select(entry => new CacheEntrySampleDto(
                entry.CacheKeyPrefix,
                entry.CacheType,
                lastHits.TryGetValue(entry.CacheKeyPrefix, out var lastHit) ? lastHit : null,
                entry.State))
            .ToList();
    }

    private static string GetDisplayName(string cacheType)
    {
        return cacheType switch
        {
            LightRagCacheKeyBuilder.QueryCacheType => "Query answers",
            LightRagCacheKeyBuilder.KeywordsCacheType => "Keyword extraction",
            LightRagCacheKeyBuilder.ExtractCacheType => "Document extraction",
            LightRagCacheKeyBuilder.SummaryCacheType => "Summaries",
            _ => cacheType
        };
    }

    private static string GetValueLevel(string cacheType)
    {
        return cacheType switch
        {
            LightRagCacheKeyBuilder.QueryCacheType => "high",
            LightRagCacheKeyBuilder.KeywordsCacheType => "medium",
            LightRagCacheKeyBuilder.ExtractCacheType => "high",
            LightRagCacheKeyBuilder.SummaryCacheType => "medium",
            _ => "unknown"
        };
    }

    private static string GetRiskLevel(string cacheType)
    {
        return cacheType switch
        {
            LightRagCacheKeyBuilder.QueryCacheType => "medium",
            LightRagCacheKeyBuilder.KeywordsCacheType => "low",
            LightRagCacheKeyBuilder.ExtractCacheType => "low",
            LightRagCacheKeyBuilder.SummaryCacheType => "medium",
            _ => "unknown"
        };
    }

    private static string CreateFamilyMessage(string cacheType, int attempts, int entryCount)
    {
        if (attempts == 0)
        {
            return entryCount == 0
                ? "No measured reads or inventory entries."
                : "Inventory entries exist, but no measured reads were recorded.";
        }

        return $"{attempts} measured read attempts for {cacheType} cache.";
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
