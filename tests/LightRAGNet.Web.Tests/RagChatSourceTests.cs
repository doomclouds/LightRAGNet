using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class RagChatSourceTests
{
    [Fact]
    public void RagChat_ProvidesQuerySettingsControls()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("rag-chat-layout");
        source.Should().Contain("rag-chat-main");
        source.Should().Contain("rag-chat-toolbar");
        source.Should().Contain("rag-chat-send-action");
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
    public void RagChat_UsesScopedHiddenScrollAreas()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor.css");
        var appCss = ReadRepositoryFile("src", "LightRAGNet.Web", "wwwroot", "app.css");

        source.Should().Contain(".rag-chat-shell");
        source.Should().Contain("height: calc(100dvh - 130px);");
        source.Should().Contain(".rag-chat-scroll");
        source.Should().Contain(".rag-chat-toolbar");
        source.Should().Contain("scrollbar-color: transparent transparent;");
        source.Should().Contain("::-webkit-scrollbar-thumb");
        source.Should().Contain("overflow: hidden;");
        source.Should().Contain("overflow-y: auto;");
        appCss.Should().Contain("html, body");
        appCss.Should().Contain("scrollbar-color: transparent transparent;");
        appCss.Should().Contain("html::-webkit-scrollbar-thumb");
    }

    [Fact]
    public void RagChat_QueryToolbarControlsHaveTooltips()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("Mode decides which retrieval pipeline is used.");
        source.Should().Contain("Streaming shows the answer as it is generated.");
        source.Should().Contain("References attaches source document links to the answer.");
        source.Should().Contain("TopK controls how many graph items are considered.");
        source.Should().Contain("ChunkTopK controls how many text chunks are retrieved.");
        source.Should().Contain("Rerank asks the reranker to reorder retrieved chunks.");
        source.Should().Contain("High keywords guide graph-level retrieval.");
        source.Should().Contain("Low keywords guide entity and detail retrieval.");
        source.Should().Contain("Output switches between the final answer and debug payloads.");
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

    [Fact]
    public void RagQueryDataDialog_RendersGroupedRetrievalDataSections()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagQueryDataDialog.razor");

        source.Should().Contain("@using System.Text.Json");
        source.Should().Contain("[CascadingParameter] IMudDialogInstance MudDialog");
        source.Should().Contain("[Parameter] public RagQueryDataResponse? RetrievalData { get; set; }");
        source.Should().Contain("Entities");
        source.Should().Contain("Relationships");
        source.Should().Contain("Chunks");
        source.Should().Contain("References");
        source.Should().Contain("Metadata");
        source.Should().Contain("Raw JSON");
        source.Should().Contain("SerializeSection");
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
