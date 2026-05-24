using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class SystemHealthControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task GetHealth_ReturnsSystemHealthPayload()
    {
        await using var factory = CreateFactory(
            new FakeSystemHealthCheck(SystemHealthCheckResult.Healthy(
                "server-api",
                "Server API",
                "Server",
                "Server API is available.",
                new Dictionary<string, object?>
                {
                    ["ready"] = true
                })));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadHealthResponseAsync(response);
        body.Should().NotBeNull();
        body!.Status.Should().Be(SystemHealthStatus.Healthy);
        body.GeneratedAt.Should().NotBe(default);
        body.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        body.Checks.Should().ContainSingle();
        body.Checks[0].Id.Should().Be("server-api");
        body.Checks[0].Status.Should().Be(SystemHealthStatus.Healthy);
        body.Checks[0].DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_RedactsSensitiveEvidence()
    {
        await using var factory = CreateFactory(
            new FakeSystemHealthCheck(SystemHealthCheckResult.Healthy(
                "llm-config",
                "LLM configuration",
                "Configuration",
                "LLM configuration is present.",
                new Dictionary<string, object?>
                {
                    ["apiKey"] = "sk-secret-value",
                    ["connectionString"] = "Server=localhost;Password=super-secret;",
                    ["publicSetting"] = "safe"
                })));
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/system/health");

        json.Should().Contain("<redacted>");
        json.Should().NotContain("sk-secret-value");
        json.Should().NotContain("super-secret");
        json.Should().Contain("safe");
    }

    [Fact]
    public async Task GetHealth_WhenCheckThrows_ReturnsUnhealthyWithFailedCheckId()
    {
        await using var factory = CreateFactory(new ThrowingSystemHealthCheck("sqlite"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadHealthResponseAsync(response);
        body.Should().NotBeNull();
        body!.Status.Should().Be(SystemHealthStatus.Unhealthy);
        body.Checks.Should().ContainSingle(check =>
            check.Id == "sqlite" &&
            check.Status == SystemHealthStatus.Unhealthy);
    }

    private static LightRagServerFactory CreateFactory(params ISystemHealthCheck[] checks)
    {
        return new LightRagServerFactory(services =>
        {
            services.RemoveAll<ISystemHealthCheck>();

            foreach (var check in checks)
            {
                services.AddSingleton(check);
            }
        });
    }

    private static async Task<SystemHealthResponse?> ReadHealthResponseAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<SystemHealthResponse>(JsonOptions);
    }

    private sealed class FakeSystemHealthCheck(SystemHealthCheckResult result) : ISystemHealthCheck
    {
        public string Id => result.Id;

        public string Name => result.Name;

        public string Category => result.Category;

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingSystemHealthCheck(string id) : ISystemHealthCheck
    {
        public string Id { get; } = id;

        public string Name => "Throwing health check";

        public string Category => "Test";

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Simulated health failure.");
        }
    }
}
