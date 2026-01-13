using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Entity builder implementation
/// Reference: Python version _merge_nodes_then_upsert function
/// </summary>
internal class EntityBuilder(
    IGraphStore graphStore,
    DescriptionMerger descriptionMerger,
    SourceIdsLimiter sourceIdsLimiter,
    IOptions<LightRAGOptions> options,
    ILogger<EntityBuilder> logger)
{
    private readonly LightRAGOptions _options = options.Value;

    private const string GraphFieldSep = "<SEP>";
    private const string SourceIdsLimitMethodFifo = "FIFO";
    private const string SourceIdsLimitMethodKeep = "KEEP";

    public async Task<EntityMergeData?> BuildAsync(
        string entityName,
        List<Entity> entities,
        IKVStore entityChunksStore,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
            throw new ArgumentException("Entity list cannot be empty", nameof(entities));

        // 1. Get existing node data
        var alreadyNode = await graphStore.GetNodeAsync(entityName, cancellationToken);
        var alreadyEntityTypes = new List<string>();
        var alreadySourceIds = new List<string>();
        var alreadyDescriptions = new List<string>();
        var alreadyFilePaths = new List<string>();

        if (alreadyNode != null)
        {
            // Python version directly accesses dictionary keys, assuming fields always exist
            // For consistency, we also directly access, using default values if not present
            var existingEntityType = alreadyNode.Properties.TryGetValue("entity_type", out var et)
                ? et.ToString() ?? "UNKNOWN"
                : "UNKNOWN";
            alreadyEntityTypes.Add(existingEntityType);

            // Python version: already_source_ids.extend(already_node["source_id"].split(GRAPH_FIELD_SEP))
            // merge_source_ids function filters empty strings, so empty strings can be included here
            var sourceIdStr = alreadyNode.Properties.TryGetValue("source_id", out var sid)
                ? sid.ToString() ?? ""
                : "";
            if (!string.IsNullOrEmpty(sourceIdStr))
            {
                // Python version split does not filter empty strings, but merge_source_ids will filter
                // For consistency, we don't filter here either, let MergeSourceIds function handle it
                alreadySourceIds.AddRange(sourceIdStr.Split(GraphFieldSep));
            }

            // Python version: already_file_paths.extend(already_node["file_path"].split(GRAPH_FIELD_SEP))
            var filePathStr = alreadyNode.Properties.TryGetValue("file_path", out var fp)
                ? fp.ToString() ?? ""
                : "";
            if (!string.IsNullOrEmpty(filePathStr))
            {
                alreadyFilePaths.AddRange(filePathStr.Split(GraphFieldSep));
            }

            // Python version: already_description.extend(already_node["description"].split(GRAPH_FIELD_SEP))
            var descStr = alreadyNode.Properties.TryGetValue("description", out var desc)
                ? desc.ToString() ?? ""
                : "";
            if (!string.IsNullOrEmpty(descStr))
            {
                alreadyDescriptions.AddRange(descStr.Split(GraphFieldSep));
            }
        }

        // 2. Collect new source_ids
        var newSourceIds = entities
            .Select(e => e.SourceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        // 3. Get existing source_ids from entity_chunks_storage
        var existingFullSourceIds = new List<string>();
        var storedChunks = await entityChunksStore.GetByIdAsync(entityName, cancellationToken);
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

        // If not obtained from storage, use source_ids from existing node
        if (existingFullSourceIds.Count == 0)
        {
            existingFullSourceIds = alreadySourceIds
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
        }

        // 4. Merge source_ids
        var fullSourceIds = MergeSourceIds(existingFullSourceIds, newSourceIds);

        // 5. Update entity_chunks_storage
        if (fullSourceIds.Count > 0)
        {
            await entityChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [entityName] = new()
                {
                    ["chunk_ids"] = fullSourceIds,
                    ["count"] = fullSourceIds.Count
                }
            }, cancellationToken);
        }

        // 6. Apply source_ids limit
        var limitMethod = !string.IsNullOrEmpty(_options.SourceIdsLimitMethod)
            ? _options.SourceIdsLimitMethod
            : SourceIdsLimitMethodFifo;
        var sourceIds = sourceIdsLimiter.ApplyLimit(fullSourceIds, _options.MaxSourceIdsPerEntity, $"`{entityName}`");

        // 7. Filter nodes in KEEP mode
        var filteredEntities = entities;
        if (limitMethod == SourceIdsLimitMethodKeep)
        {
            var allowedSourceIds = sourceIds.ToHashSet();
            filteredEntities = entities
                .Where(e => string.IsNullOrEmpty(e.SourceId) ||
                            allowedSourceIds.Contains(e.SourceId) ||
                            existingFullSourceIds.Contains(e.SourceId))
                .ToList();
        }

        // 8. Check if need to skip (KEEP mode and already have enough data and no new nodes)
        if (limitMethod == SourceIdsLimitMethodKeep &&
            existingFullSourceIds.Count >= _options.MaxSourceIdsPerEntity &&
            filteredEntities.Count == 0)
        {
            if (alreadyNode != null)
            {
                logger.LogInformation(
                    "Skipped `{EntityName}`: KEEP old chunks {AlreadyCount}/{FullCount}",
                    entityName,
                    alreadySourceIds.Count,
                    fullSourceIds.Count);

                // Return existing data
                return new EntityMergeData
                {
                    EntityName = entityName,
                    EntityContent = $"{entityName}\n{alreadyDescriptions.FirstOrDefault() ?? ""}",
                    NodeData = alreadyNode.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    EntityId = HashUtils.ComputeMd5Hash(entityName, "ent-")
                };
            }

            logger.LogError("Internal Error: already_node missing for `{EntityName}`", entityName);
            throw new InvalidOperationException($"Internal Error: already_node missing for `{entityName}`");
        }

        // 9. Determine entity type (by counting)
        var entityType = DetermineEntityType(filteredEntities, alreadyEntityTypes);

        // 10. Deduplicate descriptions and sort by timestamp and length
        var sortedDescriptions = SortAndDeduplicateDescriptions(filteredEntities);

        // 11. Merge existing and newly sorted descriptions, and deduplicate (avoid duplicates with existing descriptions)
        // Only add new descriptions that are not in existing descriptions
        var alreadyDescriptionsSet = alreadyDescriptions.ToHashSet();
        var uniqueNewDescriptions = sortedDescriptions
            .Where(desc => !alreadyDescriptionsSet.Contains(desc))
            .ToList();
        
        var descriptionList = alreadyDescriptions.Concat(uniqueNewDescriptions).ToList();
        if (descriptionList.Count == 0)
        {
            // Consistent with Python version: if no description, throw exception
            logger.LogError("Entity {EntityName} has no description", entityName);
            throw new InvalidOperationException($"Entity {entityName} has no description");
        }

        // 12. Merge descriptions
        var (mergedDescription, llmWasUsed) = await descriptionMerger.MergeAsync(
            "Entity",
            entityName,
            descriptionList,
            cancellationToken);

        // 13. Build file_path (including placeholder handling)
        var filePath = BuildFilePath(alreadyFilePaths, filteredEntities, entityName);

        // 14. Calculate truncation_info (completely consistent with Python version)
        var truncationInfo = "";
        var truncationInfoLog = "";
        if (sourceIds.Count < fullSourceIds.Count)
        {
            truncationInfoLog = $"{limitMethod} {sourceIds.Count}/{fullSourceIds.Count}";
            truncationInfo = limitMethod == SourceIdsLimitMethodFifo ? truncationInfoLog : "KEEP Old";
        }

        // 15. Log (display description content before and after merge)
        var numFragment = descriptionList.Count;
        var alreadyFragment = alreadyDescriptions.Count;
        // Calculate deduplication count: original entity count + existing description count - final description count
        // Note: uniqueNewDescriptions has already removed duplicates with existing descriptions
        var deduplicatedNum = alreadyFragment + filteredEntities.Count - numFragment;

        // Build pre-merge description summary (for logging)
        var beforeMergeSummary = descriptionList.Count > 0
            ? string.Join(" | ", descriptionList.Select((desc, idx) => 
                $"Desc{idx + 1}: {TruncateText(desc, 100)}"))
            : "No description";

        // Build post-merge description summary (for logging)
        var afterMergeSummary = !string.IsNullOrEmpty(mergedDescription)
            ? TruncateText(mergedDescription, 200)
            : "No description";

        var statusMessage = llmWasUsed
            ? $"LLMmrg: `{entityName}` | Merged {numFragment} descriptions -> 1 merged description"
            : $"Merged: `{entityName}` | Merged {numFragment} descriptions -> 1 merged description";

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
            // Log detailed content before and after merge
            logger.LogInformation("  Before merge ({Count} descriptions): {BeforeMerge}", numFragment, beforeMergeSummary);
            logger.LogInformation("  After merge: {AfterMerge}", afterMergeSummary);
        }
        else
        {
            logger.LogDebug(statusMessage);
            // Log detailed content before and after merge
            logger.LogDebug("  Before merge ({Count} descriptions): {BeforeMerge}", numFragment, beforeMergeSummary);
            logger.LogDebug("  After merge: {AfterMerge}", afterMergeSummary);
        }

        // 16. Build node data
        var nodeData = new Dictionary<string, object>
        {
            ["entity_id"] = entityName,
            ["entity_type"] = entityType,
            ["description"] = mergedDescription,
            ["source_id"] = string.Join(GraphFieldSep, sourceIds),
            ["file_path"] = filePath,
            ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["truncate"] = truncationInfo
        };

        // 17. Build entity content and ID
        var entityContent = $"{entityName}\n{mergedDescription}";
        var entityId = HashUtils.ComputeMd5Hash(entityName, "ent-");

        return new EntityMergeData
        {
            EntityName = entityName,
            EntityContent = entityContent,
            NodeData = nodeData,
            EntityId = entityId
        };
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

    private static string DetermineEntityType(List<Entity> entities, List<string> alreadyEntityTypes)
    {
        var allTypes = entities.Select(e => e.Type)
            .Concat(alreadyEntityTypes)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        return allTypes
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "UNKNOWN";
    }

    private static List<string> SortAndDeduplicateDescriptions(List<Entity> entities)
    {
        // Deduplicate descriptions, keeping first occurrence
        var uniqueNodes = new Dictionary<string, Entity>();
        foreach (var entity in entities.Where(entity => !string.IsNullOrWhiteSpace(entity.Description)))
        {
            uniqueNodes.TryAdd(entity.Description, entity);
        }

        // Sort by timestamp, then by description length (descending)
        var sortedNodes = uniqueNodes.Values
            .OrderBy(e => e.Timestamp)
            .ThenByDescending(e => e.Description.Length)
            .ToList();

        return sortedNodes
            .Select(e => e.Description)
            .ToList();
    }

    private string BuildFilePath(List<string> alreadyFilePaths, List<Entity> entities, string entityName)
    {
        var filePathsList = new List<string>();
        var seenPaths = new HashSet<string>();
        var hasPlaceholder = false;

        var maxFilePaths = _options.MaxFilePaths;
        // Get placeholder from config, use default if not available
        var filePathPlaceholder = "truncated"; // DEFAULT_FILE_PATH_MORE_PLACEHOLDER

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
        foreach (var entity in entities.Where(entity => !string.IsNullOrEmpty(entity.FilePath))
                     .Where(entity => !seenPaths.Contains(entity.FilePath)))
        {
            filePathsList.Add(entity.FilePath);
            seenPaths.Add(entity.FilePath);
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
                "Limited `{EntityName}`: file_path {OriginalCount} -> {MaxCount} ({LimitMethod})",
                entityName,
                originalCountStr,
                maxFilePaths,
                limitMethod);
        }

        return string.Join(GraphFieldSep, filePathsList);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        
        if (text.Length <= maxLength)
            return text;
        
        return text[..maxLength] + "...";
    }
}