using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.DocumentDeletion;

public sealed class DocumentDeletionService
{
    private readonly IVectorStore _vectorStore;
    private readonly IGraphStore _graphStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKVStore _textChunksStore;
    private readonly IKVStore _fullDocsStore;
    private readonly IKVStore _fullEntitiesStore;
    private readonly IKVStore _fullRelationsStore;
    private readonly IKVStore _entityChunksStore;
    private readonly IKVStore _relationChunksStore;
    private readonly IKVStore _llmCacheStore;
    private readonly DocumentLifecycleService _lifecycleService;
    private readonly ILogger<DocumentDeletionService> _logger;

    public DocumentDeletionService(
        IVectorStore vectorStore,
        IGraphStore graphStore,
        IEmbeddingService embeddingService,
        [FromKeyedServices(KVContracts.TextChunks)] IKVStore textChunksStore,
        [FromKeyedServices(KVContracts.FullDocs)] IKVStore fullDocsStore,
        [FromKeyedServices(KVContracts.FullEntities)] IKVStore fullEntitiesStore,
        [FromKeyedServices(KVContracts.FullRelations)] IKVStore fullRelationsStore,
        [FromKeyedServices(KVContracts.EntityChunks)] IKVStore entityChunksStore,
        [FromKeyedServices(KVContracts.RelationChunks)] IKVStore relationChunksStore,
        [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
        DocumentLifecycleService lifecycleService,
        ILogger<DocumentDeletionService> logger)
    {
        _vectorStore = vectorStore;
        _graphStore = graphStore;
        _embeddingService = embeddingService;
        _textChunksStore = textChunksStore;
        _fullDocsStore = fullDocsStore;
        _fullEntitiesStore = fullEntitiesStore;
        _fullRelationsStore = fullRelationsStore;
        _entityChunksStore = entityChunksStore;
        _relationChunksStore = relationChunksStore;
        _llmCacheStore = llmCacheStore;
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task<DocumentDeletionResult> DeleteAsync(
        DocumentDeletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentStage = DocumentDeletionStage.PrepareDeletion;
        var collectedCacheIds = new List<string>();

        try
        {
            await _lifecycleService.MarkDeletionStartedAsync(
                request.Workspace,
                request.DocId,
                cancellationToken);

            var deletedChunkIds = request.ChunkIds.ToHashSet(StringComparer.Ordinal);

            currentStage = DocumentDeletionStage.CollectLlmCache;
            collectedCacheIds = await CollectLlmCacheIdsAsync(request.ChunkIds, cancellationToken);

            currentStage = DocumentDeletionStage.AnalyzeGraphReferences;
            var fullEntities = await _fullEntitiesStore.GetByIdAsync(request.DocId, cancellationToken);
            var fullRelations = await _fullRelationsStore.GetByIdAsync(request.DocId, cancellationToken);
            var entityNames = ReadStringList(fullEntities, "entity_names");
            var relationPairs = ReadRelationPairs(fullRelations);

            currentStage = DocumentDeletionStage.DeleteChunkVectors;
            await _vectorStore.DeleteAsync("chunks", request.ChunkIds, cancellationToken);

            currentStage = DocumentDeletionStage.DeleteTextChunks;
            await _textChunksStore.DeleteAsync(request.ChunkIds, cancellationToken);

            var relationsToDelete = new List<(string SourceId, string TargetId, string RelationKey)>();
            var relationsToUpdate = new List<(string SourceId, string TargetId, string RelationKey, IReadOnlyList<string> RemainingChunkIds)>();
            foreach (var (sourceId, targetId) in relationPairs)
            {
                var relationKey = GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId);
                var tracking = await _relationChunksStore.GetByIdAsync(relationKey, cancellationToken);
                var chunkIds = ReadStringList(tracking, "chunk_ids");
                if (chunkIds.Count > 0 && chunkIds.All(deletedChunkIds.Contains))
                {
                    relationsToDelete.Add((sourceId, targetId, relationKey));
                }
                else if (chunkIds.Count > 0)
                {
                    var remainingChunkIds = chunkIds
                        .Where(chunkId => !deletedChunkIds.Contains(chunkId))
                        .ToList();
                    if (remainingChunkIds.Count != chunkIds.Count)
                    {
                        relationsToUpdate.Add((sourceId, targetId, relationKey, remainingChunkIds));
                    }
                }
            }

            currentStage = DocumentDeletionStage.DeleteGraphRelations;
            if (relationsToDelete.Count > 0)
            {
                await _graphStore.RemoveEdgesAsync(
                    relationsToDelete.Select(relation => (relation.SourceId, relation.TargetId)).ToList(),
                    cancellationToken);
            }

            currentStage = DocumentDeletionStage.DeleteRelationVectors;
            await _vectorStore.DeleteAsync(
                "relationships",
                relationsToDelete.Select(relation => relation.RelationKey),
                cancellationToken);

            currentStage = DocumentDeletionStage.DeleteRelationTracking;
            await _relationChunksStore.DeleteAsync(
                relationsToDelete.Select(relation => relation.RelationKey),
                cancellationToken);

            currentStage = DocumentDeletionStage.UpdateGraphReferences;
            foreach (var relation in relationsToUpdate)
            {
                await UpdateRelationAsync(
                    relation.SourceId,
                    relation.TargetId,
                    relation.RelationKey,
                    relation.RemainingChunkIds,
                    deletedChunkIds,
                    stage => currentStage = stage,
                    cancellationToken);
            }

            var entitiesToDelete = new List<string>();
            var entitiesToUpdate = new List<(string EntityName, IReadOnlyList<string> RemainingChunkIds)>();
            foreach (var entityName in entityNames)
            {
                var tracking = await _entityChunksStore.GetByIdAsync(entityName, cancellationToken);
                var chunkIds = ReadStringList(tracking, "chunk_ids");
                if (chunkIds.Count > 0 && chunkIds.All(deletedChunkIds.Contains))
                {
                    entitiesToDelete.Add(entityName);
                }
                else if (chunkIds.Count > 0)
                {
                    var remainingChunkIds = chunkIds
                        .Where(chunkId => !deletedChunkIds.Contains(chunkId))
                        .ToList();
                    if (remainingChunkIds.Count != chunkIds.Count)
                    {
                        entitiesToUpdate.Add((entityName, remainingChunkIds));
                    }
                }
            }

            currentStage = DocumentDeletionStage.DeleteGraphEntities;
            foreach (var entityName in entitiesToDelete)
            {
                await _graphStore.DeleteNodeAsync(entityName, cancellationToken);
            }

            currentStage = DocumentDeletionStage.DeleteEntityVectors;
            await _vectorStore.DeleteAsync("entities", entitiesToDelete, cancellationToken);

            currentStage = DocumentDeletionStage.DeleteEntityTracking;
            await _entityChunksStore.DeleteAsync(entitiesToDelete, cancellationToken);

            currentStage = DocumentDeletionStage.UpdateGraphReferences;
            foreach (var entity in entitiesToUpdate)
            {
                await UpdateEntityAsync(
                    entity.EntityName,
                    entity.RemainingChunkIds,
                    deletedChunkIds,
                    stage => currentStage = stage,
                    cancellationToken);
            }

            if (request.DeleteLlmCache && collectedCacheIds.Count > 0)
            {
                currentStage = DocumentDeletionStage.DeleteLlmCache;
                await _llmCacheStore.DeleteAsync(collectedCacheIds, cancellationToken);
            }

            currentStage = DocumentDeletionStage.DeleteDocumentMetadata;
            await _fullDocsStore.DeleteAsync([request.DocId], cancellationToken);
            await _fullEntitiesStore.DeleteAsync([request.DocId], cancellationToken);
            await _fullRelationsStore.DeleteAsync([request.DocId], cancellationToken);

            currentStage = DocumentDeletionStage.DeleteDocStatus;
            await _lifecycleService.MarkDeletionSucceededAsync(
                request.Workspace,
                request.DocId,
                cancellationToken);

            return new DocumentDeletionResult(
                request.DocId,
                request.Workspace,
                Found: true,
                Succeeded: true,
                currentStage,
                "Document deletion completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Document deletion failed at stage {Stage} for {DocId}.",
                currentStage,
                request.DocId);

            return await _lifecycleService.MarkDeletionFailedAsync(
                request.Workspace,
                request.DocId,
                currentStage,
                ex.Message,
                collectedCacheIds,
                cancellationToken);
        }
    }

    private async Task<List<string>> CollectLlmCacheIdsAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var cacheIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunkId in chunkIds)
        {
            var chunk = await _textChunksStore.GetByIdAsync(chunkId, cancellationToken);
            foreach (var cacheId in ReadStringList(chunk, "llm_cache_list"))
            {
                if (seen.Add(cacheId))
                {
                    cacheIds.Add(cacheId);
                }
            }
        }

