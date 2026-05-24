using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class RagChatSourceTests
{
    [Fact]
    public void RagChat_HostsReactRagChatWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("@page \"/\"");
        source.Should().Contain("@using Microsoft.JSInterop");
        source.Should().Contain("@implements IAsyncDisposable");
        source.Should().Contain("@inject IConfiguration Configuration");
        source.Should().Contain("@inject IJSRuntime JSRuntime");
        source.Should().Contain("rag-chat-root");
        source.Should().Contain("data-api-base=\"@ApiBase\"");
        source.Should().Contain("RootElementId = \"rag-chat-root\"");
        source.Should().Contain("ApiBase => Configuration[\"ApiBaseUrl\"] ?? \"http://localhost:5261\"");
        source.Should().Contain("InvokeVoidAsync(\"mountRagChat\", RootElementId, ApiBase)");
        source.Should().Contain("InvokeVoidAsync(\"unmountRagChat\", RootElementId)");
        source.Should().Contain("./rag-chat/assets/rag-chat.js");
        source.Should().Contain("rag-chat/assets/rag-chat.css");
        source.Should().Contain("JSDisconnectedException");
        source.Should().Contain("ObjectDisposedException");
        source.Should().NotContain("<script type=\"module\"");
        source.Should().NotContain("<MudContainer");
        source.Should().NotContain("@inject ApiClient ApiClient");
    }

    [Fact]
    public void RagChat_GuardsJsImportAgainstDisposeRace()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

        source.Should().Contain("private bool _disposed;");
        source.Should().Contain("_disposed = true;");
        source.Should().Contain("var importedModule = await JSRuntime.InvokeAsync<IJSObjectReference>");
        source.Should().Contain("if (_disposed)");
        source.Should().Contain("await DisposeImportedModuleAsync(importedModule);");
        source.Should().Contain("await module.DisposeAsync();");
        source.Should().Contain("ragChatModule = importedModule;");
        source.Should().Contain("InvokeVoidAsync(\"mountRagChat\", RootElementId, ApiBase)");
        source.Should().Contain("InvokeVoidAsync(\"unmountRagChat\", RootElementId)");
        source.Should().NotContain("private ValueTask mountRagChat");
        source.Should().NotContain("private ValueTask unmountRagChat");
    }

    [Fact]
    public void RagChat_ViteConfigBuildsReactEntry()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "ClientApp", "vite.config.ts");

        source.Should().Contain("existsSync(\"src/rag-chat/main.tsx\")");
        source.Should().Contain("input.ragChat = \"src/rag-chat/main.tsx\";");
        source.Should().Contain("chunkInfo.name === \"ragChat\"");
        source.Should().Contain("rag-chat/assets/rag-chat.js");
        source.Should().Contain("name.includes(\"rag-chat\") || name.includes(\"ragchat\")");
        source.Should().Contain("rag-chat/assets/rag-chat.css");
    }

    [Fact]
    public void RagChat_BuildArtifactsAreCommitted()
    {
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "rag-chat", "assets", "rag-chat.js")
            .Should()
            .BeTrue();
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "rag-chat", "assets", "rag-chat.css")
            .Should()
            .BeTrue();
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
