using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LightRAGNet.Server.Tests;

public class ServerHostSmokeTests
{
    [Fact]
    public void ServerHost_CanBeCreated()
    {
        using var factory = new LightRagServerFactory();

        using var client = factory.CreateClient();

        client.Should().NotBeNull();
    }

    [Fact]
    public async Task ServerHost_WithDefaultTestFactory_CanRunSystemHealthWithoutExternalStores()
    {
        using var factory = new LightRagServerFactory();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SystemHealthService>();

        var response = await service.GetHealthAsync();

        response.Checks.Should().ContainSingle(check => check.Id == "test-system-health");
        response.Status.Should().Be(SystemHealthStatus.Healthy);
    }
}
