using System.IO.Compression;
using FluentAssertions;
using LightRAGNet.Server.Services.DocumentConversion;
using Microsoft.Extensions.Logging;
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
        exception.Which.Message.Should().Be("Source document file was not found.");
        exception.Which.Message.Should().NotContain(directory);
        exception.Which.FileName.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ConvertAsync_WhenCanceledAndSourceFileMissing_ThrowsOperationCanceledException()
    {
        var converter = CreateConverter();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var act = () => converter.ConvertAsync(
            new FileInfo(Path.Combine(directory, "missing.pdf")),
            "missing.pdf",
            "application/pdf",
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("missing.PDF")]
    [InlineData("missing.DOCX")]
    public async Task ConvertAsync_WhenExtensionUsesUppercase_UsesSupportedExtensionPath(string originalFileName)
    {
        var converter = CreateConverter();

        var act = () => converter.ConvertAsync(
            new FileInfo(Path.Combine(directory, originalFileName)),
            originalFileName,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("Source document file was not found.");
    }

    [Fact]
    public async Task ConvertAsync_WhenContentTypeSpoofed_ReturnsMediaTypeFromExtension()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.docx");
        CreateMinimalDocx(path, "Hello from docx");
        var converter = CreateConverter();

        var result = await converter.ConvertAsync(
            new FileInfo(path),
            "sample.docx",
            "text/plain",
            CancellationToken.None);

        result.DetectedMediaType.Should()
            .Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Markdown.Should().Contain("Hello from docx");
    }

    [Fact]
    public async Task ConvertAsync_WhenOriginalFileNameIncludesPath_LogsOnlyFileName()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.docx");
        CreateMinimalDocx(path, "Hello from logging test");
        var logger = new CapturingLogger<ManagedCodeDocumentMarkdownConverter>();
        var converter = new ManagedCodeDocumentMarkdownConverter(logger);

        await converter.ConvertAsync(
            new FileInfo(path),
            @"C:\unsafe\sample.docx",
            null,
            CancellationToken.None);

        logger.Messages.Should().Contain(message => message.Contains("sample.docx", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains(@"C:\unsafe", StringComparison.Ordinal));
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

    private static void CreateMinimalDocx(string path, string text)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddEntry(
            archive,
            "word/document.xml",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:r>
                    <w:t>{{System.Security.SecurityElement.Escape(text)}}</w:t>
                  </w:r>
                </w:p>
              </w:body>
            </w:document>
            """);
    }

    private static void AddEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
