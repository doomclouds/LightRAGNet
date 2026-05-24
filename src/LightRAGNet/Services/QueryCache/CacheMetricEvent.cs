namespace LightRAGNet.Services.QueryCache;

public sealed record CacheMetricEvent(
    string Id,
    DateTimeOffset Timestamp,
    string Workspace,
    string CacheType,
    string Operation,
    string? Outcome,
    string? Mode,
    long DurationMs,
    long? FactoryDurationMs,
    string? CacheKeyPrefix,
    long? Revision)
{
    public static CacheMetricEvent CreateRead(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        long durationMs,
        long? factoryDurationMs,
        string? cacheKey,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Read,
            outcome,
            mode,
            Math.Max(0, durationMs),
            factoryDurationMs is null ? null : Math.Max(0, factoryDurationMs.Value),
            BuildKeyPrefix(cacheKey),
            revision);
    }

    public static CacheMetricEvent CreateSave(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        string? mode,
        long durationMs,
        string? cacheKey,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Save,
            null,
            mode,
            Math.Max(0, durationMs),
            null,
            BuildKeyPrefix(cacheKey),
            revision);
    }

    public static CacheMetricEvent CreateClear(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        long durationMs,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Clear,
            null,
            null,
            Math.Max(0, durationMs),
            null,
            null,
            revision);
    }

    private static string NormalizeWorkspace(string workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }

    private static string? BuildKeyPrefix(string? cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            return null;
        }

        return cacheKey.Length <= 16 ? cacheKey : cacheKey[..16];
    }
}
