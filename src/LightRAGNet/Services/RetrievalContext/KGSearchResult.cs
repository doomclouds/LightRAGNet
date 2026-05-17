using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.RetrievalContext;

internal class KGSearchResult
{
    public List<EntityData> Entities { get; set; } = [];
    public List<RelationData> Relations { get; set; } = [];
    public List<ChunkData> Chunks { get; set; } = [];
    public List<ReferenceItem> References { get; set; } = [];
}

internal class EntityData
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Node degree (rank)
    /// </summary>
    public int Rank { get; set; }
    /// <summary>
    /// Creation time (optional)
    /// </summary>
    public string? CreatedAt { get; set; }
    /// <summary>
    /// File path (optional)
    /// </summary>
    public string? FilePath { get; set; }
    /// <summary>
    /// Source IDs (chunk IDs, separated by &lt;SEP&gt;)
    /// Reference: Python version source_id field
    /// </summary>
    public string? SourceId { get; set; } = string.Empty;
}

internal class RelationData
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Edge degree (rank)
    /// </summary>
    public int Rank { get; set; }
    /// <summary>
    /// Edge weight
    /// </summary>
    public double Weight { get; set; }
    /// <summary>
    /// Creation time (optional)
    /// </summary>
    public string? CreatedAt { get; set; }
    /// <summary>
    /// RSourceId IDs (chunk IDs, separated by &lt;SEP&gt;)
    /// Reference: Python version source_id field
    /// </summary>
    public string? RSourceId { get; set; } = string.Empty;
}

internal class ChunkData
{
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    /// <summary>
    /// Reference ID (corresponds to ReferenceItem's ReferenceId)
    /// Reference: Python version reference_id field
    /// </summary>
    public string ReferenceId { get; set; } = string.Empty;
}

/// <summary>
/// Chunk tracking information (consistent with Python version chunk_tracking)
/// </summary>
internal class ChunkTrackingInfo
{
    public string Source { get; set; } = string.Empty; // "C"=vector, "E"=entity, "R"=relation
    public int Frequency { get; set; }
    public int Order { get; set; }
}