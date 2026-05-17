namespace LightRAGNet.Core.Models;

public class KnowledgeGraph
{
    public List<KnowledgeGraphNode> Nodes { get; set; } = [];
    public List<KnowledgeGraphEdge> Edges { get; set; } = [];
    public bool IsTruncated { get; set; }
}

public class KnowledgeGraphNode
{
    public string Id { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class KnowledgeGraphEdge
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

