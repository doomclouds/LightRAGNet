using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Models;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Entity merge stage processor
/// Responsible for batch processing all entity merging, embedding generation and storage
/// </summary>
internal class EntityMergeStage(
    EntityBuilder entityBuilder,
    IGraphStore graphStore,
    IVectorStore vectorStore,
    IEmbeddingService embeddingService,
    ILogger<EntityMergeStage> logger,
    Dictionary<string, List<Entity>> entities,
    IKVStore entityChunksStore,
    LightRAGOptions options,
    string? docId = null)
{
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
    public async Task<List<EntityMergeData>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
        {
            logger.LogInformation("No entities to merge");
            PostTaskState(new TaskState
            {
                Stage = TaskStage.MergingEntities,
                Current = 0,
                Total = 0,
                Description = "No entities to merge"
            });
            return [];
        }
        
        logger.LogInformation("Stage 1: Merging {Count} entities", entities.Count);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = 0,
            Total = entities.Count,
            Description = $"Starting to merge {entities.Count} entities"
        });
        
        // Step 1: Build all entity data
        var entityDataList = await BuildEntityDataAsync(cancellationToken);
        
        if (entityDataList.Count == 0)
        {
            PostTaskState(new TaskState
            {
                Stage = TaskStage.MergingEntities,
                Current = 0,
                Total = entities.Count,
                Description = "Entity merge completed (no valid entities)"
            });
            return [];
        }
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entityDataList.Count,
            Total = entities.Count,
            Description = $"Entity data build completed: {entityDataList.Count}/{entities.Count}"
        });
        
        // Step 2: Batch generate embeddings (with progress percentage)
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entityDataList.Count,
            Total = entities.Count,
            Description = $"Generating entity vector embeddings: {entityDataList.Count} entities"
        });
        
        await GenerateEmbeddingsAsync(entityDataList, cancellationToken);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entityDataList.Count,
            Total = entities.Count,
            Description = $"Entity vector embeddings generated: {entityDataList.Count} entities"
        });
        
        // Step 3: Batch update graph database and vector database (with progress percentage)
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entityDataList.Count,
            Total = entities.Count,
            Description = $"Updating entity storage (graph database and vector database): {entityDataList.Count} entities"
        });
        
        await UpdateStoragesAsync(entityDataList, cancellationToken);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entityDataList.Count,
            Total = entities.Count,
            Description = $"Entity storage update completed: {entityDataList.Count} entities"
        });
        
        logger.LogInformation("Stage 1 completed: {Count} entities merged", entityDataList.Count);
        
        PostTaskState(new TaskState
        {
            Stage = TaskStage.MergingEntities,
            Current = entities.Count,
            Total = entities.Count,
            Description = $"Entity merge completed: {entityDataList.Count} entities merged"
        });
        
        return entityDataList;
    }
    
    private async Task<List<EntityMergeData>> BuildEntityDataAsync(CancellationToken cancellationToken)
    {
        var entityDataList = new List<EntityMergeData>();
        var processedCount = 0;
        
        foreach (var (entityName, lEntities) in entities)
        {
            if (lEntities.Count == 0)
                continue;
            
            try
            {
                var entityData = await entityBuilder.BuildAsync(entityName, lEntities, entityChunksStore, cancellationToken);
                if (entityData != null)
                {
                    entityDataList.Add(entityData);
                }
                
                processedCount++;
                PostTaskState(new TaskState
                {
                    Stage = TaskStage.MergingEntities,
                    Current = processedCount,
                    Total = entities.Count,
                    Description = $"Building entity data: {processedCount}/{entities.Count}",
                    Details = $"Entity: {entityName}"
                });
            }
            catch (Exception ex)
            {
                // Log detailed error information, but continue processing other entities
                logger.LogError(ex, "Failed to build entity data for {EntityName}. Entity will be skipped. Error: {ErrorMessage}", 
                    entityName, ex.Message);
                // Do not rethrow exception, continue processing other entities to avoid one entity failure causing entire batch failure
                processedCount++;
            }
        }
        
        return entityDataList;
    }
    
    private async Task GenerateEmbeddingsAsync(
        List<EntityMergeData> entityDataList,
        CancellationToken cancellationToken)
    {
        var entityContents = entityDataList.Select(e => e.EntityContent).ToArray();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            entityContents,
            cancellationToken);
        
        for (var i = 0; i < entityDataList.Count; i++)
        {
            entityDataList[i].Embedding = embeddings[i];
        }
    }
    
    private async Task UpdateStoragesAsync(
        List<EntityMergeData> entityDataList,
        CancellationToken cancellationToken)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var workspaceId = string.IsNullOrEmpty(options.Workspace) ? "_" : options.Workspace;
        
        var entityVectorDocs = new List<VectorDocument>();
        
        foreach (var entityData in entityDataList)
        {
            // Update graph database
            await graphStore.UpsertNodeAsync(
                entityData.EntityName,
                entityData.NodeData,
                cancellationToken);
            
            // Prepare vector documents
            entityVectorDocs.Add(new VectorDocument
            {
                Id = entityData.EntityId,
                Vector = entityData.Embedding!,
                Content = entityData.EntityContent,
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = entityData.EntityId,
                    ["workspace_id"] = workspaceId,
                    ["created_at"] = currentTime,
                    ["content"] = entityData.EntityContent,
                    ["entity_name"] = entityData.EntityName,
                    ["source_id"] = entityData.NodeData["source_id"].ToString() ?? "",
                    ["file_path"] = entityData.NodeData["file_path"].ToString() ?? ""
                }
            });
        }
        
        // Batch insert into vector database
        await vectorStore.UpsertAsync("entities", entityVectorDocs, cancellationToken);
    }
}

