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
                "low",
                "Removes query answers tied to older workspace revisions.",
                "stale-query-cache",
                staleQueryCount,
                staleQueryCount > 0),
            new CacheClearPlanDto(
                "unused-summary-cache",
                "Unused summary cache",
                "medium",
                "Removes summary entries that may be recreated by future merge work.",
                "unused-summary-cache",
                summaryCount,
                summaryCount > 0),
            new CacheClearPlanDto(
                "all-llm-cache",
                "All LLM cache",
                "high",
                "Removes every LLM cache entry and may increase provider calls until cache warms again.",
                "all-llm-cache",
                inventory.Count,
                inventory.Count > 0)
        ];
    }
}
