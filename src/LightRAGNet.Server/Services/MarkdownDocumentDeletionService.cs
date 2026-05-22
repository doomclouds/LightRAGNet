using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.AspNetCore.Http;

namespace LightRAGNet.Server.Services;

public sealed class MarkdownDocumentDeletionService(
    IDocumentArtifactStore artifactStore,
    ILogger<MarkdownDocumentDeletionService> logger)
{
    private const string UploadsPrefix = "/uploads/";

    public string? CreateTrustedUploadReference(MarkdownDocument document, HostString requestHost)
    {
        if (string.IsNullOrWhiteSpace(document.FileUrl))
        {
            return null;
        }

        var localPath = document.FileUrl.Replace('\\', '/');
        if (Uri.TryCreate(document.FileUrl, UriKind.Absolute, out var uri))
        {
            if (!AuthorityMatchesRequestHost(uri, requestHost))
            {
                logger.LogWarning(
                    "File URL authority does not match current request host, skipping upload reference: {FileUrl}",
                    document.FileUrl);
                return null;
            }

            localPath = uri.LocalPath.Replace('\\', '/');
        }

        var fileName = ExtractTrustedUploadFileName(localPath, document.FileUrl);
        if (fileName is null)
        {
            return null;
        }

        if (!TryResolveUploadPath(fileName, out _))
        {
            logger.LogWarning("Resolved file path is outside uploads folder, skipping upload reference: {FileName}", fileName);
            return null;
        }

        return $"{UploadsPrefix}{fileName}";
    }

    public void DeleteUploadedFileIfPresent(string? trustedUploadReference)
    {
        if (string.IsNullOrWhiteSpace(trustedUploadReference))
        {
            return;
        }

        try
        {
            var fileName = ExtractTrustedUploadFileName(trustedUploadReference.Replace('\\', '/'), trustedUploadReference);
            if (fileName is null)
            {
                return;
            }

            if (!TryResolveUploadPath(fileName, out var filePath))
            {
                logger.LogWarning("Resolved file path is outside uploads folder, skipping deletion: {FileName}", fileName);
                return;
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger.LogInformation("Deleted uploaded file: {FilePath}", filePath);
            }
            else
            {
                logger.LogWarning("Uploaded file does not exist, skipping deletion: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error occurred while deleting uploaded file: {UploadReference}", trustedUploadReference);
        }
    }

    public async Task DeleteDocumentArtifactsAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken)
    {
        await artifactStore.DeleteArtifactsAsync(document, cancellationToken);
    }

    private static bool AuthorityMatchesRequestHost(Uri uri, HostString requestHost)
    {
        var hostMatches = string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase);
        var portMatches = requestHost.Port.HasValue
            ? uri.Port == requestHost.Port.Value
            : uri.IsDefaultPort;

        return hostMatches && portMatches;
    }

    private string? ExtractTrustedUploadFileName(string localPath, string originalReference)
    {
        if (!localPath.StartsWith(UploadsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("File URL is outside uploads namespace, skipping deletion: {FileUrl}", originalReference);
            return null;
        }

        var uploadedPath = Uri.UnescapeDataString(localPath[UploadsPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(uploadedPath) ||
            uploadedPath.Contains('/') ||
            uploadedPath.Contains('\\') ||
            uploadedPath is "." or "..")
        {
            logger.LogWarning("File URL contains an invalid uploaded file path, skipping deletion: {FileUrl}", originalReference);
            return null;
        }

        return uploadedPath;
    }

    private static bool TryResolveUploadPath(string fileName, out string filePath)
    {
        var uploadsFolder = GetUploadsPath();
        var uploadsRoot = Path.GetFullPath(uploadsFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        filePath = Path.GetFullPath(Path.Combine(uploadsFolder, fileName));

        return filePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUploadsPath()
    {
        var uploadsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uploads");
        Directory.CreateDirectory(uploadsPath);
        return uploadsPath;
    }
}
