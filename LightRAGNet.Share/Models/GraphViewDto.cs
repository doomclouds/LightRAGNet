namespace LightRAGNet.Share.Models;

/// <summary>
/// Graph view data transfer object for sigma.js visualization
/// </summary>
public class GraphViewDto
{
    /// <summary>
    /// Graph nodes
    /// </summary>
    public List<GraphNodeDto> Nodes { get; set; } = [];

    /// <summary>
    /// Graph edges
    /// </summary>
    public List<GraphEdgeDto> Edges { get; set; } = [];

    /// <summary>
    /// Whether the graph is truncated
    /// </summary>
    public bool IsTruncated { get; set; }
}

/// <summary>
/// Graph node DTO for sigma.js
/// </summary>
public class GraphNodeDto
{
    /// <summary>
    /// Node ID (must be unique)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Node label (display text)
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Node size (for visualization)
    /// </summary>
    public double Size { get; set; } = 1.0;

    /// <summary>
    /// Node color
    /// </summary>
    public string Color { get; set; } = "#999";

    /// <summary>
    /// Node type (entity_type)
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Node properties (for display in properties panel)
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Graph edge DTO for sigma.js
/// </summary>
public class GraphEdgeDto
{
    /// <summary>
    /// Edge ID (must be unique)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source node ID
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Target node ID
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Edge type
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Edge size (for visualization)
    /// </summary>
    public double Size { get; set; } = 1.0;

    /// <summary>
    /// Edge color
    /// </summary>
    public string Color { get; set; } = "#ccc";
}
