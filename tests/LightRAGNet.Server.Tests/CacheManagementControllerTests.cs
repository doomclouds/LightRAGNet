using System.Net;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Services.QueryCache;
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
        root.GetProperty("families")
            .EnumerateArray()
            .Should()
            .Contain(family => family.GetProperty("name").GetString() == "query");
        rawJson.Contains("api_key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("authorization", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        rawJson.Contains("return_value", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private sealed class CacheMetricsSeederStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                using var scope = app.ApplicationServices.CreateScope();
                var metricsStore = scope.ServiceProvider.GetRequiredService<ICacheMetricsStore>();
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
