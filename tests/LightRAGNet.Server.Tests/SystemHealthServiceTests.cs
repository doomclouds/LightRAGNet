using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
    public async Task GetHealthAsync_RedactsNestedStringDictionariesAndSensitiveStringsInCollections()
    {
        var service = CreateService(FakeCheck.Unhealthy(
            "llm-config",
            new Dictionary<string, object?>
            {
                ["nested"] = new Dictionary<string, string>
                {
                    ["password"] = "nested-password-secret",
                    ["apiKey"] = "nested-api-key-secret",
                    ["uri"] = "https://example.test"
                },
                ["messages"] = new[]
                {
                    "Authorization: Bearer bearer-token-secret",
                    "password=inline-password-secret",
                    "{\"apiKey\":\"json-api-key-secret\",\"token\":\"json-token-secret\",\"connectionString\":\"json-connection-secret\"}",
                    "neo4j://user:uri-password-secret@localhost:7687"
                }
            }));

        var response = await service.GetHealthAsync();

        var evidenceJson = JsonSerializer.Serialize(response.Checks.Single().Evidence);
        evidenceJson.Should().NotContain("nested-password-secret");
        evidenceJson.Should().NotContain("nested-api-key-secret");
        evidenceJson.Should().NotContain("bearer-token-secret");
        evidenceJson.Should().NotContain("inline-password-secret");
        evidenceJson.Should().NotContain("json-api-key-secret");
        evidenceJson.Should().NotContain("json-token-secret");
        evidenceJson.Should().NotContain("json-connection-secret");
        evidenceJson.Should().NotContain("uri-password-secret");
        evidenceJson.Should().Contain("https://example.test");
    }

    [Fact]
    public async Task GetHealthAsync_RedactsSensitiveValuesFromExceptionEvidence()
    {
        var service = CreateService(FakeCheck.Throwing(
            "qdrant",
            new InvalidOperationException("payload {\"apiKey\":\"exception-api-secret\"}; Authorization: Bearer exception-bearer-secret; password=exception-password-secret")));

        var response = await service.GetHealthAsync();

        var evidenceJson = JsonSerializer.Serialize(response.Checks.Single().Evidence);
        evidenceJson.Should().NotContain("exception-api-secret");
        evidenceJson.Should().NotContain("exception-bearer-secret");
        evidenceJson.Should().NotContain("exception-password-secret");
        response.Checks.Single().Evidence["errorMessage"].Should().BeOfType<string>()
            .Which.Should().Contain("<redacted>");
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

    [Fact]
    public async Task GetHealthAsync_WhenCheckTimesOut_CancelsCheckTokenAndIncludesTimeoutMs()
    {
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(TimeSpan.FromMilliseconds(150), FakeCheck.ObservesCancellation("qdrant", cancellationObserved));

        var response = await service.GetHealthAsync();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var check = response.Checks.Single();
        check.Status.Should().Be(SystemHealthStatus.Unhealthy);
        check.Evidence.Should().ContainKey("errorType").WhoseValue.Should().Be(nameof(TimeoutException));
        check.Evidence.Should().ContainKey("timeoutMs").WhoseValue.Should().Be(150L);
    }

    [Fact]
    public async Task GetHealthAsync_WhenPerCheckTimeoutIsNotPositive_FallsBackToDefaultAndAllowsQuickCheckToPass()
    {
        var service = CreateService(TimeSpan.Zero, FakeCheck.Healthy("server-api"));

        var response = await service.GetHealthAsync();

        response.Status.Should().Be(SystemHealthStatus.Healthy);
        response.Checks.Single().Status.Should().Be(SystemHealthStatus.Healthy);
    }

    [Fact]
    public async Task GetHealthAsync_ForNeo4jFeatureImpact_UsesOpenGraphLink()
    {
        var service = CreateService(FakeCheck.Degraded(
            "neo4j",
            affects: ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"]));

        var response = await service.GetHealthAsync();

        var impact = response.FeatureImpacts.Single(impact => impact.Feature == "KG Query Modes");
        impact.Links.Should().ContainSingle()
            .Which.Should().Be(new SystemHealthLink("Open Graph", "/graph-view"));
    }

    [Fact]
    public async Task GetHealthAsync_WhenUnknownFailedCheckHasAffects_BuildsGenericFeatureImpact()
    {
        var service = CreateService(FakeCheck.Degraded(
            "custom-provider",
            affects: ["Custom Search", "Custom Indexing"]));

        var response = await service.GetHealthAsync();

        response.FeatureImpacts.Should().ContainSingle(impact => impact.Feature == "Custom Search, Custom Indexing")
            .Which.Should().Match<SystemHealthFeatureImpact>(impact =>
                impact.Status == SystemHealthStatus.Degraded &&
                impact.Reason == "degraded" &&
                impact.AffectedBy.SequenceEqual(new[] { "custom-provider" }) &&
                impact.Links.Count == 0);
    }

    [Fact]
    public async Task GetHealthAsync_WhenUnknownFailedCheckHasNoAffects_DoesNotBuildGenericFeatureImpact()
    {
        var service = CreateService(FakeCheck.Degraded("custom-provider"));

        var response = await service.GetHealthAsync();

        response.FeatureImpacts.Should().BeEmpty();
    }

    private static SystemHealthService CreateService(params ISystemHealthCheck[] checks)
    {
        return CreateService(TimeSpan.FromSeconds(1), checks);
    }

    private static SystemHealthService CreateService(TimeSpan perCheckTimeout, params ISystemHealthCheck[] checks)
    {
        return new SystemHealthService(
            checks,
            Options.Create(new SystemHealthOptions { PerCheckTimeout = perCheckTimeout }),
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

        public static FakeCheck ObservesCancellation(string id, TaskCompletionSource<bool> cancellationObserved)
        {
            return new FakeCheck(id, async cancellationToken =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult(true);
                    throw;
                }

                return SystemHealthCheckResult.Healthy(id, id, "test", "unexpected");
            });
        }

        private static FakeCheck Returning(string id, SystemHealthCheckResult result)
        {
            return new FakeCheck(id, _ => Task.FromResult(result));
        }
    }
}
