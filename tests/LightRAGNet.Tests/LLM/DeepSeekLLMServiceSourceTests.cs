using FluentAssertions;

namespace LightRAGNet.Tests.LLM;

public sealed class DeepSeekLLMServiceSourceTests
{
    [Fact]
    public void PromptJsonSerialization_UsesHumanReadableJsonOptions()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.LLM", "DeepSeekLLMService.cs");

        source.Should().Contain("JsonSerializer.Serialize(entityTypes, LightRAGJsonOptions.HumanReadable)");
        source.Should().Contain("JsonSerializer.Serialize(new { Description = d }, LightRAGJsonOptions.HumanReadable)");
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
