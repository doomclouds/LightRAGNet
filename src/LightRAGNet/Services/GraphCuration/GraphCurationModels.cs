namespace LightRAGNet.Services.GraphCuration;

public sealed record GraphEntityCreateRequest(
    string EntityName,
    Dictionary<string, object> EntityData);

public sealed record GraphEntityEditRequest(
    string EntityName,
    Dictionary<string, object> UpdatedData,
    bool AllowRename,
    bool AllowMerge)
{
    public bool HasBlankDescription() =>
        UpdatedData.TryGetValue("description", out var value) &&
        string.IsNullOrWhiteSpace(value?.ToString());
}

public sealed record GraphRelationCreateRequest(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> RelationData);

public sealed record GraphRelationEditRequest(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> UpdatedData)
{
    public bool HasBlankDescription() =>
        UpdatedData.TryGetValue("description", out var value) &&
        string.IsNullOrWhiteSpace(value?.ToString());
}

public sealed record GraphEntityMergeRequest(
    IReadOnlyList<string> SourceEntities,
    string TargetEntity);
