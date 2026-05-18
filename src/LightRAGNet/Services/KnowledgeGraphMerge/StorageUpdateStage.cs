using LightRAGNet.Core.Interfaces;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Storage update stage processor
/// Responsible for updating full_entities, full_relations, entity_chunks and relation_chunks storage
/// </summary>
internal class StorageUpdateStage(
    IKVStore? fullEntitiesStore,
    IKVStore? fullRelationsStore,
    ILogger<StorageUpdateStage> logger,
    string? docId,
    List<EntityMergeData> entityDataList,
    List<RelationMergeData> relationDataList,
    List<string> addedEntityNames)
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
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        PostTaskState(new TaskState
        {
            Stage = TaskStage.UpdatingStorage,
            Current = 0,
            Total = 0,
            Description = "Starting storage update"
        });

        // Update full_entities and full_relations storage
        // Note: entity_chunks and relation_chunks updates are already completed during merge process
        // (in EntityBuilder and RelationBuilder)
        await UpdateFullStoragesAsync(cancellationToken);

        PostTaskState(new TaskState
        {
            Stage = TaskStage.UpdatingStorage,
            Current = 0,
            Total = 0,
            Description = "Storage update completed"
        });
    }
    
    private async Task UpdateFullStoragesAsync(CancellationToken cancellationToken)
    {
        if (fullEntitiesStore == null || fullRelationsStore == null || string.IsNullOrEmpty(docId))
            return;
        
        try
        {
            // Collect all entity names (including original entities and entities added during relation processing)
            var finalEntityNames = entityDataList.Select(e => e.EntityName).ToHashSet();
            
            // Add entities added during relation processing (consistent with Python version)
            foreach (var entityName in addedEntityNames)
            {
                finalEntityNames.Add(entityName);
            }
            
            // Collect all relation pairs using the shared ordinal relation key helper.
            // Python version: edge_data.get("src_id") and edge_data.get("tgt_id")
            // But our RelationMergeData uses SourceId and TargetId, which is correct
            var finalRelationKeys = relationDataList
                .Select(r => GraphSourceReferenceParser.MakeRelationKey(r.SourceId, r.TargetId))
                .ToHashSet(StringComparer.Ordinal);
            
            logger.LogInformation(
                "Stage 3: Updating final {EntityCount} entities and {RelationCount} relations from {DocId}",
                finalEntityNames.Count,
                finalRelationKeys.Count,
                docId);
            
            // Update full_entities storage
            if (finalEntityNames.Count > 0)
            {
                await fullEntitiesStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
                {
                    [docId] = new()
                    {
                        ["entity_names"] = finalEntityNames.ToList(),
                        ["count"] = finalEntityNames.Count
                    }
                }, cancellationToken);
            }
            
            // Update full_relations storage
            if (finalRelationKeys.Count > 0)
            {
                await fullRelationsStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
                {
                    [docId] = new()
                    {
                        ["relation_pairs"] = finalRelationKeys
                            .Select(key => key.Split(GraphSourceReferenceParser.GraphFieldSep, StringSplitOptions.None))
                            .ToList(),
                        ["count"] = finalRelationKeys.Count
                    }
                }, cancellationToken);
            }
            
            logger.LogDebug(
                "Updated entity-relation index for document {DocId}: {EntityCount} entities, {RelationCount} relations",
                docId,
                finalEntityNames.Count,
                finalRelationKeys.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update entity-relation index for document {DocId}", docId);
        }
    }
}

