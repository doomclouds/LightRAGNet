using FluentAssertions;
using LightRAGNet;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Server.Services.CacheManagement;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class CacheManagementServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_ComputesHitRateFromReadMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var metricsStore = new InMemoryCacheMetricsStore(
        [
            CreateRead(now.AddMinutes(-3), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-2), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Miss, 120),
            CreateRead(now.AddMinutes(-1), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Hit, null)
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
        var trendPoint = overview.Trend.Should().ContainSingle().Subject;
        trendPoint.HitRate.Should().BeApproximately(2d / 3d, 0.0001d);
        trendPoint.SavedCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_EstimatesOverallLatencyFromFamilyEstimates()
    {
        var now = DateTimeOffset.UtcNow;
        var metricsStore = new InMemoryCacheMetricsStore(
        [
            CreateRead(now.AddMinutes(-5), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-4), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-3), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Miss, 100),
            CreateRead(now.AddMinutes(-2), LightRagCacheKeyBuilder.ExtractCacheType, CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-1), LightRagCacheKeyBuilder.ExtractCacheType, CacheReadOutcome.Miss, 800)
        ]);
        var service = CreateService(metricsStore, new InspectableKvStore());

        var overview = await service.GetOverviewAsync("_", "24h");

        var queryFamily = overview.Families.Should().ContainSingle(family => family.CacheType == "query").Subject;
        var extractFamily = overview.Families.Should().ContainSingle(family => family.CacheType == "extract").Subject;
        queryFamily.EstimatedLatencySavedMs.Should().Be(200);
        extractFamily.EstimatedLatencySavedMs.Should().Be(800);
        overview.Summary.EstimatedLatencySavedMs.Should().Be(1000);
    }

    [Fact]
    public async Task GetOverviewAsync_IgnoresNonHitMissReadOutcomesAndSaveMetricsForAttempts()
    {
        var now = DateTimeOffset.UtcNow;
        var metricsStore = new InMemoryCacheMetricsStore(
        [
            CreateRead(now.AddMinutes(-6), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Hit, null),
            CreateRead(now.AddMinutes(-5), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Miss, 100),
            CreateRead(now.AddMinutes(-4), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Invalid, null),
            CreateRead(now.AddMinutes(-3), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Disabled, null),
            CreateRead(now.AddMinutes(-2), LightRagCacheKeyBuilder.QueryCacheType, CacheReadOutcome.Error, null),
            CacheMetricEvent.CreateSave(
                now.AddMinutes(-1),
                workspace: "_",
                cacheType: LightRagCacheKeyBuilder.QueryCacheType,
                mode: "Mix",
                durationMs: 9,
                cacheKey: "Mix:query:saved",
                revision: 0)
        ]);
        var service = CreateService(metricsStore, new InspectableKvStore());

        var overview = await service.GetOverviewAsync("_", "24h");

        overview.Summary.Measured.Should().BeTrue();
        overview.Summary.OverallHitRate.Should().BeApproximately(0.5d, 0.0001d);
        overview.Summary.ProviderCallsAvoided.Should().Be(1);
        var queryFamily = overview.Families.Should().ContainSingle(family => family.CacheType == "query").Subject;
        queryFamily.Attempts.Should().Be(2);
        queryFamily.Hits.Should().Be(1);
        queryFamily.Misses.Should().Be(1);
        var trendPoint = overview.Trend.Should().ContainSingle().Subject;
        trendPoint.HitRate.Should().BeApproximately(0.5d, 0.0001d);
        trendPoint.SavedCalls.Should().Be(1);
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
        overview.Insights.Should().ContainSingle().Which.Level.Should().Be("info");
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
        var summaryPlan = overview.ClearPlan.Should().ContainSingle(plan => plan.Id == "summary-cache-review").Subject;
        overview.ClearPlan.Should().NotContain(plan => plan.Id == "unused-summary-cache");
        summaryPlan.CacheTypes.Should().Equal(LightRagCacheKeyBuilder.SummaryCacheType);
        summaryPlan.EntryCount.Should().Be(1);
        summaryPlan.RequiresConfirmation.Should().BeTrue();
        summaryPlan.Title.Should().Contain("Review");
        summaryPlan.Impact.Should().Contain("review", Exactly.Once());
    }

    [Fact]
    public async Task GetOverviewAsync_UsesCurrentWorkspaceRevisionForRequestedWorkspaceQueryInventoryState()
    {
        var cacheStore = new InspectableKvStore();
        cacheStore.Seed("metadata:query_revision:workspace-a", new Dictionary<string, object>
        {
            ["revision"] = 3
        });
        cacheStore.Seed(
            "Mix:query:workspace-a-old",
            CreateCacheEntry(LightRagCacheKeyBuilder.QueryCacheType, workspace: "workspace-a", workspaceQueryRevision: 2));
        cacheStore.Seed(
            "Mix:query:workspace-a-current",
            CreateCacheEntry(LightRagCacheKeyBuilder.QueryCacheType, workspace: "workspace-a", workspaceQueryRevision: 3));
        cacheStore.Seed(
            "Mix:query:workspace-b-old",
            CreateCacheEntry(LightRagCacheKeyBuilder.QueryCacheType, workspace: "workspace-b", workspaceQueryRevision: 2));
        cacheStore.Seed(
            "Mix:query:unknown-workspace",
            CreateCacheEntry(LightRagCacheKeyBuilder.QueryCacheType, workspace: null, workspaceQueryRevision: 2));
        var service = CreateService(new InMemoryCacheMetricsStore([]), cacheStore);

        var overview = await service.GetOverviewAsync("workspace-a", "24h");

        overview.EntrySamples.Should().ContainSingle(sample => sample.State == "old revision");
        overview.EntrySamples.Should().ContainSingle(sample => sample.State == "current");
        overview.EntrySamples.Should().ContainSingle(sample => sample.State == "other workspace");
        overview.EntrySamples.Should().ContainSingle(sample => sample.State == "unknown revision");
        var stalePlan = overview.ClearPlan.Should().ContainSingle(plan => plan.Id == "stale-query-cache").Subject;
        stalePlan.EntryCount.Should().Be(1);
    }

    private static CacheManagementService CreateService(
        ICacheMetricsStore metricsStore,
        IKVStore cacheStore)
    {
        return new CacheManagementService(
            metricsStore,
            new CacheEntryInspector(cacheStore),
            new LightRagLlmCacheService(
                cacheStore,
                Options.Create(new LightRAGOptions()),
                new LightRagCacheKeyBuilder(),
                NullLogger<LightRagLlmCacheService>.Instance),
            new CacheClearPlanner(),
            NullLogger<CacheManagementService>.Instance);
    }

    private static CacheMetricEvent CreateRead(
        DateTimeOffset timestamp,
        string cacheType,
        string outcome,
        long? factoryDurationMs)
    {
        return CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "_",
            cacheType: cacheType,
            outcome: outcome,
            mode: "Mix",
            durationMs: 3,
            factoryDurationMs: factoryDurationMs,
            cacheKey: $"Mix:{cacheType}:abcdef0123456789",
            revision: 0);
    }

    private static Dictionary<string, object> CreateCacheEntry(
        string cacheType,
        string? workspace = "_",
        long workspaceQueryRevision = 0)
    {
        return new LightRagCacheEntry(
            ReturnValue: "secret return value",
            CacheType: cacheType,
            OriginalPrompt: "prompt with api_key and authorization",
            QueryParam: new Dictionary<string, object?>
            {
                ["workspace"] = workspace,
                ["workspace_query_revision"] = workspaceQueryRevision
            },
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

        public Task<IReadOnlyList<InspectableKVStoreEntry>> SnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<InspectableKVStoreEntry>>(
                items
                    .Select(pair => InspectableKVStoreEntry.FromRaw(pair.Key, pair.Value))
                    .Where(entry => entry is not null)
                    .Select(entry => entry!)
                    .ToList());
        }

        public Task<Dictionary<string, object>?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(items.TryGetValue(id, out var item)
                ? item.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : null);
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
