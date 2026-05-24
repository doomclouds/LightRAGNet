using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheEntryInspector(IKVStore llmCacheStore)
{
    public async Task<IReadOnlyList<CacheInventoryEntry>> InspectAsync(
        long currentRevision,
        CancellationToken cancellationToken = default)
    {
        if (llmCacheStore is not IInspectableKVStore inspectableStore)
        {
            return [];
        }

        var snapshot = await inspectableStore.SnapshotAsync(cancellationToken);
        return snapshot
            .Select(entry => CreateInventoryEntry(entry, currentRevision))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.CacheType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static CacheInventoryEntry? CreateInventoryEntry(
        InspectableKVStoreEntry descriptor,
        long currentRevision)
    {
        if (string.IsNullOrWhiteSpace(descriptor.CacheType))
        {
            return null;
        }

        var state = GetState(descriptor, currentRevision);
        return new CacheInventoryEntry(
            descriptor.Key,
            descriptor.Key.Length <= 16 ? descriptor.Key : descriptor.Key[..16],
            descriptor.CacheType,
            state,
            null,
            descriptor.CreatedAt ?? 0);
    }

    private static string GetState(InspectableKVStoreEntry entry, long currentRevision)
    {
        if (string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && entry.WorkspaceQueryRevision is { } revision
            && revision != currentRevision)
        {
            return "old revision";
        }

        return entry.HasChunkId ? "doc-linked" : "current";
    }
}
