using FluentAssertions;
using LightRAGNet.Server.Services.DocumentConversion;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class ManagedCodeDocumentMarkdownConverterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Converter.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConvertAsync_WhenExtensionUnsupported_ThrowsNotSupportedException()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.txt");
        await File.WriteAllTextAsync(path, "plain text", CancellationToken.None);
        var converter = CreateConverter();

        var act = () => converter.ConvertAsync(
            new FileInfo(path),
            "sample.txt",
            "text/plain",
            CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Only .pdf and .docx conversion is supported.");
    }

    [Fact]
    public async Task ConvertAsync_WhenSourceFileMissing_ThrowsFileNotFoundException()
    {
        var converter = CreateConverter();
        var path = Path.Combine(directory, "missing.pdf");

        var act = () => converter.ConvertAsync(
            new FileInfo(path),
            "missing.pdf",
            "application/pdf",
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<FileNotFoundException>();
        exception.Which.FileName.Should().Be(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ManagedCodeDocumentMarkdownConverter CreateConverter()
    {
        return new ManagedCodeDocumentMarkdownConverter(
            NullLogger<ManagedCodeDocumentMarkdownConverter>.Instance);
    }
}
