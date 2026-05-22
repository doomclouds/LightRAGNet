using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Server.Models;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed class FileSystemDocumentArtifactStore : IDocumentArtifactStore
{
    private const string OutsideRootMessage = "Document artifact path is outside the configured root.";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<FileSystemDocumentArtifactStore> logger;
    private readonly string rootPath;
    private readonly string rootPathWithSeparator;

    public FileSystemDocumentArtifactStore(
        IOptions<DocumentArtifactStoreOptions> options,
        ILogger<FileSystemDocumentArtifactStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.logger = logger;
        rootPath = Path.GetFullPath(options.Value.RootPath);
        rootPathWithSeparator = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
    }

    public async Task<DocumentArtifactWriteResult> SaveOriginalAsync(
        int documentId,
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only PDF and DOCX document artifacts are supported.");
        }

        extension = extension.ToLowerInvariant();
        var relativePath = Path.Combine("documents", documentId.ToString(), $"original{extension}");
        var absolutePath = ResolveUnderRoot(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var destination = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long size = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            sha256.TransformBlock(buffer, 0, bytesRead, outputBuffer: null, outputOffset: 0);
            size += bytesRead;
        }

        sha256.TransformFinalBlock([], 0, 0);
        var hash = Convert.ToHexStringLower(sha256.Hash!);

        return new DocumentArtifactWriteResult(absolutePath, relativePath, hash, size);
    }

    public async Task<DocumentArtifactWriteResult> SaveConvertedMarkdownAsync(
        int documentId,
        string markdown,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var relativePath = Path.Combine("documents", documentId.ToString(), "converted.md");
        var absolutePath = ResolveUnderRoot(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        var bytes = Utf8NoBom.GetBytes(markdown);
        await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new DocumentArtifactWriteResult(absolutePath, relativePath, hash, bytes.Length);
    }

    public async Task<string> ReadConvertedMarkdownAsync(string relativePath, CancellationToken cancellationToken)
    {
        var absolutePath = ResolveUnderRoot(relativePath);
        return await File.ReadAllTextAsync(absolutePath, Utf8NoBom, cancellationToken);
    }

    public FileInfo GetFileInfo(string relativePath)
    {
        var absolutePath = ResolveUnderRoot(relativePath);
        return new FileInfo(absolutePath);
    }

    public bool Exists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            return File.Exists(ResolveUnderRoot(relativePath));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public Task DeleteArtifactsAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = Path.Combine("documents", document.Id.ToString());
        var absolutePath = ResolveUnderRoot(relativePath);
        if (Directory.Exists(absolutePath))
        {
            Directory.Delete(absolutePath, recursive: true);
            logger.LogInformation("Deleted document artifacts for document {DocumentId}.", document.Id);
        }

        return Task.CompletedTask;
    }

    private string ResolveUnderRoot(string relativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!absolutePath.StartsWith(rootPathWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(OutsideRootMessage);
        }

        return absolutePath;
    }
}
