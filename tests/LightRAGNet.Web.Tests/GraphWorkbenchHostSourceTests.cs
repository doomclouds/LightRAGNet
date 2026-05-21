using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class GraphWorkbenchHostSourceTests
{
    [Fact]
    public void GraphView_HostsReactGraphWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "GraphView.razor");

        source.Should().Contain("graph-workbench-root");
        source.Should().Contain("IJSRuntime");
        source.Should().Contain("mountGraphWorkbench");
        source.Should().Contain("unmountGraphWorkbench");
        source.Should().Contain("graph-workbench/assets/graph-workbench.css");
        source.Should().NotContain("<script type=\"module\"");
    }

    [Fact]
    public void GraphWorkbench_BuildArtifactsAreCommitted()
    {
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "graph-workbench", "assets", "graph-workbench.js")
            .Should()
            .BeTrue();
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "graph-workbench", "assets", "graph-workbench.css")
            .Should()
            .BeTrue();
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]));
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
