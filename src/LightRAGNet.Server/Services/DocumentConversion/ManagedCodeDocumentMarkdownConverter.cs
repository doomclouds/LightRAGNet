using MarkItDown;

namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed class ManagedCodeDocumentMarkdownConverter(
    ILogger<ManagedCodeDocumentMarkdownConverter> logger) : IDocumentMarkdownConverter
{
    private const string UnsupportedDocumentMessage = "Only .pdf and .docx conversion is supported.";
    private const string PdfMediaType = "application/pdf";
    private const string DocxMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<DocumentMarkdownConversionResult> ConvertAsync(
        FileInfo sourceFile,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx")
        {
            throw new NotSupportedException(UnsupportedDocumentMessage);
        }

        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("Source document file was not found.");
        }

        logger.LogInformation("Converting document to Markdown: {FileName}", Path.GetFileName(originalFileName));

        var client = new MarkItDownClient();
        await using var result = await client.ConvertAsync(sourceFile.FullName, cancellationToken: cancellationToken);
        var markdown = result.Markdown?.Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Document conversion produced empty Markdown.");
        }

        return new DocumentMarkdownConversionResult(
            markdown,
            GuessMediaType(extension),
            []);
    }

    private static string GuessMediaType(string extension)
    {
        return extension switch
        {
            ".pdf" => PdfMediaType,
            ".docx" => DocxMediaType,
            _ => "application/octet-stream"
        };
    }
}
