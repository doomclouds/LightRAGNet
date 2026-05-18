using System.Globalization;
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class KvDocumentStatusStore(
    [FromKeyedServices(KVContracts.DocStatus)] IKVStore store) : IDocumentStatusStore
{
    public async Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var currentKey = MakeKey(workspace, docId);
        var data = await store.GetByIdAsync(currentKey, cancellationToken);
        if (data is not null)
        {
            return FromDictionary(data);
        }

        var legacyKey = MakeLegacyKey(workspace, docId);
        var legacyData = await store.GetByIdAsync(legacyKey, cancellationToken);
        if (legacyData is null)
        {
            return null;
        }

        var record = FromDictionary(legacyData);
        if (record.Workspace != NormalizeWorkspace(workspace) || record.DocId != docId)
        {
            return null;
        }

        await UpsertAsync(record, cancellationToken);
        await store.DeleteAsync([legacyKey], cancellationToken);
        await store.IndexDoneCallbackAsync(cancellationToken);

        return record;
    }

    public async Task UpsertAsync(
        DocumentStatusRecord record,
        CancellationToken cancellationToken = default)
    {
        await store.UpsertAsync(
            new Dictionary<string, Dictionary<string, object>>
            {
                [MakeKey(record.Workspace, record.DocId)] = ToDictionary(record)
            },
            cancellationToken);

        await store.IndexDoneCallbackAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(
            [MakeKey(workspace, docId), MakeLegacyKey(workspace, docId)],
            cancellationToken);
        await store.IndexDoneCallbackAsync(cancellationToken);
    }

    private static string MakeKey(string workspace, string docId)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        return $"w{normalizedWorkspace.Length}:{normalizedWorkspace}d{docId.Length}:{docId}";
    }

    private static string MakeLegacyKey(string workspace, string docId)
    {
        return $"{NormalizeWorkspace(workspace)}:{docId}";
    }

    private static string NormalizeWorkspace(string workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }

    private static Dictionary<string, object> ToDictionary(DocumentStatusRecord record)
    {
        return new Dictionary<string, object>
        {
            ["doc_id"] = record.DocId,
            ["workspace"] = record.Workspace,
            ["status"] = record.Status.ToWireValue(),
            ["content_summary"] = record.ContentSummary,
            ["content_length"] = record.ContentLength,
            ["chunks_count"] = record.ChunksCount,
            ["chunks_list"] = record.ChunksList,
            ["chunk_snapshots"] = record.ChunkSnapshots.Select(snapshot => new Dictionary<string, object>
            {
                ["chunk_id"] = snapshot.ChunkId,
                ["tokens"] = snapshot.Tokens,
                ["chunk_order_index"] = snapshot.ChunkOrderIndex,
                ["file_path"] = snapshot.FilePath
            }).ToList(),
            ["file_path"] = record.FilePath,
            ["track_id"] = record.TrackId,
            ["error_msg"] = record.ErrorMessage,
            ["metadata"] = record.Metadata,
            ["created_at"] = record.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ["updated_at"] = record.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static DocumentStatusRecord FromDictionary(Dictionary<string, object> data)
    {
        return new DocumentStatusRecord
        {
            DocId = GetString(data, "doc_id"),
            Workspace = GetString(data, "workspace", "_"),
            Status = DocumentLifecycleStatusExtensions.FromWireValue(GetString(data, "status")),
            ContentSummary = GetString(data, "content_summary"),
            ContentLength = GetInt(data, "content_length"),
            ChunksCount = GetInt(data, "chunks_count"),
            ChunksList = GetStringList(data, "chunks_list"),
            ChunkSnapshots = GetChunkSnapshots(data, "chunk_snapshots"),
            FilePath = GetString(data, "file_path", "unknown_source"),
            TrackId = GetString(data, "track_id"),
            ErrorMessage = GetString(data, "error_msg"),
            Metadata = GetObjectDictionary(data, "metadata"),
            CreatedAt = GetDateTimeOffset(data, "created_at"),
            UpdatedAt = GetDateTimeOffset(data, "updated_at")
        };
    }

    private static string GetString(
        Dictionary<string, object> data,
        string key,
        string defaultValue = "")
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? defaultValue,
            JsonElement json when json.ValueKind == JsonValueKind.Null => defaultValue,
            _ => value.ToString() ?? defaultValue
        };
    }

    private static int GetInt(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Number => json.GetInt32(),
            int number => number,
            long number => checked((int)number),
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
        };
    }

    private static DateTimeOffset GetDateTimeOffset(Dictionary<string, object> data, string key)
    {
        var value = GetString(data, key);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static List<string> GetStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            return json.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrEmpty(item))
                .Select(item => item!)
                .ToList();
        }

        if (value is IEnumerable<object> objects)
        {
            return objects
                .Select(item => item.ToString())
                .Where(item => !string.IsNullOrEmpty(item))
                .Select(item => item!)
                .ToList();
        }

        return [];
    }

    private static List<DocumentChunkSnapshot> GetChunkSnapshots(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            return json.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new DocumentChunkSnapshot(
                    GetJsonString(item, "chunk_id"),
                    GetJsonInt(item, "tokens"),
                    GetJsonInt(item, "chunk_order_index"),
                    GetJsonString(item, "file_path", "unknown_source")))
                .ToList();
        }

        if (value is IEnumerable<object> objects)
        {
            return objects
                .OfType<Dictionary<string, object>>()
                .Select(item => new DocumentChunkSnapshot(
                    GetString(item, "chunk_id"),
                    GetInt(item, "tokens"),
                    GetInt(item, "chunk_order_index"),
                    GetString(item, "file_path", "unknown_source")))
                .ToList();
        }

        return [];
    }

    private static Dictionary<string, object> GetObjectDictionary(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            return json.EnumerateObject()
                .ToDictionary(property => property.Name, property => ReadJsonValue(property.Value));
        }

        return value as Dictionary<string, object> ?? [];
    }

    private static string GetJsonString(JsonElement data, string key, string defaultValue = "")
    {
        return data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private static int GetJsonInt(JsonElement data, string key)
    {
        return data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static object ReadJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(property => property.Name, property => ReadJsonValue(property.Value)),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(ReadJsonValue)
                .ToList(),
            _ => value.ToString()
        };
    }
}
