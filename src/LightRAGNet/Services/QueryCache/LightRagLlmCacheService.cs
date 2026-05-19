using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.QueryCache;

public sealed class LightRagLlmCacheService(
    [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    LightRagCacheKeyBuilder keyBuilder,
    ILogger<LightRagLlmCacheService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public async Task<KeywordsResult?> TryGetKeywordsAsync(
        string workspace,
        QueryMode mode,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!IsKeywordCacheEnabled())
        {
            return null;
        }

        var key = keyBuilder.BuildKeywordKey(workspace, mode, query);
        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            if (!LightRagCacheEntry.TryFromDictionary(data, out var entry))
            {
                return null;
            }

            if (!TryDeserializeKeywordPayload(entry.ReturnValue, out var payload))
            {
                return null;
            }

            return new KeywordsResult
            {
                HighLevelKeywords = payload.HighLevelKeywords ?? [],
                LowLevelKeywords = payload.LowLevelKeywords ?? []
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read keyword cache entry {CacheKey}.", key);
            return null;
        }
    }

    public async Task SaveKeywordsAsync(
        string workspace,
        QueryMode mode,
        string query,
        KeywordsResult keywords,
        CancellationToken cancellationToken = default)
    {
        if (!IsKeywordCacheEnabled() || !HasAnyKeyword(keywords))
        {
            return;
        }

        var key = keyBuilder.BuildKeywordKey(workspace, mode, query);
        try
        {
            var payload = JsonSerializer.Serialize(
                new KeywordCachePayload
                {
                    HighLevelKeywords = keywords.HighLevelKeywords,
                    LowLevelKeywords = keywords.LowLevelKeywords
                },
                SerializerOptions);
            var entry = new LightRagCacheEntry(
                payload,
                LightRagCacheKeyBuilder.KeywordsCacheType,
                query,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [key] = entry.ToDictionary()
            }, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save keyword cache entry {CacheKey}.", key);
        }
    }

    public async Task<string?> TryGetQueryResponseAsync(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        CancellationToken cancellationToken = default)
    {
        if (!IsQueryCacheEnabled())
        {
            return null;
        }

        var key = BuildQueryKey(workspace, workspaceQueryRevision, query, queryParam, keywords);
        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            return LightRagCacheEntry.TryFromDictionary(data, out var entry)
                ? entry.ReturnValue
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read query cache entry {CacheKey}.", key);
            return null;
        }
    }

    public async Task SaveQueryResponseAsync(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        string response,
        CancellationToken cancellationToken = default)
    {
        if (!IsQueryCacheEnabled() || string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        var key = BuildQueryKey(workspace, workspaceQueryRevision, query, queryParam, keywords);
        try
        {
            var entry = new LightRagCacheEntry(
                response,
                LightRagCacheKeyBuilder.QueryCacheType,
                query,
                BuildQueryParamSnapshot(queryParam, keywords, workspaceQueryRevision),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [key] = entry.ToDictionary()
            }, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save query cache entry {CacheKey}.", key);
        }
    }

    public async Task<long> GetWorkspaceQueryRevisionAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadWorkspaceQueryRevisionStrictAsync(workspace, cancellationToken);
        return result.Revision;
    }

    public Task<(bool Succeeded, long Revision)> TryGetWorkspaceQueryRevisionAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        return ReadWorkspaceQueryRevisionStrictAsync(workspace, cancellationToken);
    }

    public async Task<long> BumpWorkspaceQueryRevisionAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadWorkspaceQueryRevisionStrictAsync(workspace, cancellationToken);
        if (!result.Succeeded)
        {
            return 0;
        }

        var key = keyBuilder.BuildRevisionKey(workspace);
        var next = result.Revision + 1;
        try
        {
            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [key] = new Dictionary<string, object>
                {
                    ["revision"] = next,
                    ["updated_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                }
            }, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
            return next;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save workspace query revision {CacheKey}.", key);
            return result.Revision;
        }
    }

    private async Task<(bool Succeeded, long Revision)> ReadWorkspaceQueryRevisionStrictAsync(
        string workspace,
        CancellationToken cancellationToken)
    {
        var key = keyBuilder.BuildRevisionKey(workspace);
        try
        {
            var data = await llmCacheStore.GetByIdAsync(key, cancellationToken);
            return (true, ReadRevision(data));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read workspace query revision {CacheKey}.", key);
            return (false, 0);
        }
    }

    private bool IsKeywordCacheEnabled()
    {
        return options.Value.EnableLlmCache && options.Value.EnableKeywordCache;
    }

    private bool IsQueryCacheEnabled()
    {
        return options.Value.EnableLlmCache && options.Value.EnableQueryCache;
    }

    private string BuildQueryKey(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords)
    {
        return queryParam.Mode == QueryMode.Bypass
            ? keyBuilder.BuildBypassQueryKey(query, queryParam)
            : keyBuilder.BuildRagQueryKey(workspace, workspaceQueryRevision, query, queryParam, keywords);
    }

    private static Dictionary<string, object?> BuildQueryParamSnapshot(
        QueryParam queryParam,
        KeywordsResult keywords,
        long workspaceQueryRevision)
    {
        return new Dictionary<string, object?>
        {
            ["mode"] = queryParam.Mode.ToString(),
            ["response_type"] = queryParam.ResponseType,
            ["top_k"] = queryParam.TopK,
            ["chunk_top_k"] = queryParam.ChunkTopK,
            ["max_entity_tokens"] = queryParam.MaxEntityTokens,
            ["max_relation_tokens"] = queryParam.MaxRelationTokens,
            ["max_total_tokens"] = queryParam.MaxTotalTokens,
            ["hl_keywords"] = keywords.HighLevelKeywords.ToList(),
            ["ll_keywords"] = keywords.LowLevelKeywords.ToList(),
            ["user_prompt"] = queryParam.UserPrompt,
            ["enable_rerank"] = queryParam.EnableRerank,
            ["workspace_query_revision"] = workspaceQueryRevision
        };
    }

    private static bool HasAnyKeyword(KeywordsResult keywords)
    {
        return keywords.HighLevelKeywords.Any(keyword => !string.IsNullOrWhiteSpace(keyword))
            || keywords.LowLevelKeywords.Any(keyword => !string.IsNullOrWhiteSpace(keyword));
    }

    private static long ReadRevision(Dictionary<string, object>? data)
    {
        if (data is null || !data.TryGetValue("revision", out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long revision => revision,
            int revision => revision,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var revision) => revision,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision) => revision,
            _ => 0
        };
    }

    private static bool TryDeserializeKeywordPayload(string json, out KeywordCachePayload payload)
    {
        payload = new KeywordCachePayload();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("high_level_keywords", out _)
            || !document.RootElement.TryGetProperty("low_level_keywords", out _))
        {
            return false;
        }

        var deserialized = JsonSerializer.Deserialize<KeywordCachePayload>(json, SerializerOptions);
        if (deserialized?.HighLevelKeywords is null || deserialized.LowLevelKeywords is null)
        {
            return false;
        }

        payload = deserialized;
        return true;
    }

    private sealed class KeywordCachePayload
    {
        [JsonPropertyName("high_level_keywords")]
        public List<string>? HighLevelKeywords { get; init; }

        [JsonPropertyName("low_level_keywords")]
        public List<string>? LowLevelKeywords { get; init; }
    }
}
