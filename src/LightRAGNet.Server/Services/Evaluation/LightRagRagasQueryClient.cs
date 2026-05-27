using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class LightRagRagasQueryClient(LightRAG lightRAG) : IRagasRagQueryClient
{
    public async Task<RagasQueryExecutionResult> QueryAsync(
        RagasDatasetCase dataSetCase,
        RagasEvaluationQueryOptions options,
        CancellationToken cancellationToken)
    {
        var queryParam = new QueryParam
        {
            Mode = options.Mode,
            Stream = false,
            IncludeReferences = true,
            OnlyNeedContext = false,
            OnlyNeedPrompt = false,
            TopK = options.TopK,
            ChunkTopK = options.ChunkTopK,
            EnableRerank = options.EnableRerank
        };

        var result = await lightRAG.QueryAsync(dataSetCase.Question, queryParam, cancellationToken);

        return new RagasQueryExecutionResult(
            result.Content ?? string.Empty,
            ExtractContexts(result.RawData),
            options.Mode);
    }

    internal static IReadOnlyList<RagasRetrievedContext> ExtractContexts(Dictionary<string, object>? rawData)
    {
        if (rawData is null || !TryGetData(rawData, out var data) || !TryGetChunks(data, out var chunks))
        {
            return [];
        }

        var contexts = new List<RagasRetrievedContext>();
        foreach (var chunk in chunks)
        {
            var content = ReadField(chunk, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            contexts.Add(new RagasRetrievedContext(
                content,
                ReadField(chunk, "chunk_id"),
                ReadField(chunk, "file_path"),
                ReadField(chunk, "reference_id")));
        }

        return contexts;
    }

    private static bool TryGetData(Dictionary<string, object> rawData, out object data)
    {
        if (rawData.TryGetValue("data", out data!) && data is not null)
        {
            return true;
        }

        data = null!;
        return false;
    }

    private static bool TryGetChunks(object data, out IEnumerable<object> chunks)
    {
        switch (data)
        {
            case Dictionary<string, object> dataDictionary
                when dataDictionary.TryGetValue("chunks", out var dictionaryChunks):
                return TryConvertChunks(dictionaryChunks, out chunks);
            case JsonElement { ValueKind: JsonValueKind.Object } dataElement
                when dataElement.TryGetProperty("chunks", out var jsonChunks):
                return TryConvertChunks(jsonChunks, out chunks);
            default:
                chunks = [];
                return false;
        }
    }

    private static bool TryConvertChunks(object? value, out IEnumerable<object> chunks)
    {
        switch (value)
        {
            case IEnumerable<Dictionary<string, object>> dictionaryChunks:
                chunks = dictionaryChunks;
                return true;
            case IEnumerable<object> objectChunks:
                chunks = objectChunks;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Array } arrayElement:
                chunks = arrayElement.EnumerateArray().Select(static item => (object)item).ToArray();
                return true;
            default:
                chunks = [];
                return false;
        }
    }

    private static string ReadField(object chunk, string fieldName)
    {
        return chunk switch
        {
            Dictionary<string, object> dictionary when dictionary.TryGetValue(fieldName, out var value) =>
                ReadScalar(value),
            JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty(fieldName, out var value) =>
                ReadScalar(value),
            _ => string.Empty
        };
    }

    private static string ReadScalar(object? value)
    {
        return value switch
        {
            null => string.Empty,
            JsonElement element => ReadJsonScalar(element),
            string text => text,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ReadJsonScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => string.Empty
        };
    }
}
