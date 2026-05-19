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
