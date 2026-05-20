using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class RagChatSourceTests
{
    [Fact]
    public void RagChat_ProvidesQuerySettingsControls()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("_querySettings.SelectedMode");
        source.Should().Contain("_querySettings.StreamResponse");
        source.Should().Contain("_querySettings.EffectiveIncludeReferences");
        source.Should().Contain("_querySettings.BuildRequest(userMessage)");
        source.Should().Contain("MudSelect T=\"ChatQueryDebugOutputMode\"");
        source.Should().NotContain("_onlyNeedContext");
        source.Should().NotContain("_onlyNeedPrompt");
        source.Should().Contain("new RagQueryStreamHandlers");
        source.Should().NotContain("ApiClient.QueryRagAsync(\r\n                    userMessage");
        source.Should().NotContain("ApiClient.QueryRagAsync(\n                    userMessage");
    }

    [Fact]
    public void RagChat_RendersAssistantMetadataAndErrors()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("ShouldRenderReferences(chatMessage)");
        source.Should().Contain("ShouldRenderDiagnostics(chatMessage)");
        source.Should().Contain("chatMessage.References.Count");
        source.Should().Contain("chatMessage.Diagnostics");
        source.Should().Contain("chatMessage.ErrorMessage");
        source.Should().Contain("chatMessage.IsComplete");
        source.Should().Contain("No content returned.");
        source.Should().Contain("assistantMessage.IsComplete = true;");
        source.Should().Contain("ChatQuerySettingsModel.ApplyMetadata(assistantMessage, metadataEvent)");
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
