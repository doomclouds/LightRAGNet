namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Relation merge data model
/// </summary>
internal class RelationMergeData
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string RelationContent { get; set; } = string.Empty;
    public Dictionary<string, object> EdgeData { get; set; } = new();
    public string RelationId { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    /// <summary>
    /// Complete source_ids list (unlimited), used for updating entity_chunks_storage
    /// </summary>
    public List<string> FullSourceIds { get; set; } = [];
}

