using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class SystemStatusHostSourceTests
{
    [Fact]
    public void SystemStatus_HostsReactSystemStatusWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "SystemStatus.razor");

        source.Should().Contain("@page \"/system-status\"");
        source.Should().Contain("system-status-root");
        source.Should().Contain("data-api-base=\"@ApiBase\"");
        source.Should().Contain("mountSystemStatus");
        source.Should().Contain("unmountSystemStatus");
        source.Should().Contain("./system-status/assets/system-status.js");
        source.Should().Contain("system-status/assets/system-status.css");
        source.Should().NotContain("<script type=\"module\"");
    }

    [Fact]
    public void NavMenu_ContainsSystemStatusEntry()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Layout", "NavMenu.razor");

        source.Should().Contain("Href=\"system-status\"");
        source.Should().Contain("System Status");
        source.Should().Contain("MonitorHeart");
    }

    [Fact]
    public void SystemStatus_BuildArtifactsAreCommitted()
    {
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "system-status", "assets", "system-status.js")
            .Should()
            .BeTrue();
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "system-status", "assets", "system-status.css")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SystemStatusReact_DoesNotPerformHealthAggregationLocally()
    {
        var source = ReadRepositoryFile(
            "src",
            "LightRAGNet.Web",
            "ClientApp",
            "src",
            "system-status",
            "SystemStatusWorkbench.tsx");

        source.Should().NotContain("fixFirst =");
        source.Should().NotContain("featureImpacts =");
        source.Should().NotContain("overallStatus");
        source.Should().Contain("health.status");
        source.Should().Contain("health.fixFirst");
        source.Should().Contain("health.featureImpacts");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]), System.Text.Encoding.UTF8);
    }

    private static bool RepositoryFileExists(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.Exists(Path.Combine([repositoryRoot, .. relativeParts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }
}
