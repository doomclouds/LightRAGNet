namespace LightRAGNet.Server.Services.DocumentConversion;

public interface IDocumentMarkdownConverter
{
    Task<DocumentMarkdownConversionResult> ConvertAsync(
        FileInfo sourceFile,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken);
}
