using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace LightRAGNet.Core.Interfaces;

public sealed record InspectableKVStoreEntry(
    string Key,
    string? CacheType,
    long? WorkspaceQueryRevision,
    bool HasChunkId,
    long? CreatedAt)
{
    public static InspectableKVStoreEntry? FromRaw(
        string key,
        IReadOnlyDictionary<string, object> value)
    {
        var cacheType = ReadString(value, "cache_type");
        if (string.IsNullOrWhiteSpace(cacheType))
        {
            return null;
        }

        return new InspectableKVStoreEntry(
            key,
            cacheType,
            ReadWorkspaceQueryRevision(value),
            !string.IsNullOrWhiteSpace(ReadString(value, "chunk_id")),
            ReadInt64(value, "create_time"));
    }

    private static long? ReadWorkspaceQueryRevision(IReadOnlyDictionary<string, object> value)
    {
        if (!value.TryGetValue("queryparam", out var queryParam)
            || queryParam is null
            || !TryReadDictionaryValue(queryParam, "workspace_query_revision", out var revisionValue))
        {
            return null;
        }

        return ReadInt64(revisionValue);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> value, string key)
    {
        if (!value.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return null;
        }

        return rawValue switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement json => json.ToString(),
            _ => rawValue.ToString()
        };
    }

    private static long? ReadInt64(IReadOnlyDictionary<string, object> value, string key)
    {
        return value.TryGetValue(key, out var rawValue) ? ReadInt64(rawValue) : null;
    }

    private static long? ReadInt64(object? value)
    {
        return value switch
        {
            null => null,
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number) => number,
            JsonElement { ValueKind: JsonValueKind.String } json
                when long.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool TryReadDictionaryValue(object value, string key, out object? result)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Object } json)
        {
            if (json.TryGetProperty(key, out var property))
            {
                result = property;
                return true;
            }

            result = null;
            return false;
        }

        if (value is IDictionary dictionary && dictionary.Contains(key))
        {
            result = dictionary[key];
            return true;
        }

        result = null;
        return false;
    }
}

public interface IInspectableKVStore
{
    Task<IReadOnlyList<InspectableKVStoreEntry>> SnapshotAsync(
        CancellationToken cancellationToken = default);
}
