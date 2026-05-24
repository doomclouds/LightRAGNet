using FluentAssertions;

namespace LightRAGNet.Tests.Query;

public sealed class RagPromptReferenceContractTests
{
    [Fact]
    public void LightRagPrompt_DoesNotRequireModelGeneratedReferencesSection()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet", "LightRAG.cs");

        source.Should().NotContain("### References");
        source.Should().NotContain("References Section Format");
        source.Should().NotContain("Reference list entries should adhere to the format");
        source.Should().NotContain("Do not generate anything after the reference section");
        source.Should().Contain("DO NOT invent");
        source.Should().Contain("Source references are rendered separately by the system UI from structured metadata.");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
