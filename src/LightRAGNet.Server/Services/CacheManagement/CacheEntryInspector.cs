using System.Globalization;
using System.Text.Json;
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
            .Select(pair => CreateInventoryEntry(pair.Key, pair.Value, currentRevision))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.CacheType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static CacheInventoryEntry? CreateInventoryEntry(
        string key,
        Dictionary<string, object> value,
        long currentRevision)
    {
        if (!LightRagCacheEntry.TryFromDictionary(value, out var entry))
        {
            return null;
        }

        var state = GetState(entry, currentRevision);
        return new CacheInventoryEntry(
            key,
            key.Length <= 16 ? key : key[..16],
            entry.CacheType,
            state,
            entry.ChunkId,
            entry.CreateTime);
    }

    private static string GetState(LightRagCacheEntry entry, long currentRevision)
    {
        if (string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
            && TryReadRevision(entry.QueryParam, out var revision)
            && revision != currentRevision)
        {
            return "old revision";
        }

        return entry.ChunkId is not null ? "doc-linked" : "current";
    }

    private static bool TryReadRevision(Dictionary<string, object?>? queryParam, out long revision)
    {
        revision = 0;
        if (queryParam is null
            || !queryParam.TryGetValue("workspace_query_revision", out var value)
            || value is null)
        {
            return false;
        }

        switch (value)
        {
            case long number:
                revision = number;
                return true;
            case int number:
                revision = number;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number):
                revision = number;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } json:
                return long.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out revision);
            case string text:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out revision);
            default:
                return false;
        }
    }
}
