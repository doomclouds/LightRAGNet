using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace LightRAGNet.Storage;

/// <summary>
/// Neo4j graph store implementation
/// Reference: Python version kg/neo4j_impl.py
/// </summary>
public class Neo4JGraphStore : IGraphStore
{
    private readonly IDriver _driver;
    private readonly string _workspaceLabel; // Workspace label for node isolation
    
    public Neo4JGraphStore(
        IDriver driver,
        ILogger<Neo4JGraphStore> logger,
        IOptions<Neo4JOptions> options)
    {
        _driver = driver;
        
        _workspaceLabel = options.Value.Workspace ?? "base";
        
        logger.LogInformation("Using Neo4j default database (Community Edition mode) with workspace: {Workspace}", _workspaceLabel);
    }
    
    /// <summary>
    /// Create a session using the default database
    /// </summary>
    private IAsyncSession CreateSession()
    {
        return _driver.AsyncSession();
    }
    
    public async Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id
        var query = $"MATCH (n:`{_workspaceLabel}` {{entity_id: $entity_id}}) RETURN n LIMIT 1";
        var result = await session.RunAsync(query, new { entity_id = nodeId });
        
        return await result.FetchAsync();
    }
    
    public async Task<bool> HasEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id, DIRECTED relationship type
        var query = $$"""
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: $source_entity_id})-[r:DIRECTED]-(b:`{{_workspaceLabel}}` {entity_id: $target_entity_id})
                      RETURN r LIMIT 1
                      """;
        var result = await session.RunAsync(query, new 
        { 
            source_entity_id = sourceNodeId, 
            target_entity_id = targetNodeId 
        });
        
        return await result.FetchAsync();
    }
    
    public async Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id
        var query = $"MATCH (n:`{_workspaceLabel}` {{entity_id: $entity_id}})-[r]-() RETURN count(r) as degree";
        var result = await session.RunAsync(query, new { entity_id = nodeId });
        
        var record = await result.SingleAsync(cancellationToken: cancellationToken);
        return record["degree"].As<int>();
    }
    
    public async Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id
        var query = $"MATCH (n:`{_workspaceLabel}` {{entity_id: $entity_id}}) RETURN n";
        var result = await session.RunAsync(query, new { entity_id = nodeId });
        
        var record = await result.SingleOrDefaultAsync(cancellationToken: cancellationToken);
        if (record == null)
            return null;
        
        var node = record["n"].As<INode>();
        // Get entity_id from node properties as Id
        var entityId = node.Properties.TryGetValue("entity_id", out var eid) 
            ? eid?.ToString() ?? nodeId 
            : nodeId;
        
        return new GraphNode
        {
            Id = entityId,
            Properties = node.Properties.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertValue(kvp.Value))
        };
    }
    
    public async Task<GraphEdge?> GetEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id, DIRECTED relationship type
        var query = $$"""
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: $source_entity_id})-[r:DIRECTED]-(b:`{{_workspaceLabel}}` {entity_id: $target_entity_id})
                      RETURN r, type(r) as relType LIMIT 1
                      """;
        var result = await session.RunAsync(query, new 
        { 
            source_entity_id = sourceNodeId, 
            target_entity_id = targetNodeId 
        });
        
        var record = await result.SingleOrDefaultAsync(cancellationToken: cancellationToken);
        if (record == null)
            return null;
        
        var relationship = record["r"].As<IRelationship>();
        return new GraphEdge
        {
            SourceId = sourceNodeId,
            TargetId = targetNodeId,
            Properties = relationship.Properties.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertValue(kvp.Value))
        };
    }
    
    public async Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
        string sourceNodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id, DIRECTED relationship type
        var query = $$"""
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: $entity_id})-[r:DIRECTED]-(b:`{{_workspaceLabel}}`)
                      RETURN b.entity_id as targetId
                      """;
        var result = await session.RunAsync(query, new { entity_id = sourceNodeId });
        
        var edges = new List<(string, string)>();
        await foreach (var record in result)
        {
            var targetId = record["targetId"].As<string>();
            edges.Add((sourceNodeId, targetId));
        }
        
        return edges;
    }
    
    public async Task UpsertNodeAsync(
        string nodeId,
        Dictionary<string, object> nodeData,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Reference Python version: use workspace_label and entity_type as labels, entity_id as identifier
        nodeData.TryAdd("entity_id", nodeId);
        
        // Get entity_type (required)
        var entityType = nodeData.TryGetValue("entity_type", out var type) 
            ? type.ToString() ?? "Entity" 
            : "Entity";
        
        var properties = new Dictionary<string, object>();
        
        if (nodeData.TryGetValue("entity_id", out var entityIdValue))
        {
            properties["entity_id"] = entityIdValue;
        }
        
        if (nodeData.TryGetValue("entity_type", out var entityTypeValue))
        {
            properties["entity_type"] = entityTypeValue;
        }
        
        if (nodeData.TryGetValue("description", out var description))
        {
            properties["description"] = description;
        }
        
        if (nodeData.TryGetValue("source_id", out var sourceIdValue))
        {
            properties["source_id"] = sourceIdValue;
        }
        
        if (nodeData.TryGetValue("file_path", out var filePathValue))
        {
            properties["file_path"] = filePathValue;
        }
        
        if (nodeData.TryGetValue("created_at", out var createdAtValue))
        {
            properties["created_at"] = createdAtValue;
        }
        
        if (nodeData.TryGetValue("truncate", out var truncateValue))
        {
            properties["truncate"] = truncateValue;
        }
        
        foreach (var kvp in nodeData.Where(kvp => !properties.ContainsKey(kvp.Key)))
        {
            properties[kvp.Key] = kvp.Value;
        }
        
        // Python version query: MERGE (n:`{workspace_label}` {{entity_id: $entity_id}}) SET n += $properties SET n:`{entity_type}`
        var query = $$"""
                      MERGE (n:`{{_workspaceLabel}}` {entity_id: $entity_id})
                      SET n += $properties
                      SET n:`{{entityType}}`
                      """;
        
        await session.RunAsync(query, new 
        { 
            entity_id = nodeId, 
            properties 
        });
    }
    
    public async Task UpsertEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        Dictionary<string, object> edgeData,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Ensure nodes exist (using entity_id)
        await UpsertNodeAsync(sourceNodeId, new Dictionary<string, object> 
        { 
            ["entity_id"] = sourceNodeId 
        }, cancellationToken);
        await UpsertNodeAsync(targetNodeId, new Dictionary<string, object> 
        { 
            ["entity_id"] = targetNodeId 
        }, cancellationToken);
        
        // Reference Python version: use DIRECTED relationship type, match nodes using entity_id
        // Python version query: MATCH (source:`{workspace_label}` {{entity_id: $source_entity_id}}) WITH source MATCH (target:`{workspace_label}` {{entity_id: $target_entity_id}}) MERGE (source)-[r:DIRECTED]-(target) SET r += $properties
        var query = $$"""
                      MATCH (source:`{{_workspaceLabel}}` {entity_id: $source_entity_id})
                      WITH source
                      MATCH (target:`{{_workspaceLabel}}` {entity_id: $target_entity_id})
                      MERGE (source)-[r:DIRECTED]-(target)
                      SET r += $properties
                      RETURN r, source, target
                      """;
        
        await session.RunAsync(query, new 
        { 
            source_entity_id = sourceNodeId, 
            target_entity_id = targetNodeId,
            properties = edgeData 
        });
    }
    
    public async Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id
        var query = $"MATCH (n:`{_workspaceLabel}` {{entity_id: $entity_id}}) DETACH DELETE n";
        await session.RunAsync(query, new { entity_id = nodeId });
    }
    
    public async Task RemoveEdgesAsync(
        List<(string SourceId, string TargetId)> edges,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Match nodes using workspace_label and entity_id, DIRECTED relationship type
        var query = $$"""
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: $source_entity_id})-[r:DIRECTED]-(b:`{{_workspaceLabel}}` {entity_id: $target_entity_id})
                      DELETE r
                      """;
        
        foreach (var (sourceId, targetId) in edges)
        {
            await session.RunAsync(query, new 
            { 
                source_entity_id = sourceId, 
                target_entity_id = targetId 
            });
        }
    }
    
    public async Task<KnowledgeGraph> GetKnowledgeGraphAsync(
        string nodeLabel,
        int maxDepth = 3,
        int maxNodes = 1000,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Use workspace_label and entity_id
        var query = nodeLabel == "*"
            ? $"""
               MATCH path = (start:`{_workspaceLabel}`)-[*1..{maxDepth}]-(connected:`{_workspaceLabel}`)
               WHERE start.entity_id IS NOT NULL
               WITH start, connected, path
               LIMIT {maxNodes}
               RETURN DISTINCT start, connected
               """
            : $"""
               MATCH (start:`{_workspaceLabel}`)
               WHERE start.entity_id CONTAINS $label OR start.entity_type = $label
               MATCH path = (start)-[*1..{maxDepth}]-(connected:`{_workspaceLabel}`)
               WITH start, connected, path
               LIMIT {maxNodes}
               RETURN DISTINCT start, connected
               """;
        
        var parameters = nodeLabel == "*" 
            ? new Dictionary<string, object>() 
            : new Dictionary<string, object> { { "label", nodeLabel } };
        
        var result = await session.RunAsync(query, parameters);
        
        var nodes = new Dictionary<string, KnowledgeGraphNode>();
        var edges = new HashSet<(string Source, string Target)>();
        var nodeCount = 0;
        
        await foreach (var record in result)
        {
            if (nodeCount >= maxNodes)
            {
                return new KnowledgeGraph
                {
                    Nodes = nodes.Values.ToList(),
                    Edges = edges.Select(e => new KnowledgeGraphEdge
                    {
                        Source = e.Source,
                        Target = e.Target
                    }).ToList(),
                    IsTruncated = true
                };
            }
            
            var startNode = record["start"].As<INode>();
            var connectedNode = record["connected"].As<INode>();
            
            // Use entity_id as node ID
            var startId = startNode.Properties.TryGetValue("entity_id", out var sid) 
                ? sid?.ToString() ?? "" 
                : "";
            var connectedId = connectedNode.Properties.TryGetValue("entity_id", out var cid) 
                ? cid?.ToString() ?? "" 
                : "";
            
            if (string.IsNullOrEmpty(startId) || string.IsNullOrEmpty(connectedId))
                continue;
            
            if (!nodes.ContainsKey(startId))
            {
                nodes[startId] = new KnowledgeGraphNode
                {
                    Id = startId,
                    Labels = startNode.Labels.ToList(),
                    Properties = startNode.Properties.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ConvertValue(kvp.Value))
                };
                nodeCount++;
            }
            
            if (!nodes.ContainsKey(connectedId))
            {
                nodes[connectedId] = new KnowledgeGraphNode
                {
                    Id = connectedId,
                    Labels = connectedNode.Labels.ToList(),
                    Properties = connectedNode.Properties.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ConvertValue(kvp.Value))
                };
                nodeCount++;
            }
            
            // Normalize edge direction: ensure source ID is less than target ID (undirected graph consistency)
            // This avoids (A, B) and (B, A) being identified as different edges
            var normalizedEdge = string.Compare(startId, connectedId, StringComparison.Ordinal) < 0
                ? (startId, connectedId)
                : (connectedId, startId);
            
            edges.Add(normalizedEdge);
        }
        
        return new KnowledgeGraph
        {
            Nodes = nodes.Values.ToList(),
            Edges = edges.Select(e => new KnowledgeGraphEdge
            {
                Source = e.Source,
                Target = e.Target
            }).ToList(),
            IsTruncated = false
        };
    }
    
    public async Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Filter nodes using workspace_label
        var query = $"MATCH (n:`{_workspaceLabel}`) RETURN DISTINCT labels(n) as labels";
        var result = await session.RunAsync(query);
        
        var labels = new HashSet<string>();
        await foreach (var record in result)
        {
            var labelList = record["labels"].As<List<string>>();
            foreach (var label in labelList)
            {
                // Exclude workspace_label itself
                if (label != _workspaceLabel)
                {
                    labels.Add(label);
                }
            }
        }
        
        return labels.OrderBy(l => l).ToList();
    }
    
    public async Task<List<string>> GetPopularLabelsAsync(
        int limit = 300,
        CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession();
        
        // Filter nodes using workspace_label
        var query = $"""
                     MATCH (n:`{_workspaceLabel}`)
                     UNWIND labels(n) as label
                     WHERE label <> '{_workspaceLabel}'
                     WITH label, count(*) as degree
                     ORDER BY degree DESC
                     LIMIT $limit
                     RETURN label
                     """;
        var result = await session.RunAsync(query, new { limit });
        
        var labels = new List<string>();
        await foreach (var record in result)
        {
            labels.Add(record["label"].As<string>());
        }
        
        return labels;
    }
    
    private static object ConvertValue(object value)
    {
        return value switch
        {
            string s => s,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            bool b => b,
            List<object> list => list.Select(ConvertValue).ToList(),
            Dictionary<string, object> dict => dict.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertValue(kvp.Value)),
            _ => value.ToString() ?? string.Empty
        };
    }
    
    public async Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        if (nodeIds.Count == 0)
            return new Dictionary<string, GraphNode>();
        
        await using var session = CreateSession();
        
        // Batch query nodes
        var query = $$"""
                      UNWIND $entity_ids AS entity_id
                      MATCH (n:`{{_workspaceLabel}}` {entity_id: entity_id})
                      RETURN n.entity_id AS id, n
                      """;
        
        var result = await session.RunAsync(query, new { entity_ids = nodeIds.ToArray() });
        
        var nodes = new Dictionary<string, GraphNode>();
        await foreach (var record in result)
        {
            var id = record["id"].As<string>();
            var node = record["n"].As<INode>();
            
            nodes[id] = new GraphNode
            {
                Id = id,
                Properties = node.Properties.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertValue(kvp.Value))
            };
        }
        
        return nodes;
    }
    
    public async Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        if (nodeIds.Count == 0)
            return new Dictionary<string, int>();
        
        await using var session = CreateSession();
        
        // Batch query node degrees
        var query = $$"""
                      UNWIND $entity_ids AS entity_id
                      MATCH (n:`{{_workspaceLabel}}` {entity_id: entity_id})-[r]-()
                      RETURN n.entity_id AS id, count(r) AS degree
                      """;
        
        var result = await session.RunAsync(query, new { entity_ids = nodeIds.ToArray() });
        
        var degrees = new Dictionary<string, int>();
        await foreach (var record in result)
        {
            var id = record["id"].As<string>();
            var degree = record["degree"].As<long>();
            degrees[id] = (int)degree;
        }
        
        // Ensure all nodes have degrees (even if 0)
        foreach (var nodeId in nodeIds)
        {
            degrees.TryAdd(nodeId, 0);
        }
        
        return degrees;
    }
    
    public async Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        if (nodeIds.Count == 0)
            return new Dictionary<string, List<(string, string)>>();
        
        await using var session = CreateSession();
        
        // Batch query node edges
        var query = $$"""
                      UNWIND $entity_ids AS entity_id
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: entity_id})-[r:DIRECTED]-(b:`{{_workspaceLabel}}`)
                      RETURN a.entity_id AS sourceId, b.entity_id AS targetId
                      """;
        
        var result = await session.RunAsync(query, new { entity_ids = nodeIds.ToArray() });
        
        var edgesDict = new Dictionary<string, List<(string, string)>>();
        
        // Initialize edge lists for all nodes
        foreach (var nodeId in nodeIds)
        {
            edgesDict[nodeId] = [];
        }
        
        await foreach (var record in result)
        {
            var sourceId = record["sourceId"].As<string>();
            var targetId = record["targetId"].As<string>();
            
            if (edgesDict.ContainsKey(sourceId))
            {
                edgesDict[sourceId].Add((sourceId, targetId));
            }
        }
        
        return edgesDict;
    }
    
    public async Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        if (edgePairs.Count == 0)
            return new Dictionary<(string, string), GraphEdge>();
        
        await using var session = CreateSession();
        
        // Build edge pair list for query
        var edgePairsList = edgePairs.Select(e => new { source = e.SourceId, target = e.TargetId }).ToList();
        
        // Batch query edges
        var query = $$"""
                      UNWIND $edge_pairs AS pair
                      MATCH (a:`{{_workspaceLabel}}` {entity_id: pair.source})-[r:DIRECTED]-(b:`{{_workspaceLabel}}` {entity_id: pair.target})
                      RETURN a.entity_id AS sourceId, b.entity_id AS targetId, r
                      """;
        
        var result = await session.RunAsync(query, new { edge_pairs = edgePairsList });
        
        var edges = new Dictionary<(string, string), GraphEdge>();
        
        // Create normalized key mapping to match incoming edgePairs
        var normalizedKeyMap = edgePairs.ToDictionary(
            pair => string.Compare(pair.SourceId, pair.TargetId, StringComparison.Ordinal) < 0
                ? (pair.SourceId, pair.TargetId)
                : (pair.TargetId, pair.SourceId),
            pair => pair);
        
        await foreach (var record in result)
        {
            var sourceId = record["sourceId"].As<string>();
            var targetId = record["targetId"].As<string>();
            var relationship = record["r"].As<IRelationship>();
            
            // Normalize edge key returned from query
            var normalizedKey = string.Compare(sourceId, targetId, StringComparison.Ordinal) < 0
                ? (sourceId, targetId)
                : (targetId, sourceId);
            
            // Use original incoming key if exists, otherwise use normalized key
            var originalKey = normalizedKeyMap.GetValueOrDefault(normalizedKey, normalizedKey);
            
            // Skip if already exists (avoid overwriting)
            if (!edges.ContainsKey(originalKey))
            {
                var (srcId, tgtId) = originalKey;
                edges[originalKey] = new GraphEdge
                {
                    SourceId = srcId,
                    TargetId = tgtId,
                    Properties = relationship.Properties.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ConvertValue(kvp.Value))
                };
            }
        }
        
        return edges;
    }
    
    public async Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        if (edgePairs.Count == 0)
            return new Dictionary<(string, string), int>();
        
        // Reference Python version: collect all unique node IDs, then call node_degrees_batch
        var uniqueNodeIds = new HashSet<string>();
        foreach (var (sourceId, targetId) in edgePairs)
        {
            uniqueNodeIds.Add(sourceId);
            uniqueNodeIds.Add(targetId);
        }
        
        // Batch get degrees for all nodes
        var nodeDegrees = await GetNodeDegreesBatchAsync(uniqueNodeIds.ToList(), cancellationToken);
        
        // For each edge pair, sum the degrees of both endpoints
        var edgeDegrees = new Dictionary<(string, string), int>();
        foreach (var (sourceId, targetId) in edgePairs)
        {
            var sourceDegree = nodeDegrees.GetValueOrDefault(sourceId, 0);
            var targetDegree = nodeDegrees.GetValueOrDefault(targetId, 0);
            edgeDegrees[(sourceId, targetId)] = sourceDegree + targetDegree;
        }
        
        return edgeDegrees;
    }
}

/// <summary>
/// Neo4j configuration options
/// </summary>
public class Neo4JOptions
{
    /// <summary>
    /// Neo4j connection URI
    /// </summary>
    public string Uri { get; set; } = "neo4j://localhost:7687";
    
    /// <summary>
    /// Neo4j username
    /// </summary>
    public string User { get; set; } = "neo4j";
    
    /// <summary>
    /// Neo4j password
    /// </summary>
    public string Password { get; set; } = "password";
    
    /// <summary>
    /// Workspace label (optional, defaults to "base" if empty)
    /// </summary>
    public string? Workspace { get; set; }
}

