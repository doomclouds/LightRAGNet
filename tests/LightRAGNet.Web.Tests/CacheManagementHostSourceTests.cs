using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class CacheManagementHostSourceTests
{
    [Fact]
    public void CacheManagement_HostsReactCacheManagementWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "CacheManagement.razor");

        source.Should().Contain("@page \"/cache-management\"");
        source.Should().Contain("cache-management-root");
        source.Should().Contain("IConfiguration");
        source.Should().Contain("ApiBaseUrl");
        source.Should().Contain("data-api-base=\"@ApiBase\"");
        source.Should().Contain("IJSRuntime");
        source.Should().Contain("mountCacheManagement");
        source.Should().Contain("unmountCacheManagement");
        source.Should().Contain("./cache-management/assets/cache-management.js");
        source.Should().Contain("cache-management/assets/cache-management.css");
        source.Should().NotContain("<script type=\"module\"");
    }

    [Fact]
    public void CacheManagement_BuildArtifactsAreCommitted()
    {
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "cache-management", "assets", "cache-management.js")
            .Should()
            .BeTrue();
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "cache-management", "assets", "cache-management.css")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void NavMenu_LinksToCacheManagementWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Layout", "NavMenu.razor");

        source.Should().Contain("Href=\"cache-management\"");
        source.Should().Contain("Cache Management");
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
