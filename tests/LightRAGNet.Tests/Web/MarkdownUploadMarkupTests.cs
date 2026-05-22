using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class MarkdownUploadMarkupTests
{
    [Fact]
    public void MarkdownUpload_CustomFileUploadButton_OpensMudBlazorPicker()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

        markup.Should().Contain("<CustomContent Context=\"fileUpload\">");
        markup.Should().Contain("OnClick=\"@fileUpload.OpenFilePickerAsync\"");
        markup.Should().NotContain("HtmlTag=\"label\"");
    }

    [Fact]
    public void MarkdownUpload_AcceptsOnlyPdfAndDocx()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

        markup.Should().Contain("Accept=\".pdf,.docx\"");
        markup.Should().Contain("PDF");
        markup.Should().Contain("DOCX");
        markup.Should().NotContain("Accept=\".md,.markdown\"");
    }

    [Fact]
    public void MarkdownUpload_CopySaysAddToRagStartsProcessing()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

        markup.Should().Contain("Add to RAG");
        markup.Should().Contain("starts processing");
    }

    [Fact]
    public void MarkdownUpload_CopyDoesNotPromiseDuplicateHashBlocking()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

        markup.Should().NotContain("duplicate files");
        markup.Should().NotContain("content hash");
        markup.Should().NotContain("duplicate files cannot be uploaded");
        markup.Should().NotContain("content duplicate");
        markup.Should().NotContain("IsDuplicate");
        markup.Should().NotContain("duplicateCount");
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
