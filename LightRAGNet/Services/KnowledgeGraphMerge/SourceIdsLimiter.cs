using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Source IDs limit strategy implementation
/// Reference: Python version apply_source_ids_limit function
/// </summary>
internal class SourceIdsLimiter(IOptions<LightRAGOptions> options, ILogger<SourceIdsLimiter>? logger = null)
{
    private readonly LightRAGOptions _options = options.Value;
    private const string SourceIdsLimitMethodFifo = "FIFO";
    private const string SourceIdsLimitMethodKeep = "KEEP";
    // Consistent with Python version: default to FIFO (from constants.py DEFAULT_SOURCE_IDS_LIMIT_METHOD)
    private const string DefaultSourceIdsLimitMethod = SourceIdsLimitMethodFifo;

    public List<string> ApplyLimit(List<string> sourceIds, int maxLimit, string? identifier = null)
    {
        // Consistent with Python version: if limit <= 0, return empty list
        if (maxLimit <= 0)
            return [];

        var sourceIdsList = sourceIds.ToList(); // Consistent with Python version: convert to list
        if (sourceIdsList.Count <= maxLimit)
            return sourceIdsList;

        // Normalize method name (consistent with Python version normalize_source_ids_limit_method)
        var normalizedMethod = NormalizeSourceIdsLimitMethod(_options.SourceIdsLimitMethod);

        var truncated =
            // FIFO strategy: keep newest (tail)
            normalizedMethod == SourceIdsLimitMethodFifo ? sourceIdsList.TakeLast(maxLimit).ToList() :
            // KEEP strategy: keep oldest (head)
            sourceIdsList.Take(maxLimit).ToList();

        // Log (consistent with Python version)
        if (identifier != null && truncated.Count < sourceIdsList.Count)
        {
            logger?.LogDebug(
                "Source_id truncated: {Identifier} | {Method} keeping {TruncatedCount} of {OriginalCount} entries",
                identifier,
                normalizedMethod,
                truncated.Count,
                sourceIdsList.Count);
        }

        return truncated;
    }

    public string ComputeTruncationInfo(List<string> originalIds, List<string> limitedIds)
    {
        if (limitedIds.Count >= originalIds.Count)
            return string.Empty;

        var normalizedMethod = NormalizeSourceIdsLimitMethod(_options.SourceIdsLimitMethod);
        return normalizedMethod == SourceIdsLimitMethodFifo
            ? $"FIFO {limitedIds.Count}/{originalIds.Count}"
            : "KEEP Old";
    }

    /// <summary>
    /// Normalize source IDs limit method name (consistent with Python version normalize_source_ids_limit_method)
    /// </summary>
    private string NormalizeSourceIdsLimitMethod(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return DefaultSourceIdsLimitMethod;

        var normalized = method.ToUpperInvariant();
        if (normalized != SourceIdsLimitMethodFifo && normalized != SourceIdsLimitMethodKeep)
        {
            // If method name is invalid, fall back to default and log warning (consistent with Python version)
            logger?.LogWarning(
                "Unknown SOURCE_IDS_LIMIT_METHOD '{Method}', falling back to {DefaultMethod}",
                method,
                DefaultSourceIdsLimitMethod);
            return DefaultSourceIdsLimitMethod;
        }

        return normalized;
    }
}