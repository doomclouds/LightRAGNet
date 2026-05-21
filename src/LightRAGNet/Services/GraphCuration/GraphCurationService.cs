using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentDeletion;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.GraphCuration;

public sealed class GraphCurationService
{
    private const string EntitiesCollection = "entities";
    private const string RelationshipsCollection = "relationships";
    private const string SourceSeparator = "<SEP>";
    private static readonly HashSet<string> ImmutableEntityFields = new(StringComparer.Ordinal)
    {
        "source_id",
        "file_path",
        "created_at"
    };

    private readonly IGraphStore graphStore;
    private readonly IVectorStore vectorStore;
    private readonly IEmbeddingService embeddingService;
    private readonly IKVStore textChunksStore;
    private readonly IKVStore fullEntitiesStore;
    private readonly IKVStore fullRelationsStore;
    private readonly IKVStore entityChunksStore;
    private readonly IKVStore relationChunksStore;
    private readonly Func<Task> bumpQueryRevisionAsync;
    private readonly ILogger<GraphCurationService> logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> entityLocks = new(StringComparer.Ordinal);

    public GraphCurationService(
        IGraphStore graphStore,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IKVStore textChunksStore,
        IKVStore fullEntitiesStore,
        IKVStore fullRelationsStore,
        IKVStore entityChunksStore,
        IKVStore relationChunksStore,
        Func<Task> bumpQueryRevisionAsync,
        ILogger<GraphCurationService> logger)
    {
        this.graphStore = graphStore;
        this.vectorStore = vectorStore;
        this.embeddingService = embeddingService;
        this.textChunksStore = textChunksStore;
        this.fullEntitiesStore = fullEntitiesStore;
        this.fullRelationsStore = fullRelationsStore;
        this.entityChunksStore = entityChunksStore;
        this.relationChunksStore = relationChunksStore;
        this.bumpQueryRevisionAsync = bumpQueryRevisionAsync;
        this.logger = logger;
    }

    public Task<bool> EntityExistsAsync(string entityName, CancellationToken cancellationToken = default)
    {
        return graphStore.HasNodeAsync(entityName, cancellationToken);
    }

    public async Task<GraphCurationOperationResult> CreateEntityAsync(
        GraphEntityCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var entityName = request.EntityName.Trim();
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return GraphCurationOperationResult.Failure(
                "Entity name is required.",
                "validation",
                "validation_error");
        }

        var description = GetString(request.EntityData, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            return GraphCurationOperationResult.Failure(
                "Entity description is required.",
                "validation",
                "validation_error");
        }

