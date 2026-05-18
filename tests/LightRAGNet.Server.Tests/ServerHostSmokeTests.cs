using FluentAssertions;
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
}
