namespace LightRAGNet.Services.QueryCache;

public interface ICacheMetricsStore
{
    Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
