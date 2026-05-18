namespace LightRAGNet.Services.DocumentDeletion;

public sealed class DocumentDeletionImpact
{
    public List<string> ChunkIdsToDelete { get; } = [];
    public List<string> EntityIdsToDelete { get; } = [];
    public Dictionary<string, IReadOnlyList<string>> EntityIdsToUpdate { get; } = new(StringComparer.Ordinal);
    public List<(string SourceId, string TargetId)> RelationsToDelete { get; } = [];
    public Dictionary<(string SourceId, string TargetId), IReadOnlyList<string>> RelationsToUpdate { get; } = [];
    public List<string> LlmCacheIdsToDelete { get; } = [];
}
