using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryControllerSourceTests
{
    [Fact]
    public void QueryAsync_PreservesCancellationAndNullBodyGuards()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "RagQueryController.cs");

        source.Should().Contain("[FromBody] RagQueryRequest? request");
        source.Should().Contain("request is null || string.IsNullOrWhiteSpace(request.Query)");

        var cancellationCatchIndex = source.IndexOf(
            "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)",
            StringComparison.Ordinal);
        var broadCatchIndex = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);

        cancellationCatchIndex.Should().BeGreaterThanOrEqualTo(0);
        broadCatchIndex.Should().BeGreaterThan(cancellationCatchIndex);
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
