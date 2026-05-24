using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class SystemHealthServiceTests
{
    [Fact]
    public async Task GetHealthAsync_WhenAnyCheckIsUnhealthy_ReturnsUnhealthyAndOrdersFixFirst()
    {
        var service = CreateService(
            FakeCheck.Healthy("server-api"),
            FakeCheck.Unhealthy("neo4j"),
            FakeCheck.Unhealthy("qdrant"));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.Unhealthy);
        response.FixFirst.Select(item => item.CheckId).Should().Equal("qdrant", "neo4j");
    }

    [Fact]
    public async Task GetHealthAsync_WhenOnlyDegradedChecksExist_ReturnsDegradedAndIncludesRerankConfig()
    {
        var service = CreateService(
            FakeCheck.Healthy("server-api"),
            FakeCheck.Degraded("rerank-config"));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.Degraded);
        response.FixFirst.Select(item => item.CheckId).Should().Equal("rerank-config");
    }

    [Fact]
    public async Task GetHealthAsync_WhenAllMeasuredChecksAreHealthy_ReturnsHealthyWithEmptyFixFirst()
    {
        var service = CreateService(
            FakeCheck.Healthy("server-api"),
            FakeCheck.Healthy("sqlite"),
            FakeCheck.Healthy("qdrant"));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.Healthy);
        response.FixFirst.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthAsync_WhenAllChecksAreNotMeasured_ReturnsNotMeasured()
    {
        var service = CreateService(
            FakeCheck.NotMeasured("llm-config"),
            FakeCheck.NotMeasured("embedding-config"));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.NotMeasured);
        response.Summary.NotMeasured.Should().Be(2);
    }

    [Fact]
    public async Task GetHealthAsync_WhenCheckThrows_CapturesFailureResultWithErrorTypeEvidence()
    {
        var service = CreateService(FakeCheck.Throwing("qdrant", new InvalidOperationException("vector store offline")));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.Unhealthy);
        response.Checks.Should().ContainSingle();
        response.Checks[0].Id.Should().Be("qdrant");
        response.Checks[0].Status.Should().Be(SystemHealthStatus.Unhealthy);
        response.Checks[0].Evidence.Should().ContainKey("errorType").WhoseValue.Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task GetHealthAsync_RedactsSensitiveEvidenceKeysAndPreservesUri()
    {
        var service = CreateService(FakeCheck.Unhealthy(
            "llm-config",
            new Dictionary<string, object?>
            {
                ["apiKey"] = "secret-api-key",
                ["password"] = "secret-password",
                ["token"] = "secret-token",
                ["authorizationHeader"] = "Bearer secret",
                ["connectionString"] = "Server=.;Password=secret;",
                ["uri"] = "https://example.test"
            }));

        var response = await service.GetHealthAsync();

        var evidence = response.Checks.Single().Evidence;
        evidence["apiKey"].Should().Be("<redacted>");
        evidence["password"].Should().Be("<redacted>");
        evidence["token"].Should().Be("<redacted>");
        evidence["authorizationHeader"].Should().Be("<redacted>");
        evidence["connectionString"].Should().Be("<redacted>");
        evidence["uri"].Should().Be("https://example.test");
    }

    [Fact]
    public async Task GetHealthAsync_BuildsFeatureImpactsFromAffectedChecks()
    {
        var service = CreateService(FakeCheck.Degraded(
            "neo4j",
            affects: ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"]));

        var response = await service.GetHealthAsync();

        response.FeatureImpacts.Should().ContainSingle(impact => impact.Feature == "KG Query Modes")
            .Which.Should().Match<SystemHealthFeatureImpact>(impact =>
                impact.Status == SystemHealthStatus.Degraded &&
                impact.AffectedBy.SequenceEqual(new[] { "neo4j" }));
    }

    private static SystemHealthService CreateService(params ISystemHealthCheck[] checks)
    {
        return new SystemHealthService(
            checks,
            Options.Create(new SystemHealthOptions { PerCheckTimeout = TimeSpan.FromSeconds(1) }),
            NullLogger<SystemHealthService>.Instance);
    }

    private sealed class FakeCheck : ISystemHealthCheck
    {
        private readonly Func<CancellationToken, Task<SystemHealthCheckResult>> checkAsync;

        private FakeCheck(
            string id,
            Func<CancellationToken, Task<SystemHealthCheckResult>> checkAsync,
            string? name = null,
            string category = "test")
        {
            Id = id;
            Name = name ?? id;
            Category = category;
            this.checkAsync = checkAsync;
        }

        public string Id { get; }

        public string Name { get; }

        public string Category { get; }

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            return checkAsync(cancellationToken);
        }

        public static FakeCheck Healthy(string id)
        {
            return Returning(id, SystemHealthCheckResult.Healthy(id, id, "test", "ok"));
        }

        public static FakeCheck Degraded(
            string id,
            IReadOnlyList<string>? affects = null,
            IReadOnlyDictionary<string, object?>? evidence = null)
        {
            return Returning(id, SystemHealthCheckResult.Degraded(id, id, "test", "degraded", "fix degraded", affects ?? [], evidence));
        }

        public static FakeCheck Unhealthy(string id, IReadOnlyDictionary<string, object?>? evidence = null)
        {
            return Returning(id, SystemHealthCheckResult.Unhealthy(id, id, "test", "unhealthy", "fix unhealthy", [], evidence));
        }

        public static FakeCheck NotMeasured(string id)
        {
            return Returning(id, SystemHealthCheckResult.NotMeasured(id, id, "test", "not measured"));
        }

        public static FakeCheck Throwing(string id, Exception exception)
        {
            return new FakeCheck(id, _ => throw exception);
        }

        private static FakeCheck Returning(string id, SystemHealthCheckResult result)
        {
            return new FakeCheck(id, _ => Task.FromResult(result));
        }
    }
}
