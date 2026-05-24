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
                "Review stale query cache",
                [LightRagCacheKeyBuilder.QueryCacheType],
                staleQueryCount,
                "Low",
                "Reviews query answers tied to older revisions before cleanup.",
                false),
            new CacheClearPlanDto(
                "summary-cache-review",
                "Review summary cache",
                [LightRagCacheKeyBuilder.SummaryCacheType],
                summaryCount,
                "Medium",
                "Requires confirmation and review because summary cache entries may be reused by future merge work.",
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
                "High",
                "Reviews every LLM cache entry and may increase provider calls until cache warms again.",
                true)
        ];
    }
}
