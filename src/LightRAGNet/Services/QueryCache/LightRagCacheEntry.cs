using System.Globalization;
using System.Text.Json;

namespace LightRAGNet.Services.QueryCache;

public sealed record LightRagCacheEntry(
    string ReturnValue,
    string CacheType,
    string OriginalPrompt,
    Dictionary<string, object?>? QueryParam,
    long CreateTime,
    string? ChunkId = null)
{
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["return"] = ReturnValue,
            ["cache_type"] = CacheType,
            ["chunk_id"] = ChunkId!,
            ["original_prompt"] = OriginalPrompt,
            ["queryparam"] = QueryParam!,
            ["create_time"] = CreateTime
        };
    }

    public static bool TryFromDictionary(
        Dictionary<string, object>? data,
        out LightRagCacheEntry entry)
    {
        entry = new LightRagCacheEntry(string.Empty, string.Empty, string.Empty, null, 0);
        if (data is null)
        {
            return false;
        }

        var returnValue = ReadString(data, "return");
        var cacheType = ReadString(data, "cache_type");
        if (string.IsNullOrEmpty(returnValue) || string.IsNullOrEmpty(cacheType))
        {
            return false;
        }

        entry = new LightRagCacheEntry(
            returnValue,
            cacheType,
            ReadString(data, "original_prompt"),
            ReadDictionary(data, "queryparam"),
            ReadInt64(data, "create_time"),
            ReadNullableString(data, "chunk_id"));
        return true;
    }

    private static string ReadString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
            JsonElement json => json.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static long ReadInt64(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number) => number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };
    }

    private static string? ReadNullableString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } json => string.IsNullOrWhiteSpace(json.GetString())
                ? null
                : json.GetString(),
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }

    private static Dictionary<string, object?>? ReadDictionary(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Object } json)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json.GetRawText());
        }

        return null;
    }
}
