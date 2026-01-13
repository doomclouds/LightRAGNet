using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Relation builder implementation
/// Reference: Python version _merge_edges_then_upsert function
/// </summary>
internal class RelationBuilder(
    IGraphStore graphStore,
    DescriptionMerger descriptionMerger,
    SourceIdsLimiter sourceIdsLimiter,
    IOptions<LightRAGOptions> options,
    ILogger<RelationBuilder> logger)
{
    private readonly LightRAGOptions _options = options.Value;

    private const string GraphFieldSep = "<SEP>";
    private const string SourceIdsLimitMethodFifo = "FIFO";
    private const string SourceIdsLimitMethodKeep = "KEEP";

    public async Task<RelationMergeData?> BuildAsync(
        string sourceId,
        string targetId,
        List<Relationship> relations,
        IKVStore relationChunksStore,
        CancellationToken cancellationToken = default)
    {
        // Check self-loop
        if (sourceId == targetId)
            return null;

        if (relations.Count == 0)
            throw new ArgumentException("Relation list cannot be empty", nameof(relations));

        // 1. Get existing edge data
        var alreadyEdge = await GetExistingEdgeAsync(sourceId, targetId, cancellationToken);
        var alreadyWeights = new List<double>();
        var alreadySourceIds = new List<string>();
        var alreadyDescriptions = new List<string>();
        var alreadyKeywords = new List<string>();
        var alreadyFilePaths = new List<string>();

        if (alreadyEdge != null)
        {
            // Get weight, default 1.0
            if (alreadyEdge.Properties.TryGetValue("weight", out var weightObj))
            {
                var existingWeight = weightObj switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    _ => 1.0
                };
                alreadyWeights.Add(existingWeight);
            }
            else
            {
                alreadyWeights.Add(1.0);
            }

            // Get source_id
            if (alreadyEdge.Properties.TryGetValue("source_id", out var sid))
            {
                var existingSourceIdStr = sid.ToString() ?? "";
                if (!string.IsNullOrEmpty(existingSourceIdStr))
                {
                    alreadySourceIds.AddRange(existingSourceIdStr.Split(GraphFieldSep));
                }
            }

            // Get file_path
            if (alreadyEdge.Properties.TryGetValue("file_path", out var fp))
            {
                var filePathStr = fp.ToString() ?? "";
                if (!string.IsNullOrEmpty(filePathStr))
                {
                    alreadyFilePaths.AddRange(filePathStr.Split(GraphFieldSep));
                }
            }

            // Get description
            if (alreadyEdge.Properties.TryGetValue("description", out var desc))
            {
                var descStr = desc.ToString() ?? "";
                if (!string.IsNullOrEmpty(descStr))
                {
                    alreadyDescriptions.AddRange(descStr.Split(GraphFieldSep));
                }
            }

            // Get keywords (split by GRAPH_FIELD_SEP, consistent with Python version split_string_by_multi_markers)
            if (alreadyEdge.Properties.TryGetValue("keywords", out var kw))
            {
                var keywordsStr = kw.ToString() ?? "";
                if (!string.IsNullOrEmpty(keywordsStr))
                {
                    // Python version uses split_string_by_multi_markers, supports multiple separators
                    // Here first split by GRAPH_FIELD_SEP, then by comma (because keywords are stored with comma connection)
                    var parts = keywordsStr.Split(GraphFieldSep);
                    foreach (var part in parts)
                    {
                        if (!string.IsNullOrEmpty(part))
                        {
                            alreadyKeywords.Add(part);
                        }
                    }
                }
            }
        }

        // 2. Collect new source_ids
        var newSourceIds = relations
            .Select(r => r.SourceChunkId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        // 3. Get existing source_ids from relation_chunks_storage
        var storageKey = MakeRelationChunkKey(sourceId, targetId);
        var existingFullSourceIds = new List<string>();
        var storedChunks = await relationChunksStore.GetByIdAsync(storageKey, cancellationToken);
        if (storedChunks != null && storedChunks.TryGetValue("chunk_ids", out var chunkIdsObj))
        {
            if (chunkIdsObj is List<object> chunkIds)
            {
                existingFullSourceIds = chunkIds
                    .Select(id => id.ToString())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList()!;
            }
        }

        // If not obtained from storage, use source_ids from existing edge
        if (existingFullSourceIds.Count == 0)
        {
            existingFullSourceIds = alreadySourceIds
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
        }

        // 4. Merge source_ids
        var fullSourceIds = MergeSourceIds(existingFullSourceIds, newSourceIds);

        // 5. Update relation_chunks_storage
        if (fullSourceIds.Count > 0)
        {
            await relationChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [storageKey] = new()
                {
                    ["chunk_ids"] = fullSourceIds,
                    ["count"] = fullSourceIds.Count
                }
            }, cancellationToken);
        }

        // 6. Apply source_ids limit
        var sourceIds = sourceIdsLimiter.ApplyLimit(fullSourceIds, _options.MaxSourceIdsPerRelation, $"`{sourceId}`~`{targetId}`");

        // Re-get limit_method (Python version re-gets here)
        var limitMethod = !string.IsNullOrEmpty(_options.SourceIdsLimitMethod)
            ? _options.SourceIdsLimitMethod
            : SourceIdsLimitMethodKeep;

        // 7. Filter edges in KEEP mode
        var filteredRelations = relations;
        if (limitMethod == SourceIdsLimitMethodKeep)
        {
            var allowedSourceIds = sourceIds.ToHashSet();
            filteredRelations = relations
                .Where(r => string.IsNullOrEmpty(r.SourceChunkId) ||
                           allowedSourceIds.Contains(r.SourceChunkId) ||
                           existingFullSourceIds.Contains(r.SourceChunkId))
                .ToList();
        }

        // 8. Check if need to skip (KEEP mode and already have enough data and no new edges)
        if (limitMethod == SourceIdsLimitMethodKeep &&
            existingFullSourceIds.Count >= _options.MaxSourceIdsPerRelation &&
            filteredRelations.Count == 0)
        {
            if (alreadyEdge != null)
            {
                logger.LogInformation(
                    "Skipped `{SourceId}`~`{TargetId}`: KEEP old chunks {AlreadyCount}/{FullCount}",
                    sourceId,
                    targetId,
                    alreadySourceIds.Count,
                    fullSourceIds.Count);

                // Return existing data (need to include FullSourceIds for subsequent processing)
                return new RelationMergeData
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    RelationContent = $"{string.Join(",", ExtractKeywords(alreadyKeywords))}\t{sourceId}\n{targetId}\n{alreadyDescriptions.FirstOrDefault() ?? ""}",
                    EdgeData = alreadyEdge.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    RelationId = HashUtils.ComputeMd5Hash(sourceId + targetId, "rel-"),
                    FullSourceIds = fullSourceIds // Save complete source_ids list for subsequent processing
                };
            }

            logger.LogError("Internal Error: already_edge missing for `{SourceId}`~`{TargetId}`", sourceId, targetId);
            throw new InvalidOperationException($"Internal Error: already_edge missing for `{sourceId}`~`{targetId}`");
        }

        // 9. Determine source_id (join)
        var sourceIdStr = string.Join(GraphFieldSep, sourceIds);

        // 10. Calculate weight (accumulate)
        var newWeight = filteredRelations.Sum(r => r.Weight);
        var weight = newWeight + alreadyWeights.Sum();

        // 11. Merge keywords (need to calculate before checking descriptions for use when needed)
        var keywords = MergeKeywords(alreadyKeywords, filteredRelations);

        // 12. Deduplicate descriptions and sort by timestamp and length
        var sortedDescriptions = SortAndDeduplicateDescriptions(filteredRelations);

        // 13. Merge existing and newly sorted descriptions, and deduplicate (avoid duplicates with existing descriptions)
        // Only add new descriptions that are not in existing descriptions
        var alreadyDescriptionsSet = alreadyDescriptions.ToHashSet();
        var uniqueNewDescriptions = sortedDescriptions
            .Where(desc => !alreadyDescriptionsSet.Contains(desc))
            .ToList();
        
        var descriptionList = alreadyDescriptions.Concat(uniqueNewDescriptions).ToList();
        if (descriptionList.Count == 0)
        {
            // Consistent with Python version: if no description, throw exception
            logger.LogError("Relation {SourceId}~{TargetId} has no description", sourceId, targetId);
            throw new InvalidOperationException($"Relation {sourceId}~{targetId} has no description");
        }

        // 14. Merge descriptions
        var (mergedDescription, llmWasUsed) = await descriptionMerger.MergeAsync(
            "Relation",
            $"({sourceId}, {targetId})",
            descriptionList,
            cancellationToken);

        // 15. Build file_path (including placeholder handling)
        var filePath = BuildFilePath(alreadyFilePaths, filteredRelations, sourceId, targetId);

        // 16. Calculate truncation_info (completely consistent with Python version)
        var truncationInfo = "";
        var truncationInfoLog = "";
        if (sourceIds.Count < fullSourceIds.Count)
        {
            truncationInfoLog = $"{limitMethod} {sourceIds.Count}/{fullSourceIds.Count}";
            truncationInfo = limitMethod == SourceIdsLimitMethodFifo ? truncationInfoLog : "KEEP Old";
        }

        // 17. Log (consistent with Python version)
        var numFragment = descriptionList.Count;
        var alreadyFragment = alreadyDescriptions.Count;
        var deduplicatedNum = alreadyFragment + filteredRelations.Count - numFragment;

        var statusMessage = llmWasUsed
            ? $"LLMmrg: `{sourceId}`~`{targetId}` | {alreadyFragment}+{numFragment - alreadyFragment}"
            : $"Merged: `{sourceId}`~`{targetId}` | {alreadyFragment}+{numFragment - alreadyFragment}";

        var ddMessage = "";
        if (deduplicatedNum > 0)
        {
            ddMessage = $"dd {deduplicatedNum}";
        }

        if (!string.IsNullOrEmpty(ddMessage) || !string.IsNullOrEmpty(truncationInfoLog))
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(truncationInfoLog))
                parts.Add(truncationInfoLog);
            if (!string.IsNullOrEmpty(ddMessage))
                parts.Add(ddMessage);
            statusMessage += $" ({string.Join(", ", parts)})";
        }

        // Determine log level based on conditions (consistent with Python version)
        if (alreadyFragment > 0 || llmWasUsed)
        {
            logger.LogInformation(statusMessage);
        }
        else
        {
            logger.LogDebug(statusMessage);
        }

        // 18. Build edge data
        var edgeData = new Dictionary<string, object>
        {
            ["description"] = mergedDescription,
            ["keywords"] = keywords,
            ["weight"] = weight,
            ["source_id"] = sourceIdStr,
            ["file_path"] = filePath,
            ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["truncate"] = truncationInfo
        };

        // 19. Build relation content and ID
        var relationContent = $"{keywords}\t{sourceId}\n{targetId}\n{mergedDescription}";
        var relationId = HashUtils.ComputeMd5Hash(sourceId + targetId, "rel-");

        return new RelationMergeData
        {
            SourceId = sourceId,
            TargetId = targetId,
            RelationContent = relationContent,
            EdgeData = edgeData,
            RelationId = relationId,
            FullSourceIds = fullSourceIds // Save complete source_ids list
        };
    }

    private async Task<GraphEdge?> GetExistingEdgeAsync(string sourceId, string targetId, CancellationToken cancellationToken)
    {
        if (await graphStore.HasEdgeAsync(sourceId, targetId, cancellationToken))
        {
            return await graphStore.GetEdgeAsync(sourceId, targetId, cancellationToken);
        }
        return null;
    }

    private static string MakeRelationChunkKey(string src, string tgt)
    {
        // Python version: GRAPH_FIELD_SEP.join(sorted((src, tgt)))
        var sorted = new[] { src, tgt }.OrderBy(x => x).ToArray();
        return string.Join(GraphFieldSep, sorted);
    }

    private static List<string> MergeSourceIds(List<string> existingIds, List<string> newIds)
    {
        // Reference Python version merge_source_ids function: maintain order and deduplicate
        var seen = new HashSet<string>();

        // First add existing_ids, maintaining order
        var merged = existingIds.Where(id => !string.IsNullOrEmpty(id)).Where(id => seen.Add(id)).ToList();
        // Then add new_ids, maintaining order
        merged.AddRange(newIds.Where(id => !string.IsNullOrEmpty(id)).Where(id => seen.Add(id)));

        return merged;
    }

    private static List<string> ExtractKeywords(List<string> keywordStrings)
    {
        var allKeywords = new HashSet<string>();
        foreach (var trimmed in from keywordStr in keywordStrings where !string.IsNullOrEmpty(keywordStr) from k in keywordStr.Split(',') select k.Trim() into trimmed where !string.IsNullOrEmpty(trimmed) select trimmed)
        {
            allKeywords.Add(trimmed);
        }
        return allKeywords.OrderBy(k => k).ToList();
    }

    private static string MergeKeywords(List<string> alreadyKeywords, List<Relationship> relations)
    {
        var allKeywords = new HashSet<string>();

        // Process existing keywords (comma-separated)
        foreach (var trimmed in from keywordStr in alreadyKeywords where !string.IsNullOrEmpty(keywordStr) from k in keywordStr.Split(',') select k.Trim() into trimmed where !string.IsNullOrEmpty(trimmed) select trimmed)
        {
            allKeywords.Add(trimmed);
        }

        // Process new keywords
        foreach (var trimmed in from relation in relations where !string.IsNullOrEmpty(relation.Keywords) from k in relation.Keywords.Split(',') select k.Trim() into trimmed where !string.IsNullOrEmpty(trimmed) select trimmed)
        {
            allKeywords.Add(trimmed);
        }

        // Join all unique keywords with comma, sorted
        return string.Join(",", allKeywords.OrderBy(k => k));
    }

    private static List<string> SortAndDeduplicateDescriptions(List<Relationship> relations)
    {
        // Deduplicate descriptions, keeping first occurrence
        var uniqueEdges = new Dictionary<string, Relationship>();
        foreach (var relation in relations.Where(r => !string.IsNullOrWhiteSpace(r.Description)))
        {
            uniqueEdges.TryAdd(relation.Description, relation);
        }

        // Sort by timestamp, then by description length (descending)
        var sortedEdges = uniqueEdges.Values
            .OrderBy(r => r.Timestamp)
            .ThenByDescending(r => r.Description.Length)
            .ToList();

        return sortedEdges
            .Select(r => r.Description)
            .ToList();
    }

    private string BuildFilePath(List<string> alreadyFilePaths, List<Relationship> relations, string sourceId, string targetId)
    {
        var filePathsList = new List<string>();
        var seenPaths = new HashSet<string>();
        var hasPlaceholder = false;

        var maxFilePaths = _options.MaxFilePaths;
        // Get placeholder from config, use default if not available
        const string filePathPlaceholder = "truncated"; // DEFAULT_FILE_PATH_MORE_PLACEHOLDER

        // Collect from existing file_paths, excluding placeholders
        foreach (var fp in alreadyFilePaths.Where(fp => !string.IsNullOrEmpty(fp)))
        {
            if (fp.StartsWith($"...{filePathPlaceholder}"))
            {
                hasPlaceholder = true;
                continue;
            }

            if (!seenPaths.Contains(fp))
            {
                filePathsList.Add(fp);
                seenPaths.Add(fp);
            }
        }

        // Collect from new data
        foreach (var relation in relations.Where(r => !string.IsNullOrEmpty(r.FilePath))
                     .Where(r => !seenPaths.Contains(r.FilePath)))
        {
            filePathsList.Add(relation.FilePath);
            seenPaths.Add(relation.FilePath);
        }

        // Apply quantity limit
        if (filePathsList.Count > maxFilePaths)
        {
            var limitMethod = !string.IsNullOrEmpty(_options.SourceIdsLimitMethod)
                ? _options.SourceIdsLimitMethod
                : SourceIdsLimitMethodKeep;
            var originalCountStr = hasPlaceholder
                ? $"{filePathsList.Count}+"
                : filePathsList.Count.ToString();

            if (limitMethod == SourceIdsLimitMethodFifo)
            {
                // FIFO: Keep tail (newest), discard head
                filePathsList = filePathsList.TakeLast(maxFilePaths).ToList();
                filePathsList.Add($"...{filePathPlaceholder}...(FIFO)");
            }
            else
            {
                // KEEP: Keep head (oldest), discard tail
                filePathsList = filePathsList.Take(maxFilePaths).ToList();
                filePathsList.Add($"...{filePathPlaceholder}...(KEEP Old)");
            }

            logger.LogInformation(
                "Limited `{SourceId}`~`{TargetId}`: file_path {OriginalCount} -> {MaxCount} ({LimitMethod})",
                sourceId,
                targetId,
                originalCountStr,
                maxFilePaths,
                limitMethod);
        }

        return string.Join(GraphFieldSep, filePathsList);
    }
}
