using LightRAGNet.Core.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.DocumentPreview;

public sealed class DocumentReferencePreviewResolver(AppDbContext context)
{
    private sealed record PreviewDocument(
        int Id,
        string? FileName,
        string? OriginalFileName,
        string? FileUrl,
        string? OriginalFilePath,
        string? ConvertedMarkdownPath);

    public async Task<IReadOnlyList<RagQueryReferenceDto>> ResolveAsync(
        IReadOnlyList<ReferenceItem> references,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(request);

        if (references.Count == 0)
        {
            return [];
        }

        var documents = await context.MarkdownDocuments
            .AsNoTracking()
            .Select(document => new PreviewDocument(
                document.Id,
                document.FileName,
                document.OriginalFileName,
                document.FileUrl,
                document.OriginalFilePath,
                document.ConvertedMarkdownPath))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return references
            .Select(reference => ResolveReference(reference, documents, request))
            .ToList();
    }

    private static RagQueryReferenceDto ResolveReference(
        ReferenceItem reference,
        IReadOnlyList<PreviewDocument> documents,
        HttpRequest request)
    {
        var document = FindMatchingDocument(documents, reference.FilePath);
        if (document is null)
        {
            return CreateExternalReference(reference);
        }

        return new RagQueryReferenceDto
        {
            ReferenceId = reference.ReferenceId,
            FilePath = reference.FilePath,
            FileName = SelectFileName(document, reference),
            PreviewUrl = BuildPreviewUrl(request, document.Id),
            OpenKind = SelectOpenKind(document)
        };
    }

    private static RagQueryReferenceDto CreateExternalReference(ReferenceItem reference)
    {
        return new RagQueryReferenceDto
        {
            ReferenceId = reference.ReferenceId,
            FilePath = reference.FilePath,
            FileName = ExtractDisplayName(reference.FilePath, reference.ReferenceId),
            OpenKind = ReferenceOpenKind.ExternalOrUnresolved
        };
    }

    private static PreviewDocument? FindMatchingDocument(
        IReadOnlyList<PreviewDocument> documents,
        string referencePath)
    {
        var normalizedReference = Normalize(referencePath);
        if (string.IsNullOrWhiteSpace(normalizedReference))
        {
            return null;
        }

        return FindUniqueMatch(documents, document => ExactMatch(document.FileUrl, normalizedReference))
            ?? FindUniqueMatch(documents, document => ExactMatch(document.OriginalFilePath, normalizedReference))
            ?? FindUniqueMatch(documents, document => ExactMatch(document.ConvertedMarkdownPath, normalizedReference))
            ?? FindUniqueMatch(documents, document => ExactMatch(document.OriginalFileName, normalizedReference))
            ?? FindUniqueMatch(documents, document => ExactMatch(document.FileName, normalizedReference))
            ?? (CanUseFallbackMatch(normalizedReference)
                ? FindUniqueMatch(documents, document => FallbackMatch(document, normalizedReference))
                : null);
    }

    private static PreviewDocument? FindUniqueMatch(
        IReadOnlyList<PreviewDocument> documents,
        Func<PreviewDocument, bool> predicate)
    {
        var matches = documents
            .Where(predicate)
            .DistinctBy(document => document.Id)
            .Take(2)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : null;
    }

    private static bool ExactMatch(string? value, string normalizedReference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return false;
        }

        return string.Equals(normalizedValue, normalizedReference, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FallbackMatch(PreviewDocument document, string normalizedReference)
    {
        return FallbackMatchValue(document.FileUrl, normalizedReference)
            || FallbackMatchValue(document.OriginalFilePath, normalizedReference)
            || FallbackMatchValue(document.ConvertedMarkdownPath, normalizedReference)
            || FallbackMatchValue(document.OriginalFileName, normalizedReference)
            || FallbackMatchValue(document.FileName, normalizedReference);
    }

    private static bool FallbackMatchValue(string? value, string normalizedReference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedValue)
            || string.Equals(normalizedValue, normalizedReference, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return EndsWithPathSegment(normalizedReference, normalizedValue)
            || EndsWithPathSegment(normalizedValue, normalizedReference)
            || string.Equals(ExtractDisplayName(normalizedValue, normalizedValue), normalizedReference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ExtractDisplayName(normalizedReference, normalizedReference), normalizedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUseFallbackMatch(string normalizedReference)
    {
        return !string.IsNullOrWhiteSpace(normalizedReference)
            && !normalizedReference.Contains('/', StringComparison.Ordinal)
            && !string.Equals(normalizedReference, ".", StringComparison.Ordinal)
            && !string.Equals(normalizedReference, "..", StringComparison.Ordinal);
    }

    private static bool EndsWithPathSegment(string value, string suffix)
    {
        return value.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string SelectFileName(PreviewDocument document, ReferenceItem reference)
    {
        if (!string.IsNullOrWhiteSpace(document.OriginalFileName))
        {
            return document.OriginalFileName;
        }

        if (!string.IsNullOrWhiteSpace(document.FileName))
        {
            return document.FileName;
        }

        return ExtractDisplayName(reference.FilePath, reference.ReferenceId);
    }

    private static string SelectOpenKind(PreviewDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.ConvertedMarkdownPath))
        {
            return ReferenceOpenKind.ConvertedMarkdown;
        }

        if (!string.IsNullOrWhiteSpace(document.OriginalFilePath))
        {
            return ReferenceOpenKind.OriginalArtifact;
        }

        return ReferenceOpenKind.DocumentPreview;
    }

    private static string BuildPreviewUrl(HttpRequest request, int documentId)
    {
        return $"{request.Scheme}://{request.Host}{request.PathBase.ToUriComponent()}/document-preview/{documentId}";
    }

    private static string Normalize(string value)
    {
        return Uri.UnescapeDataString(value.Replace('\\', '/').Trim());
    }

    private static string ExtractDisplayName(string value, string fallback)
    {
        var normalized = Normalize(value);
        var trimmed = normalized.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        var fileName = lastSlash >= 0 && lastSlash + 1 < trimmed.Length
            ? trimmed[(lastSlash + 1)..]
            : trimmed;

        return string.IsNullOrWhiteSpace(fileName)
            ? fallback
            : fileName;
    }
}
