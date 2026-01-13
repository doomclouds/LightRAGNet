using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Global search strategy: vector search relations, then get detailed information from graph database
/// Reference: Python version _get_edge_data function
/// </summary>
internal class GlobalSearchStrategy(
    IVectorStore vectorStore,
    IGraphStore graphStore,
    ILogger<GlobalSearchStrategy> logger)
    : BaseSearchStrategy(vectorStore, graphStore, logger), IKGSearchStrategy
{
    public async Task<KGSearchResult> SearchAsync(KGSearchContext context, CancellationToken cancellationToken)
    {
        var globalEntities = new List<EntityData>();
        var globalRelations = new List<RelationData>();

        if (string.IsNullOrEmpty(context.HighLevelKeywords))
        {
            Logger.LogInformation("Global query: No high-level keywords provided");
            return new KGSearchResult
            {
                Entities = globalEntities,
                Relations = globalRelations
            };
        }

        // Vector search relations (using high_level_keywords)
        // Reference Python version: results = await relationships_vdb.query(keywords, top_k=query_param.top_k)
        Logger.LogInformation(
            "Query edges: {Keywords} (top_k:{TopK})",
            context.HighLevelKeywords,
            context.QueryParam.TopK);

        // Use HighLevelKeywords embedding vector for relation search
        // In Python version, if query_embedding is not provided, it will use query parameter (i.e., hl_keywords) to generate embedding vector
        var relationEmbedding = context.HighLevelKeywordsEmbedding 
            ?? throw new InvalidOperationException("HighLevelKeywordsEmbedding is required for relation search");
        
        // Vector search relations
        var relationResults = await VectorStore.QueryAsync(
            "relationships",
            context.HighLevelKeywords,
            context.QueryParam.TopK,
            relationEmbedding,
            cancellationToken: cancellationToken);

        if (relationResults.Count == 0)
        {
            Logger.LogInformation("Global query: No relations found");
            return new KGSearchResult
            {
                Entities = globalEntities,
                Relations = globalRelations
            };
        }

        // Build relation data (maintain vector search order)
        // Reference Python version: edge_datas construction logic
        globalRelations = await BuildRelationsFromResultsAsync(relationResults, cancellationToken);

        // Get related entities from relations
        // Reference Python version: use_entities = await _find_most_related_entities_from_relationships(...)
        globalEntities = await GetGlobalEntitiesFromRelationsAsync(globalRelations, cancellationToken);

        // Log information consistent with Python version
        Logger.LogInformation(
            "Global query: {EntityCount} entities, {RelationCount} relations",
            globalEntities.Count,
            globalRelations.Count);

        return new KGSearchResult
        {
            Entities = globalEntities,
            Relations = globalRelations
        };
    }
}

