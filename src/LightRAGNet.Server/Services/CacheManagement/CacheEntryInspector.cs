using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheEntryInspector(IKVStore llmCacheStore)
{
    public async Task<IReadOnlyList<CacheInventoryEntry>> InspectAsync(
        string requestedWorkspace,
        long currentRevision,
        CancellationToken cancellationToken = default)
    {
        if (llmCacheStore is not IInspectableKVStore inspectableStore)
        {
            return [];
        }

        var snapshot = await inspectableStore.SnapshotAsync(cancellationToken);
        return snapshot
            .Select(entry => CreateInventoryEntry(entry, requestedWorkspace, currentRevision))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.CacheType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    public async Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return;
        }

        await llmCacheStore.DeleteAsync(keyList, cancellationToken);
        await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
    }

    private static CacheInventoryEntry? CreateInventoryEntry(
        InspectableKVStoreEntry descriptor,
        string requestedWorkspace,
        long currentRevision)
    {
        if (string.IsNullOrWhiteSpace(descriptor.CacheType))
        {
            return null;
        }

        var state = GetState(descriptor, requestedWorkspace, currentRevision);
        return new CacheInventoryEntry(
            descriptor.Key,
            descriptor.Key.Length <= 16 ? descriptor.Key : descriptor.Key[..16],
            descriptor.CacheType,
            state,
            null,
            descriptor.CreatedAt ?? 0);
    }

    private static string GetState(
        InspectableKVStoreEntry entry,
        string requestedWorkspace,
        long currentRevision)
    {
        if (string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && entry.WorkspaceQueryRevision is { } revision)
        {
            if (string.IsNullOrWhiteSpace(entry.Workspace))
            {
                return "unknown revision";
            }

            if (!string.Equals(entry.Workspace, requestedWorkspace, StringComparison.Ordinal))
            {
                return "other workspace";
            }

            if (revision != currentRevision)
            {
                return "old revision";
            }
        }

        return entry.HasChunkId ? "doc-linked" : "current";
    }
}
