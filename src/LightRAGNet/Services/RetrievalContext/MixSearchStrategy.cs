using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Mix/Hybrid search strategy: combines Local and Global
/// Reference: Python version _perform_kg_search function mix/hybrid mode processing
/// </summary>
internal class MixSearchStrategy(
    IVectorStore vectorStore,
    IGraphStore graphStore,
    ILogger<MixSearchStrategy> logger,
    LocalSearchStrategy localStrategy,
    GlobalSearchStrategy globalStrategy)
    : BaseSearchStrategy(vectorStore, graphStore, logger), IKGSearchStrategy
{
    public async Task<KGSearchResult> SearchAsync(KGSearchContext context, CancellationToken cancellationToken)
    {
        var localEntities = new List<EntityData>();
        var localRelations = new List<RelationData>();
        var globalEntities = new List<EntityData>();
        var globalRelations = new List<RelationData>();

        // Local part: vector search entities
        // Reference Python version: if len(ll_keywords) > 0: local_entities, local_relations = await _get_node_data(...)
        if (!string.IsNullOrEmpty(context.LowLevelKeywords))
        {
            var localContext = new KGSearchContext
            {
                Query = context.Query,
                LowLevelKeywords = context.LowLevelKeywords,
                QueryEmbedding = context.QueryEmbedding,
                LowLevelKeywordsEmbedding = context.LowLevelKeywordsEmbedding,
                QueryParam = context.QueryParam
            };
            var localResult = await localStrategy.SearchAsync(localContext, cancellationToken);
            localEntities = localResult.Entities;
            localRelations = localResult.Relations;
        }

        // Global part: vector search relations
        // Reference Python version: if len(hl_keywords) > 0: global_relations, global_entities = await _get_edge_data(...)
        if (!string.IsNullOrEmpty(context.HighLevelKeywords))
        {
            var globalContext = new KGSearchContext
            {
                Query = context.Query,
                HighLevelKeywords = context.HighLevelKeywords,
                QueryEmbedding = context.QueryEmbedding,
                HighLevelKeywordsEmbedding = context.HighLevelKeywordsEmbedding,
                QueryParam = context.QueryParam
            };
            var globalResult = await globalStrategy.SearchAsync(globalContext, cancellationToken);
            globalEntities = globalResult.Entities;
            globalRelations = globalResult.Relations;
        }

        // Round-robin merge entities (consistent with Python version)
        // Python version: in _perform_kg_search, round-robin merge entities, return final_entities
        var finalEntities = MergeEntitiesRoundRobin(localEntities, globalEntities);
        
        // Round-robin merge relations (consistent with Python version)
        // Python version: in _perform_kg_search, round-robin merge relations, return final_relations
        var finalRelations = MergeRelationsRoundRobin(localRelations, globalRelations);

        // Log information consistent with Python version
        // Python version: logger.info(f"Raw search results: {len(final_entities)} entities, {len(final_relations)} relations, {len(vector_chunks)} vector chunks")
        // Note: vector_chunks count will be added to log in PerformKGSearchAsync
        Logger.LogInformation(
            "Raw search results: {EntityCount} entities, {RelationCount} relations",
            finalEntities.Count,
            finalRelations.Count);

        // Return merged results (consistent with Python version)
        // Python version returns final_entities and final_relations, no longer distinguishing local and global
        // For mix mode, put merged results into LocalEntities and LocalRelations
        // GlobalEntities and GlobalRelations are set to empty (because already merged)
        return new KGSearchResult
        {
            Entities = finalEntities,  // Merged entities
            Relations = finalRelations, // Merged relations
        };
    }

    /// <summary>
    /// Round-robin merge entities
    /// Reference: Python version round-robin merge entities logic
    /// </summary>
    private List<EntityData> MergeEntitiesRoundRobin(
        List<EntityData> localEntities,
        List<EntityData> globalEntities)
    {
        var finalEntities = new List<EntityData>();
        var seenEntities = new HashSet<string>();
        var maxLen = Math.Max(localEntities.Count, globalEntities.Count);

        for (int i = 0; i < maxLen; i++)
        {
            // First from local
            if (i < localEntities.Count)
            {
                var entity = localEntities[i];
                if (!string.IsNullOrEmpty(entity.Name) && seenEntities.Add(entity.Name))
                {
                    finalEntities.Add(entity);
                }
            }

            // Then from global
            if (i < globalEntities.Count)
            {
                var entity = globalEntities[i];
                if (!string.IsNullOrEmpty(entity.Name) && seenEntities.Add(entity.Name))
                {
                    finalEntities.Add(entity);
                }
            }
        }

        return finalEntities;
    }

    /// <summary>
    /// Round-robin merge relations
    /// Reference: Python version round-robin merge relations logic
    /// </summary>
    private List<RelationData> MergeRelationsRoundRobin(
        List<RelationData> localRelations,
        List<RelationData> globalRelations)
    {
        var finalRelations = new List<RelationData>();
        var seenRelations = new HashSet<(string Source, string Target)>();
        var maxLen = Math.Max(localRelations.Count, globalRelations.Count);

        for (int i = 0; i < maxLen; i++)
        {
            // First from local
            if (i < localRelations.Count)
            {
                var relation = localRelations[i];
                // Build relation unique identifier (consistent with Python version)
                var relKey = string.Compare(relation.SourceId, relation.TargetId, StringComparison.Ordinal) < 0
                    ? (relation.SourceId, relation.TargetId)
                    : (relation.TargetId, relation.SourceId);

                if (!string.IsNullOrEmpty(relKey.Item1) && !string.IsNullOrEmpty(relKey.Item2) 
                    && seenRelations.Add(relKey))
                {
                    finalRelations.Add(relation);
                }
            }

            // Then from global
            if (i < globalRelations.Count)
            {
                var relation = globalRelations[i];
                // Build relation unique identifier (consistent with Python version)
                var relKey = string.Compare(relation.SourceId, relation.TargetId, StringComparison.Ordinal) < 0
                    ? (relation.SourceId, relation.TargetId)
                    : (relation.TargetId, relation.SourceId);

                if (!string.IsNullOrEmpty(relKey.Item1) && !string.IsNullOrEmpty(relKey.Item2) 
                    && seenRelations.Add(relKey))
                {
                    finalRelations.Add(relation);
                }
            }
        }

        return finalRelations;
    }
}

