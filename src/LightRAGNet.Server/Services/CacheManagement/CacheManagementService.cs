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
        var inventory = await entryInspector.InspectAsync(normalizedWorkspace, currentRevision, cancellationToken);
        var families = CreateFamilies(readMetrics, inventory);
        var summary = CreateSummary(readMetrics, inventory, families);
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
        var inventory = await entryInspector.InspectAsync(normalizedWorkspace, currentRevision, cancellationToken);
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

        var keys = request.PlanId switch
        {
            "stale-query-cache" => inventory
                .Where(entry =>
                    string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
                    && string.Equals(entry.State, "old revision", StringComparison.Ordinal))
                .Select(entry => entry.Key)
                .ToList(),
            "summary-cache-review" => inventory
                .Where(entry => string.Equals(entry.CacheType, LightRagCacheKeyBuilder.SummaryCacheType, StringComparison.Ordinal))
                .Select(entry => entry.Key)
                .ToList(),
            "all-llm-cache" => inventory
                .Select(entry => entry.Key)
                .ToList(),
            _ => []
        };

        if (keys.Count == 0)
        {
            return new CacheClearResponse(
                Succeeded: true,
                DeletedEntries: 0,
                CacheTypes: plan.CacheTypes,
                Message: $"No cache entries matched clear plan '{request.PlanId}'.",
                RevisionAfter: currentRevision);
        }

        await entryInspector.DeleteAsync(keys, cancellationToken);
        logger.LogInformation(
            "Deleted {DeletedEntries} cache entries for workspace {Workspace} plan {PlanId}.",
            keys.Count,
            normalizedWorkspace,
            request.PlanId);

        return new CacheClearResponse(
            Succeeded: true,
            DeletedEntries: keys.Count,
            CacheTypes: plan.CacheTypes,
            Message: $"Deleted {keys.Count} cache entries for clear plan '{request.PlanId}'.",
            RevisionAfter: currentRevision);
    }

    private static CacheSummaryDto CreateSummary(
        IReadOnlyList<CacheMetricEvent> readMetrics,
        IReadOnlyList<CacheInventoryEntry> inventory,
        IReadOnlyList<CacheFamilyDto> families)
    {
        var hits = CountOutcome(readMetrics, CacheReadOutcome.Hit);
        var misses = CountOutcome(readMetrics, CacheReadOutcome.Miss);
        var attempts = hits + misses;
        var familyEstimates = families
            .Select(family => family.EstimatedLatencySavedMs)
            .Where(estimate => estimate is not null)
            .Select(estimate => estimate!.Value)
            .ToList();

        return new CacheSummaryDto(
            attempts > 0 ? (double)hits / attempts : null,
            hits,
            familyEstimates.Count > 0 ? familyEstimates.Sum() : null,
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
                var familyInventory = inventory
                    .Where(entry => string.Equals(entry.CacheType, cacheType, StringComparison.Ordinal))
                    .ToList();
                var entryCount = familyInventory.Count;
                var hitRate = attempts > 0 ? (double)hits / attempts : (double?)null;
                var averageMissFactoryDuration = AverageMissFactoryDuration(familyReads);

                return new CacheFamilyDto(
                    cacheType,
                    GetDisplayName(cacheType),
                    hitRate,
                    hits,
                    misses,
                    attempts,
                    entryCount,
                    GetValueLevel(attempts, hitRate),
                    GetRiskLevel(cacheType, familyInventory),
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

    private static string GetValueLevel(int attempts, double? hitRate)
    {
        if (attempts == 0 || hitRate is null)
        {
            return "NotMeasured";
        }

        return hitRate.Value switch
        {
            >= 0.8d => "VeryHigh",
            >= 0.6d => "High",
            >= 0.3d => "Medium",
            _ => "Low"
        };
    }

    private static string GetRiskLevel(string cacheType, IReadOnlyList<CacheInventoryEntry> familyInventory)
    {
        if (string.Equals(cacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && familyInventory.Any(entry => string.Equals(entry.State, "old revision", StringComparison.Ordinal)))
        {
            return "OldRevision";
        }

        if (string.Equals(cacheType, LightRagCacheKeyBuilder.ExtractCacheType, StringComparison.Ordinal)
            && familyInventory.Any(entry => string.Equals(entry.State, "doc-linked", StringComparison.Ordinal)))
        {
            return "DocLinked";
        }

        return "Current";
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
