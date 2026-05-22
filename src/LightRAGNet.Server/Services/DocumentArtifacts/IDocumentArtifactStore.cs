using LightRAGNet.Server.Models;

namespace LightRAGNet.Server.Services.DocumentArtifacts;

public interface IDocumentArtifactStore
{
    Task<DocumentArtifactWriteResult> SaveOriginalAsync(
        int documentId,
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken);

    Task<DocumentArtifactWriteResult> SaveConvertedMarkdownAsync(
        int documentId,
        string markdown,
        CancellationToken cancellationToken);

    Task<string> ReadConvertedMarkdownAsync(string relativePath, CancellationToken cancellationToken);

    FileInfo GetFileInfo(string relativePath);

    bool Exists(string? relativePath);

    Task DeleteArtifactsAsync(MarkdownDocument document, CancellationToken cancellationToken);
}