        return await ExecuteWithEntityLocksAsync(
            [entityName],
            () => CreateEntityCoreAsync(entityName, request, cancellationToken),
            cancellationToken);
    }

    private async Task<GraphCurationOperationResult> CreateEntityCoreAsync(
        string entityName,
        GraphEntityCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (await graphStore.HasNodeAsync(entityName, cancellationToken))
        {
            return GraphCurationOperationResult.Failure(
                $"Entity '{entityName}' already exists.",
                "graph",
                "conflict");
        }

        var nodeData = BuildEntityData(entityName, request.EntityData);
        var entityVector = await BuildEntityVectorDocumentAsync(entityName, nodeData, cancellationToken);

        await graphStore.UpsertNodeAsync(entityName, nodeData, cancellationToken);
        await UpsertEntityTrackingAsync(entityName, nodeData, cancellationToken);
        await UpsertFullEntityIndexAsync(entityName, nodeData, cancellationToken);
        await vectorStore.UpsertAsync(EntitiesCollection, [entityVector], cancellationToken);
        await bumpQueryRevisionAsync();

        return GraphCurationOperationResult.Success(
            $"Entity '{entityName}' created.",
            nodeData,
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "not_required",
                MergeError: null,
                OperationStatus: "created",
                TargetEntity: entityName,
                FinalEntity: entityName,
                Renamed: false));
    }

    public async Task<GraphCurationOperationResult> EditEntityAsync(
        GraphEntityEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.HasBlankDescription())
        {
            return GraphCurationOperationResult.Failure(
                "Entity description cannot be blank.",
                "validation",
                "validation_error");
        }

        var immutableField = request.UpdatedData.Keys.FirstOrDefault(ImmutableEntityFields.Contains);
        if (immutableField is not null)
        {
            return GraphCurationOperationResult.Failure(
                $"Entity field '{immutableField}' cannot be edited.",
                "validation",
                "validation_error");
        }

        var currentName = request.EntityName.Trim();
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return GraphCurationOperationResult.Failure(
                "Entity name is required.",
                "validation",
                "validation_error");
        }

        var requestedName = GetString(request.UpdatedData, "entity_name");
        var finalName = string.IsNullOrWhiteSpace(requestedName) ? currentName : requestedName.Trim();
        var renamed = !string.Equals(currentName, finalName, StringComparison.Ordinal);

        if (renamed && !request.AllowRename)
        {
            return GraphCurationOperationResult.Failure(
                "Entity rename is not allowed.",
                "validation",
                "rename_not_allowed");
        }

        return await ExecuteWithEntityLocksAsync(
            renamed ? [currentName, finalName] : [currentName],
            () => EditEntityCoreAsync(request, currentName, finalName, renamed, cancellationToken),
            cancellationToken);
    }

    private async Task<GraphCurationOperationResult> EditEntityCoreAsync(
        GraphEntityEditRequest request,
        string currentName,
        string finalName,
        bool renamed,
        CancellationToken cancellationToken)
    {
        var currentNode = await graphStore.GetNodeAsync(currentName, cancellationToken);
        if (currentNode is null)
        {
            return GraphCurationOperationResult.Failure(
                $"Entity '{currentName}' was not found.",
                "graph",
                "not_found");
        }

        if (renamed && await graphStore.HasNodeAsync(finalName, cancellationToken))
        {
            if (!request.AllowMerge)
            {
                return GraphCurationOperationResult.Failure(
                    $"Entity '{finalName}' already exists.",
                    "graph",
                    "conflict");
            }

            return await MergeEntitiesAsync(
                new GraphEntityMergeRequest([currentName], finalName),
                cancellationToken);
        }

        var updatedData = currentNode.Properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        foreach (var (key, value) in request.UpdatedData)
        {
            updatedData[key] = value;
        }

        updatedData["entity_id"] = finalName;
        updatedData["entity_name"] = finalName;

        var connectedEdges = renamed
            ? await GetConnectedEdgesAsync(currentName, finalName, cancellationToken)
            : [];
        var entityVector = await BuildEntityVectorDocumentAsync(finalName, updatedData, cancellationToken);
        var relationVectors = renamed
            ? await CreateRelationVectorDocumentsAsync(connectedEdges, cancellationToken)
            : [];

        if (renamed)
        {
            await graphStore.UpsertNodeAsync(finalName, updatedData, cancellationToken);
            await UpsertRewiredEdgesAsync(connectedEdges, cancellationToken);
            await graphStore.DeleteNodeAsync(currentName, cancellationToken);
            await entityChunksStore.DeleteAsync([currentName], cancellationToken);
            await DeleteOldRelationTrackingAsync(connectedEdges, cancellationToken);
            await UpsertRelationTrackingAsync(connectedEdges, cancellationToken);
            await UpsertFullEntityIndexAsync(finalName, updatedData, cancellationToken, currentName);
            await UpsertFullRelationIndexesAsync(connectedEdges, cancellationToken);
        }
        else
        {
            await graphStore.UpsertNodeAsync(finalName, updatedData, cancellationToken);
            await UpsertFullEntityIndexAsync(finalName, updatedData, cancellationToken);
        }

        await UpsertEntityTrackingAsync(finalName, updatedData, cancellationToken);
        if (renamed)
        {
            await vectorStore.DeleteAsync(EntitiesCollection, [GraphCurationVectorIds.Entity(currentName)], cancellationToken);
            await DeleteOldRelationVectorsAsync(connectedEdges, cancellationToken);
        }

        await vectorStore.UpsertAsync(EntitiesCollection, [entityVector], cancellationToken);
        if (renamed)
        {
            await UpsertRelationVectorsAsync(relationVectors, cancellationToken);
        }

        await bumpQueryRevisionAsync();

        return GraphCurationOperationResult.Success(
            $"Entity '{currentName}' updated.",
            updatedData,
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "not_required",
                MergeError: null,
                OperationStatus: "updated",
                TargetEntity: finalName,
                FinalEntity: finalName,
                Renamed: renamed));
    }

    public Task<GraphCurationOperationResult> MergeEntitiesAsync(
        GraphEntityMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GraphCurationOperationResult.Failure(
            "Entity merge is required but is not implemented in this task.",
            "merge",
            "merge_required",
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "merge_required",
                MergeError: "not_implemented",
                OperationStatus: "merge_required",
                TargetEntity: request.TargetEntity,
                FinalEntity: request.TargetEntity,
                Renamed: false)));
    }

    private async Task<VectorDocument> BuildEntityVectorDocumentAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken)
    {
        var description = GetString(entityData, "description") ?? string.Empty;
        var content = $"{entityName}\n{description}";
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        return new VectorDocument
        {
            Id = GraphCurationVectorIds.Entity(entityName),
            Content = content,
            Vector = embedding,
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = GraphCurationVectorIds.Entity(entityName),
                ["content"] = content,
                ["entity_name"] = entityName,
                ["source_id"] = GetString(entityData, "source_id") ?? string.Empty,
                ["description"] = description,
                ["entity_type"] = GetString(entityData, "entity_type") ?? string.Empty,
                ["file_path"] = GetString(entityData, "file_path") ?? string.Empty
            }
        };
    }

    private async Task UpsertEntityTrackingAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken)
    {
        var chunkIds = ExtractChunkIds(entityData);

        await entityChunksStore.UpsertAsync(
            new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                [entityName] = new(StringComparer.Ordinal)
                {
                    ["chunk_ids"] = chunkIds,
                    ["count"] = chunkIds.Count
                }
            },
            cancellationToken);
    }

    private async Task UpsertFullEntityIndexAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken,
        string? previousEntityName = null)
    {
        var docIds = await ResolveFullDocIdsAsync(entityData, cancellationToken);
        if (docIds.Count == 0)
        {
            logger.LogDebug(
                "No full_doc_id resolved for entity {EntityName}; skipping full_entities index update.",
                entityName);
            return;
        }

        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var docId in docIds)
        {
            var existing = await fullEntitiesStore.GetByIdAsync(docId, cancellationToken)
                ?? new Dictionary<string, object>(StringComparer.Ordinal);
            var entityNames = ReadStringList(existing, "entity_names").ToList();

            if (!string.IsNullOrWhiteSpace(previousEntityName))
            {
                entityNames.RemoveAll(name => string.Equals(name, previousEntityName, StringComparison.Ordinal));
            }

            if (!entityNames.Contains(entityName, StringComparer.Ordinal))
            {
                entityNames.Add(entityName);
            }

            var updated = existing.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            updated["entity_names"] = entityNames;
            updated["count"] = entityNames.Count;
            updates[docId] = updated;
        }

        await fullEntitiesStore.UpsertAsync(updates, cancellationToken);
    }

    private async Task UpsertFullRelationIndexesAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            var docIds = await ResolveFullDocIdsAsync(edge.Properties, cancellationToken);
            if (docIds.Count == 0)
            {
                logger.LogDebug(
                    "No full_doc_id resolved for relation {SourceId}->{TargetId}; skipping full_relations index update.",
                    edge.SourceId,
                    edge.TargetId);
                continue;
            }

            var oldKey = GraphSourceReferenceParser.MakeRelationKey(edge.OriginalSourceId, edge.OriginalTargetId);
            var newKey = GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId);

            foreach (var docId in docIds)
            {
                var existing = updates.TryGetValue(docId, out var pending)
                    ? pending
                    : await fullRelationsStore.GetByIdAsync(docId, cancellationToken)
                        ?? new Dictionary<string, object>(StringComparer.Ordinal);

                var relationKeys = ReadRelationPairKeys(existing, "relation_pairs").ToList();
                relationKeys.RemoveAll(key => string.Equals(key, oldKey, StringComparison.Ordinal));
                if (!relationKeys.Contains(newKey, StringComparer.Ordinal))
                {
                    relationKeys.Add(newKey);
                }

                var updated = existing.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
                updated["relation_pairs"] = relationKeys
                    .Select(key => key.Split(GraphSourceReferenceParser.GraphFieldSep, StringSplitOptions.None))
                    .ToList();
                updated["count"] = relationKeys.Count;
                updates[docId] = updated;
            }
        }

        if (updates.Count == 0)
        {
            return;
        }

        await fullRelationsStore.UpsertAsync(updates, cancellationToken);
    }

    private static Dictionary<string, object> BuildEntityData(
        string entityName,
        Dictionary<string, object> sourceData)
    {
        var entityData = sourceData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        entityData["entity_id"] = entityName;
        entityData["entity_name"] = entityName;
        entityData["entity_type"] = GetString(sourceData, "entity_type") ?? string.Empty;
        entityData["description"] = GetString(sourceData, "description") ?? string.Empty;
        entityData["source_id"] = GetString(sourceData, "source_id") ?? string.Empty;
        entityData["file_path"] = GetString(sourceData, "file_path") ?? string.Empty;
        entityData["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return entityData;
    }

    private static List<string> ExtractChunkIds(Dictionary<string, object> entityData)
    {
        var sourceId = GetString(entityData, "source_id");
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        return sourceId
            .Split(SourceSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveFullDocIdsAsync(
        Dictionary<string, object> sourceData,
        CancellationToken cancellationToken)
    {
        var docIds = new List<string>();
        foreach (var chunkId in ExtractChunkIds(sourceData))
        {
            var chunk = await textChunksStore.GetByIdAsync(chunkId, cancellationToken);
            var docId = GetString(chunk, "full_doc_id");
            if (!string.IsNullOrWhiteSpace(docId) && !docIds.Contains(docId, StringComparer.Ordinal))
            {
                docIds.Add(docId);
            }
        }

        return docIds;
    }

    private static string? GetString(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }

    private static IReadOnlyList<string> ReadStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            JsonElement json => ReadJsonStringList(json),
            IEnumerable<string> strings => strings
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            IEnumerable<object> objects => objects
                .Select(item => item?.ToString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            string text when !string.IsNullOrWhiteSpace(text) => [text.Trim()],
            _ => []
        };
    }

    private static IReadOnlyList<string> ReadJsonStringList(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Select(ReadJsonScalar)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!.Trim()],
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [value.ToString()],
            _ => []
        };
    }

    private static string ReadJsonScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => string.Empty
        };
    }

    private static IReadOnlyList<string> ReadRelationPairKeys(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        var keys = value switch
        {
            JsonElement json => ReadJsonRelationPairKeys(json),
            IEnumerable<string[]> arrays => arrays
                .Where(pair => pair.Length >= 2)
                .Select(pair => GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])),
            IEnumerable<object> objects => objects.SelectMany(ReadObjectRelationPairKeys),
            _ => []
        };

        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> ReadObjectRelationPairKeys(object? value)
    {
        switch (value)
        {
            case JsonElement json:
                return ReadJsonRelationPairKeys(json);
            case IEnumerable<string> strings:
            {
                var pair = strings.ToList();
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            case IEnumerable<object> objects:
            {
                var pair = objects.Select(item => item?.ToString() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .ToList();
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            case string text:
            {
                var pair = GraphSourceReferenceParser.Split(text);
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            default:
                return [];
        }
    }

    private static IReadOnlyList<string> ReadJsonRelationPairKeys(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var keys = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var pair = item.EnumerateArray()
                    .Select(ReadJsonScalar)
                    .Where(text => text.Length > 0)
                    .ToList();
                if (pair.Count >= 2)
                {
                    keys.Add(GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1]));
                }
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                var pair = GraphSourceReferenceParser.Split(item.GetString());
                if (pair.Count >= 2)
                {
                    keys.Add(GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1]));
                }
            }
        }

        return keys;
    }

    private async Task<List<VectorDocument>> CreateRelationVectorDocumentsAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var documents = new List<VectorDocument>();
        foreach (var edge in edges)
        {
            documents.Add(await CreateRelationVectorDocumentAsync(edge, cancellationToken));
        }

        return documents;
    }

    private async Task<VectorDocument> CreateRelationVectorDocumentAsync(
        RewiredEdge edge,
        CancellationToken cancellationToken)
    {
        var normalizedPair = GraphCurationVectorIds.NormalizePair(edge.SourceId, edge.TargetId);
        var description = GetString(edge.Properties, "description") ?? string.Empty;
        var keywords = GetString(edge.Properties, "keywords") ?? string.Empty;
        var content = $"{normalizedPair.Source}\n{normalizedPair.Target}\n{keywords}\n{description}";
        var vectorId = GraphCurationVectorIds.Relation(normalizedPair.Source, normalizedPair.Target);
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        return new VectorDocument
        {
            Id = vectorId,
            Content = content,
            Vector = embedding,
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = vectorId,
                ["content"] = content,
                ["src_id"] = normalizedPair.Source,
                ["tgt_id"] = normalizedPair.Target,
                ["source_id"] = GetString(edge.Properties, "source_id") ?? string.Empty,
                ["description"] = description,
                ["keywords"] = keywords,
                ["weight"] = GetDouble(edge.Properties, "weight"),
                ["file_path"] = GetString(edge.Properties, "file_path") ?? string.Empty
            }
        };
    }

    private async Task<List<RewiredEdge>> GetConnectedEdgesAsync(
        string currentName,
        string finalName,
        CancellationToken cancellationToken)
    {
        var edgePairs = await graphStore.GetNodeEdgesAsync(currentName, cancellationToken);
        var rewiredEdges = new List<RewiredEdge>();

        foreach (var edgePair in edgePairs)
        {
            var edge = await graphStore.GetEdgeAsync(edgePair.SourceId, edgePair.TargetId, cancellationToken);
            if (edge is null)
            {
                logger.LogWarning(
                    "Connected edge {SourceId}->{TargetId} was listed but could not be loaded during entity rename.",
                    edgePair.SourceId,
                    edgePair.TargetId);
                continue;
            }

            var rewiredSourceId = string.Equals(edgePair.SourceId, currentName, StringComparison.Ordinal)
                ? finalName
                : edgePair.SourceId;
            var rewiredTargetId = string.Equals(edgePair.TargetId, currentName, StringComparison.Ordinal)
                ? finalName
                : edgePair.TargetId;

            rewiredEdges.Add(new RewiredEdge(
                edgePair.SourceId,
                edgePair.TargetId,
                rewiredSourceId,
                rewiredTargetId,
                edge.Properties.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)));
        }

        return rewiredEdges;
    }

    private async Task UpsertRewiredEdgesAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            await graphStore.UpsertEdgeAsync(
                edge.SourceId,
                edge.TargetId,
                edge.Properties,
                cancellationToken);
        }
    }

    private async Task DeleteOldRelationVectorsAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var ids = edges
            .SelectMany(edge => GraphCurationVectorIds.RelationIds(edge.OriginalSourceId, edge.OriginalTargetId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        await vectorStore.DeleteAsync(RelationshipsCollection, ids, cancellationToken);
    }

    private async Task UpsertRelationVectorsAsync(
        IReadOnlyList<VectorDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        await vectorStore.UpsertAsync(RelationshipsCollection, documents, cancellationToken);
    }

    private async Task DeleteOldRelationTrackingAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var keys = edges
            .Select(edge => GraphSourceReferenceParser.MakeRelationKey(edge.OriginalSourceId, edge.OriginalTargetId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            return;
        }

        await relationChunksStore.DeleteAsync(keys, cancellationToken);
    }

    private async Task UpsertRelationTrackingAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var data = edges.ToDictionary(
            edge => GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId),
            edge =>
            {
                var chunkIds = ExtractChunkIds(edge.Properties);
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["chunk_ids"] = chunkIds,
                    ["count"] = chunkIds.Count
                };
            },
            StringComparer.Ordinal);

        if (data.Count == 0)
        {
            return;
        }

        await relationChunksStore.UpsertAsync(data, cancellationToken);
    }

    private static double GetDouble(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            double typed => typed,
            float typed => typed,
            decimal typed => (double)typed,
            int typed => typed,
            long typed => typed,
            _ when double.TryParse(
                value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => 0
        };
    }

    private async Task<T> ExecuteWithEntityLocksAsync<T>(
        IEnumerable<string> entityNames,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var locks = entityNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => entityLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1)))
            .ToList();

        var acquiredLocks = 0;

        try
        {
            foreach (var semaphore in locks)
            {
                await semaphore.WaitAsync(cancellationToken);
                acquiredLocks++;
            }

            return await operation();
        }
        finally
        {
            for (var i = acquiredLocks - 1; i >= 0; i--)
            {
                locks[i].Release();
            }
        }
    }

    private sealed record RewiredEdge(
        string OriginalSourceId,
        string OriginalTargetId,
        string SourceId,
        string TargetId,
        Dictionary<string, object> Properties);
}
