using FluentAssertions;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Tests.QueryCache;

public sealed class CacheMetricEventTests
{
    [Fact]
    public void CacheMetricEvent_CreateReadEvent_KeepsSafeFieldsOnly()
    {
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

        metric.Operation.Should().Be(CacheMetricOperation.Read);
        metric.Outcome.Should().Be(CacheReadOutcome.Hit);
        metric.CacheKeyPrefix.Should().Be("Mix:query:abcdef");
        metric.Workspace.Should().Be("_");
        metric.CacheType.Should().Be("query");
        metric.Mode.Should().Be("Mix");
        metric.DurationMs.Should().Be(4);
        metric.FactoryDurationMs.Should().BeNull();
        metric.Revision.Should().Be(12);
    }

    [Fact]
    public void CacheMetricEvent_CreateReadEvent_NormalizesBoundaryFields()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        var metric = CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "  abc  ",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Miss,
            mode: "Mix",
            durationMs: -4,
            factoryDurationMs: -9,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: null);

        metric.Workspace.Should().Be("abc");
        metric.DurationMs.Should().Be(0);
        metric.FactoryDurationMs.Should().Be(0);
        metric.CacheKeyPrefix.Should().Be("Mix:query:abcdef");
    }

    [Fact]
    public void CacheMetricEvent_CreateReadEvent_UsesSafeDefaultsForBlankFields()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        var metric = CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "   ",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Disabled,
            mode: null,
            durationMs: 2,
            factoryDurationMs: null,
            cacheKey: "   ",
            revision: null);

        metric.Workspace.Should().Be("_");
        metric.CacheKeyPrefix.Should().BeNull();
    }

    [Fact]
    public void CacheMetricEvent_CreateSaveEvent_ReturnsSaveSemantics()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        var metric = CacheMetricEvent.CreateSave(
            timestamp,
            workspace: "  abc  ",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            mode: "Mix",
            durationMs: -1,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: 12);

        metric.Timestamp.Should().Be(timestamp);
        metric.Workspace.Should().Be("abc");
        metric.CacheType.Should().Be("query");
        metric.Operation.Should().Be(CacheMetricOperation.Save);
        metric.Outcome.Should().BeNull();
        metric.Mode.Should().Be("Mix");
        metric.DurationMs.Should().Be(0);
        metric.FactoryDurationMs.Should().BeNull();
        metric.CacheKeyPrefix.Should().Be("Mix:query:abcdef");
        metric.Revision.Should().Be(12);
    }

    [Fact]
    public void CacheMetricEvent_CreateClearEvent_ReturnsClearSemantics()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        var metric = CacheMetricEvent.CreateClear(
            timestamp,
            workspace: "   ",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            durationMs: -1,
            revision: 12);

        metric.Timestamp.Should().Be(timestamp);
        metric.Workspace.Should().Be("_");
        metric.CacheType.Should().Be("query");
        metric.Operation.Should().Be(CacheMetricOperation.Clear);
        metric.Outcome.Should().BeNull();
        metric.Mode.Should().BeNull();
        metric.DurationMs.Should().Be(0);
        metric.FactoryDurationMs.Should().BeNull();
        metric.CacheKeyPrefix.Should().BeNull();
        metric.Revision.Should().Be(12);
    }

    [Fact]
    public void CacheValueResult_Hit_ReturnsExpectedFlags()
    {
        var result = CacheValueResult<string>.FromHit(
            "cached",
            LightRagCacheKeyBuilder.QueryCacheType,
            "Mix:query:abcdef",
            TimeSpan.FromMilliseconds(3));

        result.Value.Should().Be("cached");
        result.CacheEnabled.Should().BeTrue();
        result.Hit.Should().BeTrue();
        result.Saved.Should().BeFalse();
        result.FactoryDuration.Should().BeNull();
    }

    [Theory]
    [InlineData(true, true, "Mix:query:abcdef")]
    [InlineData(false, false, null)]
    public void CacheValueResult_Miss_ReturnsExpectedFlags(bool cacheEnabled, bool saved, string? cacheKey)
    {
        var result = CacheValueResult<string>.FromMiss(
            "fresh",
            cacheEnabled,
            saved,
            cacheKey,
            LightRagCacheKeyBuilder.QueryCacheType,
            TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(9));

        result.Value.Should().Be("fresh");
        result.CacheEnabled.Should().Be(cacheEnabled);
        result.Hit.Should().BeFalse();
        result.Saved.Should().Be(saved);
        result.CacheKey.Should().Be(cacheKey);
        result.CacheType.Should().Be("query");
        result.CacheLookupDuration.Should().Be(TimeSpan.FromMilliseconds(3));
        result.FactoryDuration.Should().Be(TimeSpan.FromMilliseconds(9));
    }
}
