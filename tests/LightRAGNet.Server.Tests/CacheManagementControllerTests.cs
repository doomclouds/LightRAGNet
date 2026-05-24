using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Server.Services.CacheManagement;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class CacheManagementControllerTests
{
    [Fact]
    public async Task GetOverview_ReturnsSummaryAndQueryFamilyWithoutSensitiveFields()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.AddSingleton<IStartupFilter>(new CacheMetricsSeederStartupFilter());
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/cache-management/overview?workspace=_&window=24h");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        root.TryGetProperty("summary", out _).Should().BeTrue();
        root.TryGetProperty("entrySamples", out _).Should().BeTrue();
        root.TryGetProperty("clearPlan", out _).Should().BeTrue();
        root.GetProperty("families")
            .EnumerateArray()
            .Should()
            .Contain(family => family.GetProperty("cacheType").GetString() == "query");
        var trendPoint = root.GetProperty("trend").EnumerateArray().Should().ContainSingle().Subject;
        trendPoint.TryGetProperty("savedCalls", out _).Should().BeTrue();
        trendPoint.TryGetProperty("reads", out _).Should().BeFalse();
        trendPoint.TryGetProperty("hits", out _).Should().BeFalse();
        trendPoint.TryGetProperty("misses", out _).Should().BeFalse();
        rawJson.Contains("api_key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("authorization", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("provider payload", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("secret prompt", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("secret return", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("return_value", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task GetOverview_WhenUnmeasured_ReturnsInsightLevelWithoutSeverity()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var rawJson = await client.GetStringAsync("/api/cache-management/overview?workspace=_&window=24h");

        using var document = JsonDocument.Parse(rawJson);
        var insight = document.RootElement.GetProperty("insights").EnumerateArray().Should().ContainSingle().Subject;
        insight.TryGetProperty("level", out _).Should().BeTrue();
        insight.TryGetProperty("severity", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Clear_ReturnsOkStubResponseWithoutExecutingClear()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/cache-management/clear", new CacheClearRequest(
            Workspace: "_",
            PlanId: "all-llm-cache",
            Confirm: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CacheClearResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().BeFalse();
        body.DeletedEntries.Should().Be(0);
    }

    [Fact]
    public async Task Clear_WithUnknownPlan_ReturnsOkUnknownPlanBody()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/cache-management/clear", new CacheClearRequest(
            Workspace: "_",
            PlanId: "not-a-plan",
            Confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CacheClearResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().BeFalse();
        body.DeletedEntries.Should().Be(0);
        body.Message.Should().Contain("Unknown", Exactly.Once());
    }

    [Fact]
    public async Task Clear_WhenPlanRequiresConfirmationWithoutConfirm_ReturnsRequiresConfirmationBody()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/cache-management/clear", new CacheClearRequest(
            Workspace: "_",
            PlanId: "all-llm-cache",
            Confirm: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CacheClearResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().BeFalse();
        body.DeletedEntries.Should().Be(0);
        body.Message.Should().Contain("confirmation");
    }

    private sealed class CacheMetricsSeederStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                using var scope = app.ApplicationServices.CreateScope();
                var metricsStore = scope.ServiceProvider.GetRequiredService<ICacheMetricsStore>();
                var llmCacheStore = scope.ServiceProvider.GetRequiredKeyedService<IKVStore>(KVContracts.LLMCache);
                llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
                {
                    ["Mix:query:abcdef0123456789"] = new LightRagCacheEntry(
                        ReturnValue: "secret return provider payload",
                        CacheType: LightRagCacheKeyBuilder.QueryCacheType,
                        OriginalPrompt: "secret prompt api_key authorization",
                        QueryParam: new Dictionary<string, object?> { ["workspace_query_revision"] = 0 },
                        CreateTime: 1234).ToDictionary()
                }).GetAwaiter().GetResult();
                metricsStore.AppendAsync(CacheMetricEvent.CreateRead(
                    DateTimeOffset.UtcNow,
                    workspace: "_",
                    cacheType: LightRagCacheKeyBuilder.QueryCacheType,
                    outcome: CacheReadOutcome.Hit,
                    mode: "Mix",
                    durationMs: 2,
                    factoryDurationMs: null,
                    cacheKey: "Mix:query:abcdef0123456789",
                    revision: 0)).GetAwaiter().GetResult();

                next(app);
            };
        }
    }
}
