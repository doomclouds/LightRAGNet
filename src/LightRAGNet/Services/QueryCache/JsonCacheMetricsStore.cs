using System.Text.Json;
using LightRAGNet.Core.IO;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.QueryCache;

public sealed class JsonCacheMetricsStore(
    string filePath,
    CacheMetricsOptions options,
    ILogger<JsonCacheMetricsStore> logger) : ICacheMetricsStore
{
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public async Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (!options.Enabled)
        {
            return;
        }

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            var metrics = await LoadAllAsync(cancellationToken);
            metrics.Add(metric);

            metrics = ApplyRetention(metrics);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(metrics, LightRAGJsonOptions.HumanReadableIndented);
            await AtomicFileWriter.WriteAllTextAsync(filePath, json, cancellationToken: cancellationToken);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            var metrics = await LoadAllAsync(cancellationToken);

            return metrics
                .Where(metric => metric.Timestamp >= from && metric.Timestamp <= to)
                .OrderBy(metric => metric.Timestamp)
                .ToArray();
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task<List<CacheMetricEvent>> LoadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<CacheMetricEvent>>(json, LightRAGJsonOptions.HumanReadable)
                   ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read cache metrics from {FilePath}", filePath);
            return [];
        }
    }

    private List<CacheMetricEvent> ApplyRetention(List<CacheMetricEvent> metrics)
    {
        if (options.RetentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
            metrics = metrics
                .Where(metric => metric.Timestamp >= cutoff)
                .ToList();
        }

        if (options.MaxEvents < 1)
        {
            return [];
        }

        if (metrics.Count <= options.MaxEvents)
        {
            return metrics
                .OrderBy(metric => metric.Timestamp)
                .ToList();
        }

        return metrics
            .OrderBy(metric => metric.Timestamp)
            .TakeLast(options.MaxEvents)
            .ToList();
    }
}
