using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.QueryCache;

public sealed class LightRagCacheKeyBuilder
{
    public const string QueryCacheType = "query";
    public const string KeywordsCacheType = "keywords";
    public const string MetadataCacheType = "metadata";
    public const string DefaultLanguageMarker = "default";
    private const string DefaultResponseType = "Multiple Paragraphs";

    public string BuildKeywordKey(
        string workspace,
        QueryMode mode,
        string query,
        string? languageMarker = null)
    {
        return BuildFlattenedKey(
            mode,
            KeywordsCacheType,
            [
                Pair("workspace", workspace),
                Pair("query", query),
                Pair("language", languageMarker ?? DefaultLanguageMarker)
            ]);
    }

    public string BuildRagQueryKey(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords)
    {
        return BuildFlattenedKey(
            queryParam.Mode,
            QueryCacheType,
            [
                Pair("workspace", workspace),
                Pair("workspace_query_revision", workspaceQueryRevision.ToString(CultureInfo.InvariantCulture)),
                Pair("query", query),
                Pair("response_type", NormalizeResponseType(queryParam.ResponseType)),
                Pair("top_k", queryParam.TopK.ToString(CultureInfo.InvariantCulture)),
                Pair("chunk_top_k", queryParam.ChunkTopK.ToString(CultureInfo.InvariantCulture)),
                Pair("max_entity_tokens", queryParam.MaxEntityTokens.ToString(CultureInfo.InvariantCulture)),
                Pair("max_relation_tokens", queryParam.MaxRelationTokens.ToString(CultureInfo.InvariantCulture)),
                Pair("max_total_tokens", queryParam.MaxTotalTokens.ToString(CultureInfo.InvariantCulture)),
                Pair("high_level_keywords", EncodeList(keywords.HighLevelKeywords)),
                Pair("low_level_keywords", EncodeList(keywords.LowLevelKeywords)),
                Pair("user_prompt", queryParam.UserPrompt ?? string.Empty),
                Pair("enable_rerank", queryParam.EnableRerank ? "true" : "false")
            ]);
    }

    public string BuildBypassQueryKey(string query, QueryParam queryParam)
    {
        return BuildFlattenedKey(
            QueryMode.Bypass,
            QueryCacheType,
            [
                Pair("query", query),
                Pair("response_type", NormalizeResponseType(queryParam.ResponseType)),
                Pair("user_prompt", queryParam.UserPrompt ?? string.Empty)
            ]);
    }

    public string BuildRevisionKey(string workspace)
    {
        return $"{MetadataCacheType}:query_revision:{workspace}";
    }

    private static KeyValuePair<string, string> Pair(string key, string value)
    {
        return new KeyValuePair<string, string>(key, value);
    }

    private static string NormalizeResponseType(string? responseType)
    {
        return string.IsNullOrWhiteSpace(responseType)
            ? DefaultResponseType
            : responseType;
    }

    private static string EncodeList(IReadOnlyCollection<string> values)
    {
        return string.Concat(
            $"Count={values.Count.ToString(CultureInfo.InvariantCulture)};",
            string.Join(
                string.Empty,
                values.Select((value, index) =>
                {
                    var normalized = value.Trim();
                    return $"{index.ToString(CultureInfo.InvariantCulture)}:{normalized.Length.ToString(CultureInfo.InvariantCulture)}:{normalized}";
                })));
    }

    private static string BuildFlattenedKey(
        QueryMode mode,
        string cacheType,
        IReadOnlyList<KeyValuePair<string, string>> parts)
    {
        var canonical = string.Join(
            "\u001f",
            parts.Select(part => $"{part.Key.Length}:{part.Key}={part.Value.Length}:{part.Value}"));
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"{mode}:{cacheType}:{hash}";
    }
}
