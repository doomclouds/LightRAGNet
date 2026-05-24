namespace LightRAGNet.Services.QueryCache;

public interface ICacheMetricsRecorder
{
    Task RecordReadAsync(
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        TimeSpan duration,
        TimeSpan? factoryDuration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default);

    Task RecordSaveAsync(
        string workspace,
        string cacheType,
        string? mode,
        TimeSpan duration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default);

    Task RecordClearAsync(
        string workspace,
        string cacheType,
        TimeSpan duration,
        long? revision,
        CancellationToken cancellationToken = default);
}
