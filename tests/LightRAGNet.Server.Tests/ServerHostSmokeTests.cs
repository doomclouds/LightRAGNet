using FluentAssertions;
using Xunit;

namespace LightRAGNet.Server.Tests;

public class ServerHostSmokeTests
{
    [Fact]
    public void TestProject_Loads()
    {
        typeof(Program).Assembly.GetName().Name.Should().Be("LightRAGNet.Server");
    }
}
