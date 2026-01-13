using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Local search strategy: vector search entities, then get detailed information from graph database
/// Reference: Python version _get_node_data function
/// </summary>
internal class LocalSearchStrategy(
    IVectorStore vectorStore,
    IGraphStore graphStore,
    ILogger<LocalSearchStrategy> logger)
    : BaseSearchStrategy(vectorStore, graphStore, logger), IKGSearchStrategy
{
    public async Task<KGSearchResult> SearchAsync(KGSearchContext context, CancellationToken cancellationToken)
    {
        var localEntities = new List<EntityData>();
        var localRelations = new List<RelationData>();

        if (string.IsNullOrEmpty(context.LowLevelKeywords))
        {
            Logger.LogInformation("Local query: No low-level keywords provided");
            return new KGSearchResult
            {
                Entities = localEntities,
                Relations = localRelations
            };
        }

        // Vector search entities (using low_level_keywords)
        // Reference Python version: results = await entities_vdb.query(query, top_k=query_param.top_k)
        Logger.LogInformation(
            "Query nodes: {Query} (top_k:{TopK})",
            context.LowLevelKeywords,
            context.QueryParam.TopK);

        // Use LowLevelKeywords embedding vector for entity search
        // Reference Python version: results = await entities_vdb.query(ll_keywords, top_k=query_param.top_k)
        // In Python version, if query_embedding is not provided, it will use query parameter (i.e., ll_keywords) to generate embedding vector
        var entityEmbedding = context.LowLevelKeywordsEmbedding 
            ?? throw new InvalidOperationException("LowLevelKeywordsEmbedding is required for entity search");
        
        var entityResults = await VectorStore.QueryAsync(
            "entities",
            context.LowLevelKeywords,
            context.QueryParam.TopK,
            entityEmbedding,
            cancellationToken: cancellationToken);

        if (entityResults.Count == 0)
        {
            Logger.LogInformation("Local query: No entities found");
            return new KGSearchResult
            {
                Entities = localEntities,
                Relations = localRelations
            };
        }

        // Build entity data (including entity_name, rank, created_at, etc.)
        // Reference Python version: node_datas construction logic
        localEntities = await BuildEntitiesFromResultsAsync(entityResults, cancellationToken);

        // Get all relations of these entities
        // Reference Python version: use_relations = await _find_most_related_edges_from_entities(...)
        var entityIds = localEntities.Select(e => e.Name).ToList();
        localRelations = await GetRelationsFromEntitiesAsync(entityIds, cancellationToken);

        // Log information consistent with Python version
        Logger.LogInformation(
            "Local query: {EntityCount} entities, {RelationCount} relations",
            localEntities.Count,
            localRelations.Count);

        // Entities sorted by cosine similarity (vector search results are already sorted)
        // Relations sorted by rank+weight (implemented in GetRelationsFromEntitiesAsync)
        return new KGSearchResult
        {
            Entities = localEntities,
            Relations = localRelations
        };
    }
}

