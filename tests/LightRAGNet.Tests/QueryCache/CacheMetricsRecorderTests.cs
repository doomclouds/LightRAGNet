using FluentAssertions;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.QueryCache;

public sealed class CacheMetricsRecorderTests
{
    [Fact]
    public async Task RecordReadAsync_WhenStoreThrows_DoesNotThrow()
    {
        var recorder = new CacheMetricsRecorder(
            new ThrowingCacheMetricsStore(),
            NullLogger<CacheMetricsRecorder>.Instance);

        var act = async () => await recorder.RecordReadAsync(
            workspace: "_",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Hit,
            mode: "Mix",
            duration: TimeSpan.FromMilliseconds(4),
            factoryDuration: null,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: 12);

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingCacheMetricsStore : ICacheMetricsStore
    {
        public Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default)
        {
            throw new IOException("metrics write failed");
        }

        public Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CacheMetricEvent>>(Array.Empty<CacheMetricEvent>());
        }
    }
}
