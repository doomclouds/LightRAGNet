using LightRAGNet.Core.Models;

namespace LightRAGNet.Core.Interfaces;

public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class GraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public interface IGraphStore
{
    /// <summary>
    /// Check if node exists
    /// </summary>
    Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if edge exists
    /// </summary>
    Task<bool> HasEdgeAsync(string sourceNodeId, string targetNodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get node degree (number of connected edges)
    /// </summary>
    Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get node
    /// </summary>
    Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get edge
    /// </summary>
    Task<GraphEdge?> GetEdgeAsync(string sourceNodeId, string targetNodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all edges of a node
    /// </summary>
    Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(string sourceNodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Insert or update node
    /// </summary>
    Task UpsertNodeAsync(string nodeId, Dictionary<string, object> nodeData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Insert or update edge
    /// </summary>
    Task UpsertEdgeAsync(string sourceNodeId, string targetNodeId, Dictionary<string, object> edgeData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete node
    /// </summary>
    Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete edge
    /// </summary>
    Task RemoveEdgesAsync(List<(string SourceId, string TargetId)> edges, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get knowledge graph subgraph
    /// </summary>
    Task<KnowledgeGraph> GetKnowledgeGraphAsync(
        string nodeLabel,
        int maxDepth = 3,
        int maxNodes = 1000,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all labels
    /// </summary>
    Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get popular labels (sorted by node degree)
    /// </summary>
    Task<List<string>> GetPopularLabelsAsync(int limit = 300, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get nodes in batch
    /// </summary>
    Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(List<string> nodeIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get node degrees in batch
    /// </summary>
    Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(List<string> nodeIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get node edges in batch (returns list of connected node IDs for each node)
    /// </summary>
    Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(List<string> nodeIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get edge details in batch
    /// </summary>
    Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(List<(string SourceId, string TargetId)> edgePairs, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get edge degrees in batch (sum of degrees of both endpoints)
    /// </summary>
    Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(List<(string SourceId, string TargetId)> edgePairs, CancellationToken cancellationToken = default);
}

