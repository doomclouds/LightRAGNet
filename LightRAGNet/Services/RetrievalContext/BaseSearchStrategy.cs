using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Base search strategy, provides common methods
/// </summary>
internal abstract class BaseSearchStrategy(
    IVectorStore vectorStore,
    IGraphStore graphStore,
    ILogger logger)
{
    protected readonly IVectorStore VectorStore = vectorStore;
    protected readonly IGraphStore GraphStore = graphStore;
    protected readonly ILogger Logger = logger;

    /// <summary>
    /// Build entity data from entity results
    /// Reference: Python version _get_node_data function node data construction logic
    /// </summary>
    protected async Task<List<EntityData>> BuildEntitiesFromResultsAsync(
        List<SearchResult> entityResults,
        CancellationToken cancellationToken)
    {
        if (entityResults.Count == 0)
            return [];

        // entity_name is the node entity_id in the graph database
        var entityIds = entityResults
            .Select(r => r.Metadata.GetValueOrDefault("entity_name")?.ToString() ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        // Concurrently get node data and degrees (consistent with Python version)
        var nodesDict = await GraphStore.GetNodesBatchAsync(entityIds, cancellationToken);
        var degreesDict = await GraphStore.GetNodeDegreesBatchAsync(entityIds, cancellationToken);

        var entityDataList = new List<EntityData>();
        foreach (var result in entityResults)
        {
            var entityName = result.Metadata.GetValueOrDefault("entity_name")?.ToString() ?? "";
            if (string.IsNullOrEmpty(entityName))
                continue;

            var node = nodesDict.GetValueOrDefault(entityName);
            var degree = degreesDict.GetValueOrDefault(entityName, 0);

            // If node does not exist, log warning (consistent with Python version)
            if (node == null)
            {
                Logger.LogWarning("Node '{EntityName}' not found in graph store, maybe the storage is damaged", entityName);
                continue;
            }

            // Get created_at from vector search results (if exists)
            var createdAt = result.Metadata.GetValueOrDefault("created_at")?.ToString();
            
            // Get file_path from node properties or vector search results
            var filePath = node.Properties.GetValueOrDefault("file_path")?.ToString()
                ?? result.Metadata.GetValueOrDefault("file_path")?.ToString();

            // Get source_id from node properties (consistent with Python version)
            var sourceId = node.Properties.GetValueOrDefault("source_id")?.ToString();

            entityDataList.Add(new EntityData
            {
                Name = entityName,
                Description = node.Properties.GetValueOrDefault("description")?.ToString()
                    ?? result.Metadata.GetValueOrDefault("description")?.ToString() ?? "",
                Type = node.Properties.GetValueOrDefault("entity_type")?.ToString()
                    ?? result.Metadata.GetValueOrDefault("entity_type")?.ToString() ?? "",
                Rank = degree,
                CreatedAt = createdAt,
                FilePath = filePath,
                SourceId = sourceId
            });
        }

        // Entities are ordered by vector search results (cosine similarity sorted)
        // Relations are sorted by rank+weight (implemented in GetRelationsFromEntitiesAsync)
        return entityDataList;
    }

    /// <summary>
    /// Get relation data from entity IDs
    /// </summary>
    protected async Task<List<RelationData>> GetRelationsFromEntitiesAsync(
        List<string> entityIds,
        CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0)
            return [];

        var edgesDict = await GraphStore.GetNodesEdgesBatchAsync(entityIds, cancellationToken);

        var allEdges = new HashSet<(string Source, string Target)>();
        foreach (var kvp in edgesDict)
        {
            foreach (var (sourceId, targetId) in kvp.Value)
            {
                var sortedEdge = string.Compare(sourceId, targetId, StringComparison.Ordinal) < 0
                    ? (sourceId, targetId)
                    : (targetId, sourceId);
                allEdges.Add(sortedEdge);
            }
        }

        if (allEdges.Count == 0)
            return [];

        var edgePairs = allEdges.ToList();
        var edgeDataDict = await GraphStore.GetEdgesBatchAsync(edgePairs, cancellationToken);
        var edgeDegreesDict = await GraphStore.GetEdgeDegreesBatchAsync(edgePairs, cancellationToken);

        return edgePairs
            .Select(pair =>
            {
                var edge = edgeDataDict.GetValueOrDefault(pair);
                var edgeDegree = edgeDegreesDict.GetValueOrDefault(pair, 0);

                if (edge == null)
                    return null;

                var weight = edge.Properties.GetValueOrDefault("weight");
                var weightValue = weight switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    _ => 1.0
                };
                
                // Get source_id from edge properties (chunk IDs)
                var rSourceId = edge.Properties.GetValueOrDefault("source_id")?.ToString() ?? "";

                return new RelationData
                {
                    SourceId = pair.Source,
                    TargetId = pair.Target,
                    Description = edge.Properties.GetValueOrDefault("description")?.ToString() ?? "",
                    Keywords = edge.Properties.GetValueOrDefault("keywords")?.ToString() ?? "",
                    Rank = edgeDegree,
                    Weight = weightValue,
                    RSourceId = rSourceId
                };
            })
            .Where(r => r != null)
            .OrderByDescending(r => r!.Rank)
            .ThenByDescending(r => r!.Weight)
            .ToList()!;
    }

    /// <summary>
    /// Build relation data from relation results
    /// Reference: Python version _get_edge_data function relation data construction logic
    /// Maintain vector search order (sorted by similarity), does not include rank
    /// </summary>
    protected async Task<List<RelationData>> BuildRelationsFromResultsAsync(
        List<SearchResult> relationResults,
        CancellationToken cancellationToken)
    {
        if (relationResults.Count == 0)
            return [];

        // Prepare edge pairs (consistent with Python version)
        // Python version: edge_pairs_dicts = [{"src": r["src_id"], "tgt": r["tgt_id"]} for r in results]
        var edgePairsDicts = relationResults
            .Select(r =>
            {
                var sourceId = r.Metadata.GetValueOrDefault("src_id")?.ToString() ?? "";
                var targetId = r.Metadata.GetValueOrDefault("tgt_id")?.ToString() ?? "";
                return (SourceId: sourceId, TargetId: targetId);
            })
            .ToList();

        // Batch get edge data (consistent with Python version)
        // Python version: edge_data_dict = await knowledge_graph_inst.get_edges_batch(edge_pairs_dicts)
        var edgePairs = edgePairsDicts
            .Select(p => string.Compare(p.SourceId, p.TargetId, StringComparison.Ordinal) < 0
                ? (p.SourceId, p.TargetId)
                : (p.TargetId, p.SourceId))
            .ToList();
        
        var edgeDataDict = await GraphStore.GetEdgesBatchAsync(edgePairs, cancellationToken);

        // Reconstruct edge data list, maintain vector search order (consistent with Python version)
        // Python version: for k in results: ... edge_datas.append(combined)
        var relationDataList = new List<RelationData>();
        foreach (var result in relationResults)
        {
            var sourceId = result.Metadata.GetValueOrDefault("src_id")?.ToString() ?? "";
            var targetId = result.Metadata.GetValueOrDefault("tgt_id")?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
                continue;

            var pair = string.Compare(sourceId, targetId, StringComparison.Ordinal) < 0
                ? (sourceId, targetId)
                : (targetId, sourceId);

            var edgeProps = edgeDataDict.GetValueOrDefault(pair);
            
            if (edgeProps != null)
            {
                // Check if weight exists, use default value 1.0 if not (consistent with Python version)
                var weight = edgeProps.Properties.GetValueOrDefault("weight");
                var weightValue = weight switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    _ => 1.0
                };

                if (weight == null)
                {
                    Logger.LogWarning(
                        "Edge ({SourceId}, {TargetId}) missing 'weight' attribute, using default value 1.0",
                        pair.Item1, pair.Item2);
                }

                // Maintain vector search order, does not include rank (consistent with Python version)
                // Python version: combined = {"src_id": k["src_id"], "tgt_id": k["tgt_id"], "created_at": k.get("created_at", None), **edge_props}
                var createdAt = result.Metadata.GetValueOrDefault("created_at")?.ToString();
                
                // Get source_id from edge properties (chunk IDs)
                var rSourceId = edgeProps.Properties.GetValueOrDefault("source_id")?.ToString() ?? "";

                relationDataList.Add(new RelationData
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    Description = edgeProps.Properties.GetValueOrDefault("description")?.ToString() ?? "",
                    Keywords = edgeProps.Properties.GetValueOrDefault("keywords")?.ToString() ?? "",
                    Rank = 0, // Relations in Global mode do not include rank, maintain vector search order
                    Weight = weightValue,
                    CreatedAt = createdAt,
                    RSourceId = rSourceId
                });
            }
        }

        // Relations maintain vector search order (sorted by similarity)
        return relationDataList;
    }

    /// <summary>
    /// Get global entities from relations
    /// Reference: Python version _find_most_related_entities_from_relationships function
    /// Only extract entities from relations (src_id and tgt_id), no multi-hop graph traversal needed
    /// </summary>
    protected async Task<List<EntityData>> GetGlobalEntitiesFromRelationsAsync(
        List<RelationData> relations,
        CancellationToken cancellationToken)
    {
        if (relations.Count == 0)
            return [];

        // Extract entity names from relations (consistent with Python version)
        // Python version: entity_names = [] ... for e in edge_datas: ... entity_names.append(e["src_id"]) ... entity_names.append(e["tgt_id"])
        var entityNames = new List<string>();
        var seen = new HashSet<string>();

        foreach (var relation in relations)
        {
            if (!string.IsNullOrEmpty(relation.SourceId) && seen.Add(relation.SourceId))
            {
                entityNames.Add(relation.SourceId);
            }
            if (!string.IsNullOrEmpty(relation.TargetId) && seen.Add(relation.TargetId))
            {
                entityNames.Add(relation.TargetId);
            }
        }

        if (entityNames.Count == 0)
            return [];

        // Only get node data, node degrees not needed (consistent with Python version)
        // Python version: nodes_dict = await knowledge_graph_inst.get_nodes_batch(entity_names)
        var nodesDict = await GraphStore.GetNodesBatchAsync(entityNames, cancellationToken);

        // Rebuild list in entity_names order (consistent with Python version)
        // Python version: for entity_name in entity_names: ... node_datas.append(combined)
        var entityDataList = new List<EntityData>();
        foreach (var entityName in entityNames)
        {
            var node = nodesDict.GetValueOrDefault(entityName);
            if (node == null)
            {
                Logger.LogWarning("Node '{EntityName}' not found in batch retrieval.", entityName);
                continue;
            }

            // Combine node data and entity name, rank not needed (consistent with Python version)
            // Python version: combined = {**node, "entity_name": entity_name}
            // Get source_id from node properties (consistent with Python version)
            var sourceId = node.Properties.GetValueOrDefault("source_id")?.ToString();

            entityDataList.Add(new EntityData
            {
                Name = entityName,
                Description = node.Properties.GetValueOrDefault("description")?.ToString() ?? "",
                Type = node.Properties.GetValueOrDefault("entity_type")?.ToString() ?? "",
                Rank = 0, // Entities in Global mode do not include rank
                SourceId = sourceId
            });
        }

        return entityDataList;
    }
}

