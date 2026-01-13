namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Entity merge data model
/// </summary>
internal class EntityMergeData
{
    public string EntityName { get; set; } = string.Empty;
    public string EntityContent { get; set; } = string.Empty;
    public Dictionary<string, object> NodeData { get; set; } = new();
    public string EntityId { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
}

