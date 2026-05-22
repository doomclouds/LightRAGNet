using System.Text;
using FluentAssertions;

namespace LightRAGNet.Tests.Storage;

public sealed class Neo4jGraphStoreSourceTests
{
    [Fact]
    public void GetPopularLabelsAsync_FiltersUnwoundLabelsAfterWithClause()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Storage", "Neo4jGraphStore.cs");

        var normalizedSource = source.Replace("\r\n", "\n");

        normalizedSource.Should().Contain(
            """
            UNWIND labels(n) as label
                                 WITH label
                                 WHERE label <>
            """);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]), Encoding.UTF8);
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
