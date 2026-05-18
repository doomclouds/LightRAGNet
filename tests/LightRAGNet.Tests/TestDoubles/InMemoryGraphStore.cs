using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.DocumentDeletion;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryGraphStore : IGraphStore
{
    private readonly Dictionary<string, GraphNode> nodes = [];
    private readonly Dictionary<string, GraphEdge> edges = [];

    public List<string> DeletedNodes { get; } = [];
    public List<(string SourceId, string TargetId)> DeletedEdges { get; } = [];

    public void SeedNode(string nodeId, Dictionary<string, object> properties)
    {
        nodes[nodeId] = new GraphNode
        {
            Id = nodeId,
            Properties = Clone(properties)
        };
    }

    public void SeedEdge(string sourceId, string targetId, Dictionary<string, object> properties)
    {
        edges[GetEdgeKey(sourceId, targetId)] = new GraphEdge
        {
            SourceId = sourceId,
            TargetId = targetId,
            Properties = Clone(properties)
        };
    }

    public GraphNode? GetSeededNode(string nodeId)
    {
        return nodes.TryGetValue(nodeId, out var node) ? Clone(node) : null;
    }

    public GraphEdge? GetSeededEdge(string sourceId, string targetId)
    {
        return edges.TryGetValue(GetEdgeKey(sourceId, targetId), out var edge) ? Clone(edge) : null;
    }

    public Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodes.ContainsKey(nodeId));
    }

    public Task<bool> HasEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(edges.ContainsKey(GetEdgeKey(sourceNodeId, targetNodeId)));
    }

    public Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var degree = edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId);
        return Task.FromResult(degree);
    }

    public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetSeededNode(nodeId));
    }

    public Task<GraphEdge?> GetEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetSeededEdge(sourceNodeId, targetNodeId));
    }

    public Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
        string sourceNodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodeEdges = edges.Values
            .Where(edge => edge.SourceId == sourceNodeId || edge.TargetId == sourceNodeId)
            .Select(edge => (edge.SourceId, edge.TargetId))
            .ToList();

        return Task.FromResult(nodeEdges);
    }

    public Task UpsertNodeAsync(
        string nodeId,
        Dictionary<string, object> nodeData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nodes[nodeId] = new GraphNode
        {
            Id = nodeId,
            Properties = Clone(nodeData)
        };

        return Task.CompletedTask;
    }

    public Task UpsertEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        Dictionary<string, object> edgeData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        edges[GetEdgeKey(sourceNodeId, targetNodeId)] = new GraphEdge
        {
            SourceId = sourceNodeId,
            TargetId = targetNodeId,
            Properties = Clone(edgeData)
        };

        return Task.CompletedTask;
    }

    public Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DeletedNodes.Add(nodeId);
        nodes.Remove(nodeId);

        var connectedEdges = edges.Values
            .Where(edge => edge.SourceId == nodeId || edge.TargetId == nodeId)
            .Select(edge => (edge.SourceId, edge.TargetId))
            .ToList();

        foreach (var (sourceId, targetId) in connectedEdges)
        {
            edges.Remove(GetEdgeKey(sourceId, targetId));
        }

        return Task.CompletedTask;
    }

    public Task RemoveEdgesAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var (sourceId, targetId) in edgePairs)
        {
            DeletedEdges.Add((sourceId, targetId));
            edges.Remove(GetEdgeKey(sourceId, targetId));
        }

        return Task.CompletedTask;
    }

    public Task<KnowledgeGraph> GetKnowledgeGraphAsync(
        string nodeLabel,
        int maxDepth = 3,
        int maxNodes = 1000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var graph = new KnowledgeGraph
        {
            Nodes = nodes.Values
                .Take(maxNodes)
                .Select(node => new KnowledgeGraphNode
                {
                    Id = node.Id,
                    Properties = Clone(node.Properties)
                })
                .ToList(),
            Edges = edges.Values
                .Select(edge => new KnowledgeGraphEdge
                {
                    Source = edge.SourceId,
                    Target = edge.TargetId,
                    Properties = Clone(edge.Properties)
                })
                .ToList(),
            IsTruncated = nodes.Count > maxNodes
        };

        return Task.FromResult(graph);
    }

    public Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new List<string>());
    }

    public Task<List<string>> GetPopularLabelsAsync(
        int limit = 300,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new List<string>());
    }

    public Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = nodeIds
            .Where(nodes.ContainsKey)
            .ToDictionary(
                nodeId => nodeId,
                nodeId => Clone(nodes[nodeId]),
                StringComparer.Ordinal);

        return Task.FromResult(result);
    }

    public Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = nodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId),
            StringComparer.Ordinal);

        return Task.FromResult(result);
    }

    public Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = nodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => edges.Values
                .Where(edge => edge.SourceId == nodeId || edge.TargetId == nodeId)
                .Select(edge => (edge.SourceId, edge.TargetId))
                .ToList(),
            StringComparer.Ordinal);

        return Task.FromResult(result);
    }

    public Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<(string SourceId, string TargetId), GraphEdge>();
        foreach (var pair in edgePairs)
        {
            if (edges.TryGetValue(GetEdgeKey(pair.SourceId, pair.TargetId), out var edge))
            {
                result[pair] = Clone(edge);
            }
        }

        return Task.FromResult(result);
    }

    public Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = edgePairs.ToDictionary(
            pair => pair,
            pair => GetNodeDegree(pair.SourceId) + GetNodeDegree(pair.TargetId));

        return Task.FromResult(result);
    }

    private int GetNodeDegree(string nodeId)
    {
        return edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId);
    }

    private static string GetEdgeKey(string sourceId, string targetId)
    {
        return GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId);
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static GraphNode Clone(GraphNode node)
    {
        return new GraphNode
        {
            Id = node.Id,
            Properties = Clone(node.Properties)
        };
    }

    private static GraphEdge Clone(GraphEdge edge)
    {
        return new GraphEdge
        {
            SourceId = edge.SourceId,
            TargetId = edge.TargetId,
            Properties = Clone(edge.Properties)
        };
    }
}