        return cacheIds;
    }

    private async Task UpdateEntityAsync(
        string entityName,
        IReadOnlyList<string> remainingChunkIds,
        ISet<string> deletedChunkIds,
        Action<string> setStage,
        CancellationToken cancellationToken)
    {
        setStage(DocumentDeletionStage.UpdateGraphReferences);
        var node = await _graphStore.GetNodeAsync(entityName, cancellationToken)
            ?? throw new InvalidOperationException($"Graph entity '{entityName}' was not found.");

        var updatedProperties = new Dictionary<string, object>(node.Properties, StringComparer.Ordinal)
        {
            ["source_id"] = GraphSourceReferenceParser.Join(
                GraphSourceReferenceParser.Prune(GetString(node.Properties, "source_id"), deletedChunkIds))
        };

        if (string.IsNullOrWhiteSpace(updatedProperties["source_id"].ToString()))
        {
            updatedProperties["source_id"] = GraphSourceReferenceParser.Join(remainingChunkIds);
        }

        await _graphStore.UpsertNodeAsync(entityName, updatedProperties, cancellationToken);
        setStage(DocumentDeletionStage.DeleteEntityTracking);
        await _entityChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [entityName] = new()
            {
                ["chunk_ids"] = remainingChunkIds.ToList(),
                ["count"] = remainingChunkIds.Count
            }
        }, cancellationToken);

        var description = GetString(updatedProperties, "description");
        var content = $"{entityName}\n{description}";
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
        setStage(DocumentDeletionStage.UpdateEntityVectors);
        await _vectorStore.UpsertAsync("entities", [
            new VectorDocument
            {
                Id = entityName,
                Content = content,
                Vector = embedding,
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = entityName,
                    ["entity_name"] = entityName,
                    ["source_id"] = updatedProperties["source_id"].ToString() ?? string.Empty
                }
            }
        ], cancellationToken);
    }

    private async Task UpdateRelationAsync(
        string sourceId,
        string targetId,
        string relationKey,
        IReadOnlyList<string> remainingChunkIds,
        ISet<string> deletedChunkIds,
        Action<string> setStage,
        CancellationToken cancellationToken)
    {
        setStage(DocumentDeletionStage.UpdateGraphReferences);
        var edge = await _graphStore.GetEdgeAsync(sourceId, targetId, cancellationToken)
            ?? throw new InvalidOperationException($"Graph relation '{sourceId}<->{targetId}' was not found.");

        var updatedProperties = new Dictionary<string, object>(edge.Properties, StringComparer.Ordinal)
        {
            ["source_id"] = GraphSourceReferenceParser.Join(
                GraphSourceReferenceParser.Prune(GetString(edge.Properties, "source_id"), deletedChunkIds))
        };

        if (string.IsNullOrWhiteSpace(updatedProperties["source_id"].ToString()))
        {
            updatedProperties["source_id"] = GraphSourceReferenceParser.Join(remainingChunkIds);
        }

        await _graphStore.UpsertEdgeAsync(sourceId, targetId, updatedProperties, cancellationToken);
        setStage(DocumentDeletionStage.DeleteRelationTracking);
        await _relationChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [relationKey] = new()
            {
                ["chunk_ids"] = remainingChunkIds.ToList(),
                ["count"] = remainingChunkIds.Count
            }
        }, cancellationToken);

        var keywords = GetString(updatedProperties, "keywords");
        var description = GetString(updatedProperties, "description");
        var content = $"{sourceId}\n{targetId}\n{keywords}\n{description}";
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
        setStage(DocumentDeletionStage.UpdateRelationVectors);
        await _vectorStore.UpsertAsync("relationships", [
            new VectorDocument
            {
                Id = relationKey,
                Content = content,
                Vector = embedding,
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = relationKey,
                    ["src_id"] = sourceId,
                    ["tgt_id"] = targetId,
                    ["source_id"] = updatedProperties["source_id"].ToString() ?? string.Empty,
                    ["keywords"] = keywords,
                    ["description"] = description
                }
            }
        ], cancellationToken);
    }

    private static string GetString(Dictionary<string, object> data, string key)
    {
        return data.TryGetValue(key, out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<string> ReadStringList(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            IEnumerable<string> strings => strings
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList(),
            IEnumerable<object> objects => objects
                .Select(item => item?.ToString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList(),
            string text when !string.IsNullOrWhiteSpace(text) => [text.Trim()],
            _ => []
        };
    }

    private static IReadOnlyList<(string SourceId, string TargetId)> ReadRelationPairs(Dictionary<string, object>? data)
    {
        if (data is null || !data.TryGetValue("relation_pairs", out var value))
        {
            return [];
        }

        var pairs = new List<(string SourceId, string TargetId)>();
        if (value is IEnumerable<object> objects)
        {
            foreach (var item in objects)
            {
                var values = item switch
                {
                    IEnumerable<string> strings => strings.ToList(),
                    IEnumerable<object> nestedObjects => nestedObjects
                        .Select(nested => nested?.ToString() ?? string.Empty)
                        .ToList(),
                    string relationKey => GraphSourceReferenceParser.Split(relationKey).ToList(),
                    _ => []
                };

                if (values.Count >= 2)
                {
                    pairs.Add((values[0], values[1]));
                }
            }
        }

        return pairs;
    }
}
