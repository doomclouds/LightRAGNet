using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.QueryCache;

public sealed class CacheMetricsRecorder(
    ICacheMetricsStore store,
    ILogger<CacheMetricsRecorder> logger) : ICacheMetricsRecorder
{
    public Task RecordReadAsync(
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        TimeSpan duration,
        TimeSpan? factoryDuration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateRead(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            outcome,
            mode,
            ToMilliseconds(duration),
            factoryDuration is null ? null : ToMilliseconds(factoryDuration.Value),
            cacheKey,
            revision);

        return AppendAsync(metric, cancellationToken);
    }

    public Task RecordSaveAsync(
        string workspace,
        string cacheType,
        string? mode,
        TimeSpan duration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateSave(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            mode,
            ToMilliseconds(duration),
            cacheKey,
            revision);

        return AppendAsync(metric, cancellationToken);
    }

    public Task RecordClearAsync(
        string workspace,
        string cacheType,
        TimeSpan duration,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateClear(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            ToMilliseconds(duration),
            revision);

        return AppendAsync(metric, cancellationToken);
    }

    private async Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendAsync(metric, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to record cache metric event {MetricId}", metric.Id);
        }
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Max(0, duration.TotalMilliseconds);
    }
}
