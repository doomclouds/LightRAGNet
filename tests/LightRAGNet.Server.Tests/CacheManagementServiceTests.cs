using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Server.Services.CacheManagement;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class CacheManagementServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_ComputesHitRateFromReadMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var metricsStore = new InMemoryCacheMetricsStore(
        [
            CreateRead(now.AddMinutes(-3), CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-2), CacheReadOutcome.Miss, 120),
            CreateRead(now.AddMinutes(-1), CacheReadOutcome.Hit, null)
        ]);
        var cacheStore = new InspectableKvStore();
        cacheStore.Seed("Mix:query:abcdef0123456789", CreateCacheEntry(LightRagCacheKeyBuilder.QueryCacheType));
        var service = CreateService(metricsStore, cacheStore);

        var overview = await service.GetOverviewAsync("_", "24h");

        overview.Workspace.Should().Be("_");
        overview.Window.Should().Be("24h");
        overview.Summary.Measured.Should().BeTrue();
        overview.Summary.OverallHitRate.Should().BeApproximately(2d / 3d, 0.0001d);
        overview.Summary.ProviderCallsAvoided.Should().Be(2);
        overview.Summary.EstimatedLatencySavedMs.Should().Be(240);
        overview.Summary.StaleOrRiskyEntries.Should().Be(0);
        var queryFamily = overview.Families.Should().ContainSingle(family => family.CacheType == "query").Subject;
        queryFamily.Hits.Should().Be(2);
        queryFamily.Attempts.Should().Be(3);
        queryFamily.HitRate.Should().BeApproximately(2d / 3d, 0.0001d);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutMetrics_ReturnsNotMeasuredHitRate()
    {
        var service = CreateService(new InMemoryCacheMetricsStore([]), new InspectableKvStore());

        var overview = await service.GetOverviewAsync("_", "24h");

        overview.Summary.Measured.Should().BeFalse();
        overview.Summary.OverallHitRate.Should().BeNull();
        overview.Families.Select(family => family.CacheType).Should().Equal("query", "keywords", "extract", "summary");
        overview.Families.Should().OnlyContain(family => family.HitRate == null);
    }

    [Fact]
    public async Task GetOverviewAsync_UsesInspectableStoreSnapshotForEntrySamplesAndClearPlan()
    {
        var cacheStore = new InspectableKvStore();
        cacheStore.Seed("default:summary:0123456789abcdef", CreateCacheEntry(LightRagCacheKeyBuilder.SummaryCacheType));
        var service = CreateService(new InMemoryCacheMetricsStore([]), cacheStore);

        var overview = await service.GetOverviewAsync("_", "24h");

        overview.EntrySamples.Should().ContainSingle(sample =>
            sample.CacheType == "summary"
            && sample.CacheKeyPrefix == "default:summary:"
            && sample.State == "current");
        var summaryPlan = overview.ClearPlan.Should().ContainSingle(plan => plan.Id == "unused-summary-cache").Subject;
        summaryPlan.CacheTypes.Should().Equal(LightRagCacheKeyBuilder.SummaryCacheType);
        summaryPlan.EntryCount.Should().Be(1);
        summaryPlan.RequiresConfirmation.Should().BeTrue();
    }

    private static CacheManagementService CreateService(
        ICacheMetricsStore metricsStore,
        IKVStore cacheStore)
    {
        return new CacheManagementService(
            metricsStore,
            new CacheEntryInspector(cacheStore),
            new CacheClearPlanner(),
            NullLogger<CacheManagementService>.Instance);
    }

    private static CacheMetricEvent CreateRead(
        DateTimeOffset timestamp,
        string outcome,
        long? factoryDurationMs)
    {
        return CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "_",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: outcome,
            mode: "Mix",
            durationMs: 3,
            factoryDurationMs: factoryDurationMs,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: 0);
    }

    private static Dictionary<string, object> CreateCacheEntry(string cacheType)
    {
        return new LightRagCacheEntry(
            ReturnValue: "secret return value",
            CacheType: cacheType,
            OriginalPrompt: "prompt with api_key and authorization",
            QueryParam: new Dictionary<string, object?> { ["workspace_query_revision"] = 0 },
            CreateTime: 1234,
            ChunkId: null).ToDictionary();
    }

    private sealed class InMemoryCacheMetricsStore(IReadOnlyList<CacheMetricEvent> seed) : ICacheMetricsStore
    {
        private readonly List<CacheMetricEvent> metrics = seed.ToList();

        public Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.Add(metric);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CacheMetricEvent>>(
                metrics
                    .Where(metric => metric.Timestamp >= from && metric.Timestamp <= to)
                    .ToList());
        }
    }

    private sealed class InspectableKvStore : IKVStore, IInspectableKVStore
    {
        private readonly Dictionary<string, Dictionary<string, object>> items = [];

        public void Seed(string id, Dictionary<string, object> value)
        {
            items[id] = value.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        public Task<IReadOnlyDictionary<string, Dictionary<string, object>>> SnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, Dictionary<string, object>>>(
                items.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal));
        }

        public Task<Dictionary<string, object>?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<Dictionary<string, object>>> GetByIdsAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<HashSet<string>> FilterKeysAsync(HashSet<string> keys, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpsertAsync(
            Dictionary<string, Dictionary<string, object>> data,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DropAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
