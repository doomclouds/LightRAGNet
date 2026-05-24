using System.Text;
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
        var timestamp = DateTimeOffset.UtcNow;
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
        var timestamp = DateTimeOffset.UtcNow;

        await store.AppendAsync(CreateRead(timestamp, "a"));
        await store.AppendAsync(CreateRead(timestamp.AddSeconds(1), "b"));
        await store.AppendAsync(CreateRead(timestamp.AddSeconds(2), "c"));

        var events = await store.ReadAsync(timestamp.AddMinutes(-1), timestamp.AddMinutes(1));

        events.Should().HaveCount(2);
        events.Select(metric => metric.CacheKeyPrefix).Should().Equal("b", "c");
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsEmpty()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);

        var events = await store.ReadAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_EmptyFile_ReturnsEmpty()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        await File.WriteAllTextAsync(filePath, string.Empty, Encoding.UTF8);
        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);

        var events = await store.ReadAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_CorruptFile_ReturnsEmpty()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        await File.WriteAllTextAsync(filePath, "{not-json", Encoding.UTF8);
        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);

        var events = await store.ReadAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "cache_metrics.json");
        var store = new JsonCacheMetricsStore(
            filePath,
            new CacheMetricsOptions(),
            NullLogger<JsonCacheMetricsStore>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var act = async () => await store.AppendAsync(
            CreateRead(DateTimeOffset.UtcNow, "cancel"),
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
