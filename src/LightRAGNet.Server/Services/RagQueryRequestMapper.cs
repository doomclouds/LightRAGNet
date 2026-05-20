using System.Globalization;
using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services;

public static class RagQueryRequestMapper
{
    public static QueryParam ToQueryParam(RagQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new QueryParam
        {
            Mode = request.Mode,
            Stream = request.Stream,
            IncludeReferences = request.IncludeReferences,
            ResponseType = NormalizeResponseType(request.ResponseType),
            TopK = request.TopK,
            ChunkTopK = request.ChunkTopK,
            EnableRerank = request.EnableRerank,
            HighLevelKeywords = NormalizeKeywords(request.HighLevelKeywords),
            LowLevelKeywords = NormalizeKeywords(request.LowLevelKeywords),
            OnlyNeedContext = request.OnlyNeedContext,
            OnlyNeedPrompt = request.OnlyNeedPrompt,
            ConversationHistory = []
        };
    }

    public static QueryMetadataEvent ToMetadataEvent(RagQueryRequest request, QueryResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = result.Metadata;

        return new QueryMetadataEvent
        {
            Mode = request.Mode,
            Stream = request.Stream,
            IncludeReferences = request.IncludeReferences,
            ResponseType = NormalizeResponseType(request.ResponseType),
            CachePolicy = request.Stream ? "Streaming request" : "Cacheable request",
            References = request.IncludeReferences ? result.ReferenceList.Select(ToReferenceDto).ToArray() : [],
            HighLevelKeywords = GetMetadataKeywords(metadata, "high_level_keywords", request.HighLevelKeywords),
            LowLevelKeywords = GetMetadataKeywords(metadata, "low_level_keywords", request.LowLevelKeywords),
            Diagnostics = ToDiagnostics(metadata)
        };
    }

    private static string NormalizeResponseType(string? responseType)
    {
        return string.IsNullOrWhiteSpace(responseType)
            ? "Multiple Paragraphs"
            : responseType.Trim();
    }

    private static List<string> NormalizeKeywords(IEnumerable<string>? keywords)
    {
        return keywords?
            .Select(keyword => keyword.Trim())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .ToList() ?? [];
    }

    private static IReadOnlyList<string> GetMetadataKeywords(
        IReadOnlyDictionary<string, object> metadata,
        string key,
        IEnumerable<string>? fallback)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            var keywords = NormalizeKeywordValue(value);
            if (keywords.Count > 0)
            {
                return keywords;
            }
        }

        return NormalizeKeywords(fallback);
    }

    private static List<string> NormalizeKeywordValue(object? value)
    {
        return value switch
        {
            null => [],
            string keyword => NormalizeKeywords([keyword]),
            IEnumerable<string> keywords => NormalizeKeywords(keywords),
            JsonElement { ValueKind: JsonValueKind.String } element => NormalizeKeywords([element.GetString() ?? string.Empty]),
            JsonElement { ValueKind: JsonValueKind.Array } element => NormalizeKeywords(
                element.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : item.ToString())),
            IEnumerable<object> keywords => NormalizeKeywords(keywords.Select(keyword => Convert.ToString(keyword, CultureInfo.InvariantCulture) ?? string.Empty)),
            _ => []
        };
    }

    private static RagQueryReferenceDto ToReferenceDto(ReferenceItem item)
    {
        return new RagQueryReferenceDto
        {
            ReferenceId = item.ReferenceId,
            FilePath = item.FilePath
        };
    }

    private static IReadOnlyDictionary<string, string> ToDiagnostics(IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return metadata.ToDictionary(
            pair => pair.Key,
            pair => FormatDiagnosticValue(pair.Value));
    }

    private static string FormatDiagnosticValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            JsonElement element => FormatJsonElement(element),
            IEnumerable<string> strings => string.Join(", ", NormalizeKeywords(strings)),
            IEnumerable<object> objects when objects.All(IsSimpleDiagnosticValue) =>
                string.Join(", ", objects.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => JsonSerializer.Serialize(value, LightRAGJsonOptions.HumanReadable)
        };
    }

    private static string FormatJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToArray();
            if (items.All(item => item.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            {
                return string.Join(", ", items.Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString()));
            }
        }

        return JsonSerializer.Serialize(element, LightRAGJsonOptions.HumanReadable);
    }

    private static bool IsSimpleDiagnosticValue(object? value)
    {
        return value is null
            or string
            or bool
            or byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or float
            or double
            or decimal
            or DateTime
            or DateTimeOffset;
    }
}
