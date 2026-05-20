using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class MarkdownDocumentsSourceTests
{
    [Fact]
    public void MarkdownDocuments_ServerReload_IgnoresMudTableCancellation()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
        source.Should().Contain("Document list reload was cancelled");
    }

    [Fact]
    public void MarkdownDocuments_ViewDocumentFailures_AreLoggedWithDocumentId()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("@inject ILogger<MarkdownDocuments> Logger");
        source.Should().Contain("Logger.LogWarning(ex, \"Failed to load markdown document: DocumentId={DocumentId}\", id);");
    }

    [Fact]
    public void MarkdownDocuments_TableReloads_AreCentralizedBehindRefreshDocumentsAsync()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("private enum DocumentRefreshReason");
        source.Should().Contain("private async Task RefreshDocumentsAsync(DocumentRefreshReason reason)");
        source.Should().NotContain("DebouncedReloadServerDataAsync");
        CountOccurrences(source, "ReloadServerData()").Should().Be(1);
    }

    [Fact]
    public void MarkdownDocuments_TaskUpdates_UseLocalMutationBeforeRefresh()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("ApplyTaskStatusUpdate(update)");
        source.Should().Contain("ShouldRefreshForTaskStatus(update, oldStatus)");
        source.Should().Contain("RefreshDocumentsAsync(DocumentRefreshReason.TaskStatusFinalized)");
    }

    private static string ReadPageSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagePath = Path.Combine(
            repositoryRoot,
            "src",
            "LightRAGNet.Web",
            "Components",
            "Pages",
            "MarkdownDocuments.razor");

        return File.ReadAllText(pagePath);
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

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
