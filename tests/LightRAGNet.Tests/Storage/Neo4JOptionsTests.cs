using FluentAssertions;
using LightRAGNet.Storage;

namespace LightRAGNet.Tests.Storage;

public sealed class Neo4JOptionsTests
{
    [Fact]
    public void GetEffectivePassword_ConfiguredPasswordWins()
    {
        var options = new Neo4JOptions { Password = "configured" };

        options.GetEffectivePassword().Should().Be("configured");
    }

    [Fact]
    public void GetEffectivePassword_EmptyPasswordFallsBackToEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("NEO4J_PASSWORD");
        Environment.SetEnvironmentVariable("NEO4J_PASSWORD", "from-env");

        try
        {
            var options = new Neo4JOptions { Password = "" };

            options.GetEffectivePassword().Should().Be("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEO4J_PASSWORD", previous);
        }
    }
}
