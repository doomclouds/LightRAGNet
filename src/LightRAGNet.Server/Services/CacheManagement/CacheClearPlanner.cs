using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheClearPlanner
{
    public IReadOnlyList<CacheClearPlanDto> CreatePlans(IReadOnlyList<CacheInventoryEntry> inventory)
    {
        var staleQueryCount = inventory.Count(entry =>
            string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && string.Equals(entry.State, "old revision", StringComparison.Ordinal));
        var summaryCount = inventory.Count(entry =>
            string.Equals(entry.CacheType, LightRagCacheKeyBuilder.SummaryCacheType, StringComparison.Ordinal));

        return
        [
            new CacheClearPlanDto(
                "stale-query-cache",
                "Stale query cache",
                [LightRagCacheKeyBuilder.QueryCacheType],
                staleQueryCount,
                "low",
                "Removes query answers tied to older workspace revisions.",
                false),
            new CacheClearPlanDto(
                "unused-summary-cache",
                "Unused summary cache",
                [LightRagCacheKeyBuilder.SummaryCacheType],
                summaryCount,
                "medium",
                "Removes summary entries that may be recreated by future merge work.",
                true),
            new CacheClearPlanDto(
                "all-llm-cache",
                "All LLM cache",
                [
                    LightRagCacheKeyBuilder.QueryCacheType,
                    LightRagCacheKeyBuilder.KeywordsCacheType,
                    LightRagCacheKeyBuilder.ExtractCacheType,
                    LightRagCacheKeyBuilder.SummaryCacheType
                ],
                inventory.Count,
                "high",
                "Removes every LLM cache entry and may increase provider calls until cache warms again.",
                true)
        ];
    }
}
