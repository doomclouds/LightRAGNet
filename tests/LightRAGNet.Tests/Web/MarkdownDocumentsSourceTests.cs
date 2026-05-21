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

    [Fact]
    public void ApiClient_MarkdownDocumentsQuery_SupportsStatusAndTrackFilters()
    {
        var source = NormalizeLineEndings(ReadApiClientSource());

        source.Should().Contain("string? status = null");
        source.Should().Contain("string? trackId = null");
        source.Should().Contain("status={Uri.EscapeDataString(status)}");
        source.Should().Contain("trackId={Uri.EscapeDataString(trackId)}");
    }

    [Fact]
    public void ApiClient_DocumentPipelineActions_PostToDocumentEndpoints()
    {
        var source = NormalizeLineEndings(ReadApiClientSource());

        source.Should().Contain("Task<DocumentPipelineActionResult?> RetryDocumentAsync(");
        source.Should().Contain("api/MarkdownDocuments/{id}/retry");
        source.Should().Contain("Task<DocumentPipelineActionResult?> CancelDocumentPipelineAsync(");
        source.Should().Contain("api/MarkdownDocuments/{id}/cancel");
    }

    [Fact]
    public void MarkdownDocuments_StatusFilter_IsSentThroughServerReload()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("private string? _selectedStatusFilter;");
        source.Should().Contain("Value=\"_selectedStatusFilter\"");
        source.Should().Contain("GetMarkdownDocumentsAsync(state.Page + 1, state.PageSize, cancellationToken, status: _selectedStatusFilter)");
    }

    [Fact]
    public void MarkdownDocuments_PipelineActions_AreAvailableForEligibleStatuses()
    {
        var source = NormalizeLineEndings(ReadPageSource());

        source.Should().Contain("CanRetryDocument(context)");
        source.Should().Contain("CanCancelDocumentPipeline(context)");
        source.Should().Contain("RetryDocument(context)");
        source.Should().Contain("CancelDocumentPipeline(context)");
        source.Should().Contain("private async Task RetryDocument(MarkdownDocumentDto document)");
        source.Should().Contain("private async Task CancelDocumentPipeline(MarkdownDocumentDto document)");
        source.Should().Contain("RefreshDocumentsAsync(DocumentRefreshReason.UserAction)");
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

    private static string ReadApiClientSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var clientPath = Path.Combine(
            repositoryRoot,
            "src",
            "LightRAGNet.Web",
            "ApiClient.cs");

        return File.ReadAllText(clientPath);
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
