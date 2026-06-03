using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class ReactDevCorsSourceTests
{
    [Fact]
    public void ServerCors_AllowsLocalStandaloneReactDevServerOriginsDynamically()
    {
        var source = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Server/Program.cs"));

        source.Should().Contain("SetIsOriginAllowed");
        source.Should().Contain("IsAllowedDevelopmentCorsOrigin");
        source.Should().Contain("builder.Environment.IsDevelopment()");
        source.Should().Contain("Uri.TryCreate");
        source.Should().Contain("\"localhost\"");
        source.Should().Contain("\"127.0.0.1\"");
        source.Should().Contain("IPAddress.IsLoopback");
        source.Should().NotContain("AllowAnyOrigin");
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

    [Fact]
    public void DevStart_DefaultPathWaitsForReadyAndPrintsUrls()
    {
        var source = File.ReadAllText(FindRepositoryFile("scripts/dev-start.ps1"));

        source.Should().Contain("[switch]$Background");
        source.Should().Contain("if ($Background -and -not $Worker)");
        source.Should().Contain("Write-Step \"Development services are ready.\"");
        source.Should().Contain("Write-Host \"  Server: $ServerUrl\"");
        source.Should().Contain("Write-Host \"    $ReactUrl/documents\"");
        source.Should().NotContain("if (-not $Foreground -and -not $Worker)");
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
