using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class GraphWorkbenchHostSourceTests
{
    [Fact]
    public void GraphView_HostsReactGraphWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "GraphView.razor");

        source.Should().Contain("graph-workbench-root");
        source.Should().Contain("IConfiguration");
        source.Should().Contain("ApiBaseUrl");
        source.Should().Contain("data-api-base=\"@ApiBase\"");
        source.Should().Contain("IJSRuntime");
        source.Should().Contain("mountGraphWorkbench");
        source.Should().Contain("unmountGraphWorkbench");
        source.Should().Contain("./graph-workbench/assets/graph-workbench.js");
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

    [Fact]
    public void GraphCanvas_UsesStableSigmaRendererClasses()
    {
        var source = ReadRepositoryFile(
            "src",
            "LightRAGNet.Web",
            "ClientApp",
            "src",
            "components",
            "graph",
            "GraphCanvas.tsx");

        source.Should().Contain("const CurvedNoArrowProgram = createEdgeCurveProgram();");
        source.Should().Contain("edgeProgramClasses: EdgeProgramClasses");
        source.Should().Contain("nodeProgramClasses: NodeProgramClasses");
        source.Should().NotContain("curvedNoArrow: createEdgeCurveProgram()");
    }

    [Fact]
    public void GraphCanvas_CameraFocusUsesSigmaDisplayCoordinates()
    {
        var source = ReadRepositoryFile(
            "src",
            "LightRAGNet.Web",
            "ClientApp",
            "src",
            "components",
            "graph",
            "GraphCanvas.tsx");

        source.Should().Contain("sigma.getNodeDisplayData(selectedNodeId)");
        source.Should().Contain("window.requestAnimationFrame");
        source.Should().Contain("sigma.getCamera().animate({ x: nodeDisplayData.x, y: nodeDisplayData.y }");
        source.Should().NotContain("graph.getNodeAttribute(selectedNodeId, \"x\")");
        source.Should().NotContain("graph.getNodeAttribute(selectedNodeId, \"y\")");
        source.Should().NotContain("ratio: 0.55");
    }

    [Fact]
    public void GraphSearchBox_ClosesResultsAfterSelectingNode()
    {
        var source = ReadRepositoryFile(
            "src",
            "LightRAGNet.Web",
            "ClientApp",
            "src",
            "components",
            "graph",
            "GraphSearchBox.tsx");

        source.Should().Contain("const [resultsOpen, setResultsOpen] = useState(false);");
        source.Should().Contain("resultsOpen && matches.length > 0");
        source.Should().Contain("setResultsOpen(false);");
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
