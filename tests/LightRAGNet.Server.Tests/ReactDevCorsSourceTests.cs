using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class ReactDevCorsSourceTests
{
    [Fact]
    public void ServerCors_AllowsStandaloneReactDevServer()
    {
        var source = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Server/Program.cs"));

        source.Should().Contain("\"http://localhost:5173\"");
        source.Should().Contain("\"http://127.0.0.1:5173\"");
    }

    [Fact]
    public void DevStart_ValidatesReactDevServerBeforeReuse()
    {
        var source = File.ReadAllText(FindRepositoryFile("scripts/dev-start.ps1"));

        source.Should().Contain("function Test-StandaloneReactDevServer");
        source.Should().Contain("$Url/src/app/navigation.ts");
        source.Should().Contain("RAG Chat");
        source.Should().Contain("/cache-management");
        source.Should().Contain("does not match the standalone LightRAGNet.React app");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
    }
}
