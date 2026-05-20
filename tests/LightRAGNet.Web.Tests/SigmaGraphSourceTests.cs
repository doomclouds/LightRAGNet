using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class SigmaGraphSourceTests
{
    [Fact]
    public void SigmaGraph_UsesHumanReadableCamelCaseJsonOptions()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "SigmaGraph.razor");

        source.Should().Contain("LightRAGJsonOptions.HumanReadableCamelCase");
        source.Should().NotContain("new JsonSerializerOptions");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]));
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
