using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Relation merge stage processor
/// Responsible for batch processing all relation merging, embedding generation and storage
/// </summary>
internal class RelationMergeStage(
    RelationBuilder relationBuilder,
    IGraphStore graphStore,
    IVectorStore vectorStore,
    IEmbeddingService embeddingService,
    ILogger<RelationMergeStage> logger,
    Dictionary<(string Source, string Target), List<Relationship>> relations,
    IKVStore relationChunksStore,
    IKVStore entityChunksStore,
    LightRAGOptions options,
    string? docId = null)
{
    private const string GraphFieldSep = "<SEP>";
    
    /// <summary>
    /// Task state buffer (for sending state updates)
    /// </summary>
    private readonly BufferBlock<TaskState> _taskStateBuffer = new();

    /// <summary>
    /// Get task state source block (for linking data flow)
    /// </summary>
    public ISourceBlock<TaskState> GetTaskStateSource() => _taskStateBuffer;

    /// <summary>
    /// Post task state update
    /// </summary>
    private void PostTaskState(TaskState state)
    {
        state.DocId = docId;
        _taskStateBuffer.Post(state);
    }
    
    public async Task<(List<RelationMergeData> RelationDataList, List<string> AddedEntityNames)> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (relations.Count == 0)
        {
            logger.LogInformation("No relations to merge");
            PostTaskState(new TaskState
            {
                Stage = TaskStage.MergingRelations,
                Current = 0,
                Total = 0,
                Description = "No relations to merge"
            });
            return ([], []);
        }

        logger.LogInformation("Stage 2: Merging {Count} relations", relations.Count);

        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = 0,
            Total = relations.Count,
            Description = $"Starting to merge {relations.Count} relations"
        });

        // Step 1: Build all relation data
        // Note: no longer create basic nodes in advance, but create complete nodes on demand in UpdateStoragesAsync
        var relationDataList = await BuildRelationDataAsync(cancellationToken);

        if (relationDataList.Count == 0)
        {
            PostTaskState(new TaskState
            {
                Stage = TaskStage.MergingRelations,
                Current = 0,
                Total = relations.Count,
                Description = "Relation merge completed (no valid relations)"
            });
            return ([], []);
        }

        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = $"Relation data build completed: {relationDataList.Count}/{relations.Count}"
        });

        // Step 2: Batch generate embeddings (with progress percentage)
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = $"Generating relation vector embeddings: {relationDataList.Count} relations"
        });
        
        await GenerateEmbeddingsAsync(relationDataList, cancellationToken);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = $"Relation vector embeddings generated: {relationDataList.Count} relations"
        });

        // Step 3: Delete old vector records (with progress percentage)
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = "Deleting old relation vector records"
        });
        
        await DeleteOldVectorRecordsAsync(relationDataList, cancellationToken);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = "Old relation vector records deleted"
        });

        // Step 4: Batch update graph database and vector database, and create missing endpoint entities (with progress percentage)
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = $"Updating relation storage (graph database and vector database): {relationDataList.Count} relations"
        });
        
        var addedEntityNames = await UpdateStoragesAsync(relationDataList, cancellationToken);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relationDataList.Count,
            Total = relations.Count,
            Description = $"Relation storage update completed: {relationDataList.Count} relations"
        });

        logger.LogInformation("Stage 2 completed: {Count} relations merged", relationDataList.Count);

        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingRelations,
            Current = relations.Count,
            Total = relations.Count,
            Description = $"Relation merge completed: {relationDataList.Count} relations merged"
        });

        return (relationDataList, addedEntityNames);
    }

    private async Task<List<RelationMergeData>> BuildRelationDataAsync(CancellationToken cancellationToken)
    {
        var relationDataList = new List<RelationMergeData>();
        var processedCount = 0;

        foreach (var ((sourceId, targetId), lRelations) in relations)
        {
            if (lRelations.Count == 0)
                continue;

            try
            {
                var relationData = await relationBuilder.BuildAsync(
                    sourceId,
                    targetId,
                    lRelations,
                    relationChunksStore,
                    cancellationToken);
                if (relationData != null)
                {
                    relationDataList.Add(relationData);
                }
                
                processedCount++;
                PostTaskState(new TaskState
                {
                    Stage = TaskStage.MergingRelations,
                    Current = processedCount,
                    Total = relations.Count,
                    Description = $"Building relation data: {processedCount}/{relations.Count}",
                    Details = $"Relation: {sourceId} ~ {targetId}"
                });
            }
            catch (Exception ex)
            {
                // Log detailed error information, but continue processing other relations
                logger.LogError(ex, "Failed to build relation data for {SourceId}~{TargetId}. Relation will be skipped. Error: {ErrorMessage}", 
                    sourceId, targetId, ex.Message);
                // Do not rethrow exception, continue processing other relations to avoid one relation failure causing entire batch failure
                processedCount++;
            }
        }

        return relationDataList;
    }

    private async Task GenerateEmbeddingsAsync(
        List<RelationMergeData> relationDataList,
        CancellationToken cancellationToken)
    {
        var relationContents = relationDataList.Select(r => r.RelationContent).ToArray();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            relationContents,
            cancellationToken);

        for (var i = 0; i < relationDataList.Count; i++)
        {
            relationDataList[i].Embedding = embeddings[i];
        }
    }

    private async Task DeleteOldVectorRecordsAsync(
        List<RelationMergeData> relationDataList,
        CancellationToken cancellationToken)
    {
        // Collect all possible relation IDs (including forward and reverse)
        var allPossibleRelationIds = new HashSet<string>();
        foreach (var relationData in relationDataList)
        {
            allPossibleRelationIds.Add(relationData.RelationId);
            // Also include reverse ID (handle reverse records that may exist in historical data)
            var relationIdReverse = HashUtils.ComputeMd5Hash(
                relationData.TargetId + relationData.SourceId,
                "rel-");
            if (relationIdReverse != relationData.RelationId)
            {
                allPossibleRelationIds.Add(relationIdReverse);
            }
        }

        if (allPossibleRelationIds.Count == 0)
            return;

        // Batch check which vectors exist, only delete existing vectors
        var relationIdsToDelete = new List<string>();
        try
        {
            var existingRelations = await vectorStore.GetByIdsAsync(
                "relationships",
                allPossibleRelationIds,
                cancellationToken);
            var existingIds = existingRelations.Select(r => r.Id).ToHashSet();

            relationIdsToDelete.AddRange(allPossibleRelationIds.Where(relationId => existingIds.Contains(relationId)));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not check existing relationship vector records, will attempt to delete all");
            relationIdsToDelete = allPossibleRelationIds.ToList();
        }

        // Only delete existing vector records
        if (relationIdsToDelete.Count > 0)
        {
            try
            {
                await vectorStore.DeleteAsync("relationships", relationIdsToDelete, cancellationToken);
                logger.LogDebug("Deleted {Count} old relationship vector records", relationIdsToDelete.Count);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not delete old relationship vector records, continuing with update");
            }
        }
    }

    private async Task<List<string>> UpdateStoragesAsync(
        List<RelationMergeData> relationDataList,
        CancellationToken cancellationToken)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var workspaceId = string.IsNullOrEmpty(options.Workspace) ? "_" : options.Workspace;
        var addedEntityNames = new List<string>();

        // Step 1: Create missing endpoint entities (consistent with Python version)
        // Collect all entities that need to be created and their related information
        var entitiesToCreate = new Dictionary<string, (List<string> SourceIds, List<string> Descriptions, List<string> FilePaths)>();
        
        foreach (var relationData in relationDataList)
        {
            var endpointIds = new[] { relationData.SourceId, relationData.TargetId };
            foreach (var entityId in endpointIds)
            {
                var existingNode = await graphStore.GetNodeAsync(entityId, cancellationToken);
                if (existingNode == null)
                {
                    // Collect all related information for this entity (may appear in multiple relations)
                    if (!entitiesToCreate.TryGetValue(entityId, out var entityInfo))
                    {
                        entityInfo = (new List<string>(), new List<string>(), new List<string>());
                        entitiesToCreate[entityId] = entityInfo;
                    }
                    
                    // Collect source_id, description, file_path
                    var sourceIdStr = relationData.EdgeData["source_id"].ToString() ?? "";
                    if (!string.IsNullOrEmpty(sourceIdStr) && !entityInfo.SourceIds.Contains(sourceIdStr))
                    {
                        entityInfo.SourceIds.Add(sourceIdStr);
                    }
                    
                    var description = relationData.EdgeData["description"].ToString() ?? "";
                    if (!string.IsNullOrEmpty(description) && !entityInfo.Descriptions.Contains(description))
                    {
                        entityInfo.Descriptions.Add(description);
                    }
                    
                    var filePath = relationData.EdgeData["file_path"].ToString() ?? "";
                    if (!string.IsNullOrEmpty(filePath) && !entityInfo.FilePaths.Contains(filePath))
                    {
                        entityInfo.FilePaths.Add(filePath);
                    }
                }
            }
        }
        
        // Create all missing entities
        foreach (var (entityId, entityInfo) in entitiesToCreate)
        {
            var nodeCreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Merge source_ids (use first non-empty, or use first relation's)
            var sourceIdStr = entityInfo.SourceIds.Count > 0 
                ? string.Join(GraphFieldSep, entityInfo.SourceIds) 
                : "";
            
            // Merge descriptions (use first non-empty, or use entity ID as default)
            var description = entityInfo.Descriptions.Count > 0 
                ? string.Join(GraphFieldSep, entityInfo.Descriptions) 
                : entityId;
            
            // Merge file_paths
            var filePath = entityInfo.FilePaths.Count > 0 
                ? string.Join(GraphFieldSep, entityInfo.FilePaths) 
                : "";
            
            var nodeData = new Dictionary<string, object>
            {
                ["entity_id"] = entityId,
                ["source_id"] = sourceIdStr,
                ["description"] = description,
                ["entity_type"] = "UNKNOWN",
                ["file_path"] = filePath,
                ["created_at"] = nodeCreatedAt,
                ["truncate"] = ""
            };
            
            await graphStore.UpsertNodeAsync(entityId, nodeData, cancellationToken);
            
            // Collect all chunk_ids for this entity (from all related relations)
            var allChunkIds = new HashSet<string>();
            foreach (var relationData in relationDataList)
            {
                if (relationData.SourceId == entityId || relationData.TargetId == entityId)
                {
                    foreach (var chunkId in relationData.FullSourceIds.Where(id => !string.IsNullOrEmpty(id)))
                    {
                        allChunkIds.Add(chunkId);
                    }
                }
            }
            
            if (allChunkIds.Count > 0)
            {
                await entityChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
                {
                    [entityId] = new()
                    {
                        ["chunk_ids"] = allChunkIds.ToList(),
                        ["count"] = allChunkIds.Count
                    }
                }, cancellationToken);
            }
            
            // Track added entities (vectors will be generated in batch later)
            addedEntityNames.Add(entityId);
        }

        // Step 2: Update graph database (edges)
        foreach (var relationData in relationDataList)
        {
            await graphStore.UpsertEdgeAsync(
                relationData.SourceId,
                relationData.TargetId,
                relationData.EdgeData,
                cancellationToken);
        }

        // Prepare and insert new vector records
        var relationVectorDocs = relationDataList.Select(relationData => new VectorDocument
            {
                Id = relationData.RelationId,
                Vector = relationData.Embedding!,
                Content = relationData.RelationContent,
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = relationData.RelationId,
                    ["workspace_id"] = workspaceId,
                    ["created_at"] = currentTime,
                    ["content"] = relationData.RelationContent,
                    ["src_id"] = relationData.SourceId,
                    ["tgt_id"] = relationData.TargetId,
                    ["source_id"] = relationData.EdgeData["source_id"].ToString() ?? "",
                    ["file_path"] = relationData.EdgeData["file_path"].ToString() ?? "",
                    ["keywords"] = relationData.EdgeData["keywords"].ToString() ?? "",
                    ["description"] = relationData.EdgeData["description"].ToString() ?? "",
                    ["weight"] = relationData.EdgeData["weight"]
                }
            })
            .ToList();

        // Batch insert into vector database
        await vectorStore.UpsertAsync("relationships", relationVectorDocs, cancellationToken);
        
        return addedEntityNames;
    }
}