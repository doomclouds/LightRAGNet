using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentDeletion;

public sealed class DocumentDeletionImpact
{
    public List<string> ChunkIdsToDelete { get; } = [];
    public List<string> EntityIdsToDelete { get; } = [];
    public List<EntityReferenceUpdate> EntityUpdates { get; } = [];
    public List<RelationReferenceDelete> RelationsToDelete { get; } = [];
    public List<RelationReferenceUpdate> RelationUpdates { get; } = [];
    public List<string> LlmCacheIdsToDelete { get; } = [];
}

public sealed record EntityReferenceUpdate(
    string EntityName,
    IReadOnlyList<string> RemainingChunkIds,
    Dictionary<string, object> UpdatedProperties,
    VectorDocument VectorDocument);

public sealed record RelationReferenceDelete(
    string SourceId,
    string TargetId,
    string RelationKey);

public sealed record RelationReferenceUpdate(
    string SourceId,
    string TargetId,
    string RelationKey,
    IReadOnlyList<string> RemainingChunkIds,
    Dictionary<string, object> UpdatedProperties,
    VectorDocument VectorDocument);
