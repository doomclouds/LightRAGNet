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
}
