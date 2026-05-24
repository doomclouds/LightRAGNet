using FluentAssertions;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.QueryCache;

public sealed class JsonCacheMetricsStoreTests
{
    [Fact]
    public async Task AppendAsync_PersistsEventsAndReadAsyncLoadsThem()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");
        var metric = CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "_",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Hit,
            mode: "Mix",
            durationMs: 4,
            factoryDurationMs: null,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: 12);

        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);

        await store.AppendAsync(metric);
        var reopenedStore = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);

        var events = await reopenedStore.ReadAsync(
            timestamp.AddMinutes(-1),
            timestamp.AddMinutes(1));

        events.Should().ContainSingle();
        events[0].Outcome.Should().Be(CacheReadOutcome.Hit);
    }

    [Fact]
    public async Task AppendAsync_AppliesMaxEventsRetention()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions { MaxEvents = 2 },
            NullLogger<JsonCacheMetricsStore>.Instance);
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        await store.AppendAsync(CreateRead(timestamp, "a"));
        await store.AppendAsync(CreateRead(timestamp.AddSeconds(1), "b"));
        await store.AppendAsync(CreateRead(timestamp.AddSeconds(2), "c"));

        var events = await store.ReadAsync(timestamp.AddMinutes(-1), timestamp.AddMinutes(1));

        events.Should().HaveCount(2);
        events.Select(metric => metric.CacheKeyPrefix).Should().Equal("b", "c");
    }

    private static CacheMetricEvent CreateRead(DateTimeOffset timestamp, string cacheKey)
    {
        return CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "_",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Hit,
            mode: "Mix",
            durationMs: 1,
            factoryDurationMs: null,
            cacheKey: cacheKey,
            revision: null);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"LightRAGNet-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
