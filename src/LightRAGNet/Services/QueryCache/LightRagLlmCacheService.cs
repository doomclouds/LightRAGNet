using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.QueryCache;

public sealed class LightRagLlmCacheService(
    [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    LightRagCacheKeyBuilder keyBuilder,
    ICacheMetricsRecorder metricsRecorder,
    ILogger<LightRagLlmCacheService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = LightRAGJsonOptions.HumanReadable;

    public LightRagLlmCacheService(
        [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
        IOptions<LightRAGOptions> options,
        LightRagCacheKeyBuilder keyBuilder,
        ILogger<LightRagLlmCacheService> logger)
        : this(llmCacheStore, options, keyBuilder, new NoopCacheMetricsRecorder(), logger)
    {
    }

    public Task<CacheValueResult<KeywordsResult>> GetOrCreateKeywordsAsync(
        string workspace,
        QueryMode mode,
        string query,
        Func<CancellationToken, Task<KeywordsResult>> factory,
        CancellationToken cancellationToken = default)
    {
        var key = keyBuilder.BuildKeywordKey(workspace, mode, query);
        return GetOrCreateAsync(
            workspace,
            LightRagCacheKeyBuilder.KeywordsCacheType,
            mode.ToString(),
            null,
            IsKeywordCacheEnabled(),
            key,
            async ct =>
            {
                var data = await llmCacheStore.GetByIdAsync(key, ct);
                if (data is null)
                {
                    return CacheReadResult<KeywordsResult>.Miss();
                }

                if (!LightRagCacheEntry.TryFromDictionary(data, out var entry)
                    || !string.Equals(entry.CacheType, LightRagCacheKeyBuilder.KeywordsCacheType, StringComparison.Ordinal)
                    || !TryDeserializeKeywordPayload(entry.ReturnValue, out var payload))
                {
                    return CacheReadResult<KeywordsResult>.Invalid();
                }

                return CacheReadResult<KeywordsResult>.Hit(new KeywordsResult
                {
                    HighLevelKeywords = payload.HighLevelKeywords ?? [],
                    LowLevelKeywords = payload.LowLevelKeywords ?? []
                });
            },
            factory,
            HasAnyKeyword,
            (keywords, ct) =>
            {
                var payload = JsonSerializer.Serialize(
                    new KeywordCachePayload
                    {
                        HighLevelKeywords = keywords.HighLevelKeywords,
                        LowLevelKeywords = keywords.LowLevelKeywords
                    },
                    SerializerOptions);
                return SaveEntryAsync(
                    key,
                    new LightRagCacheEntry(
                        payload,
                        LightRagCacheKeyBuilder.KeywordsCacheType,
                        query,
                        null,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    ct);
            },
            cancellationToken);
    }

    public async Task<CacheValueResult<string>> GetOrCreateQueryResponseAsync(
        string workspace,
        long workspaceQueryRevision,
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        Func<CancellationToken, Task<string>> factory,
        CancellationToken cancellationToken = default)
    {
        var key = BuildQueryKey(workspace, workspaceQueryRevision, query, queryParam, keywords);
        return await GetOrCreateAsync(
            workspace,
            LightRagCacheKeyBuilder.QueryCacheType,
            queryParam.Mode.ToString(),
            workspaceQueryRevision,
            IsQueryCacheEnabled(),
            key,
            async ct =>
            {
                var data = await llmCacheStore.GetByIdAsync(key, ct);
                if (data is null)
                {
                    return CacheReadResult<string>.Miss();
                }

                return LightRagCacheEntry.TryFromDictionary(data, out var entry)
                    && string.Equals(entry.CacheType, LightRagCacheKeyBuilder.QueryCacheType, StringComparison.Ordinal)
                        ? CacheReadResult<string>.Hit(entry.ReturnValue)
                        : CacheReadResult<string>.Invalid();
            },
            factory,
            response => !string.IsNullOrWhiteSpace(response),
            (response, ct) => SaveEntryAsync(
                key,
                new LightRagCacheEntry(
                    response,
                    LightRagCacheKeyBuilder.QueryCacheType,
                    query,
                    BuildQueryParamSnapshot(queryParam, keywords, workspaceQueryRevision),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                ct),
            cancellationToken);
    }

    public Task<CacheValueResult<string>> GetOrCreateExtractAsync(
        string canonicalPrompt,
        string chunkId,
        Func<CancellationToken, Task<string>> factory,
        CancellationToken cancellationToken = default)
    {
        return GetOrCreateIndexingAsync(
            keyBuilder.BuildExtractKey(canonicalPrompt),
            canonicalPrompt,
            LightRagCacheKeyBuilder.ExtractCacheType,
            chunkId,
            factory,
            cancellationToken);
    }

    public Task<CacheValueResult<string>> GetOrCreateSummaryAsync(
        string canonicalPrompt,
        Func<CancellationToken, Task<string>> factory,
        CancellationToken cancellationToken = default)
    {
        return GetOrCreateIndexingAsync(
            keyBuilder.BuildSummaryKey(canonicalPrompt),
            canonicalPrompt,
            LightRagCacheKeyBuilder.SummaryCacheType,
            null,
            factory,
            cancellationToken);
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

    private bool IsIndexingCacheEnabled()
    {
        return options.Value.EnableLlmCache && options.Value.EnableLlmCacheForEntityExtract;
    }

    private async Task<CacheValueResult<T>> GetOrCreateAsync<T>(
        string workspace,
        string cacheType,
        string? mode,
        long? revision,
        bool cacheEnabled,
        string cacheKey,
        Func<CancellationToken, Task<CacheReadResult<T>>> tryRead,
        Func<CancellationToken, Task<T>> factory,
        Func<T, bool> shouldSave,
        Func<T, CancellationToken, Task<bool>> save,
        CancellationToken cancellationToken)
    {
        if (!cacheEnabled)
        {
            var disabledFactoryDuration = Stopwatch.StartNew();
            var disabledValue = await factory(cancellationToken);
            disabledFactoryDuration.Stop();
            await RecordReadMetricAsync(
                workspace,
                cacheType,
                CacheReadOutcome.Disabled,
                mode,
                TimeSpan.Zero,
                disabledFactoryDuration.Elapsed,
                null,
                revision,
                cancellationToken);
            return CacheValueResult<T>.FromMiss(
                disabledValue,
                cacheEnabled: false,
                saved: false,
                cacheKey: null,
                cacheType,
                TimeSpan.Zero,
                disabledFactoryDuration.Elapsed);
        }

        CacheReadResult<T> read;
        TimeSpan lookupElapsed;
        var lookupDuration = Stopwatch.StartNew();
        try
        {
            read = await tryRead(cancellationToken);
            lookupDuration.Stop();
            lookupElapsed = lookupDuration.Elapsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lookupDuration.Stop();
            lookupElapsed = lookupDuration.Elapsed;
            logger.LogWarning(ex, "Failed to read {CacheType} cache entry {CacheKey}.", cacheType, cacheKey);
            var errorFactoryDuration = Stopwatch.StartNew();
            var errorValue = await factory(cancellationToken);
            errorFactoryDuration.Stop();
            await RecordReadMetricAsync(
                workspace,
                cacheType,
                CacheReadOutcome.Error,
                mode,
                lookupDuration.Elapsed,
                errorFactoryDuration.Elapsed,
                cacheKey,
                revision,
                cancellationToken);
            return CacheValueResult<T>.FromMiss(
                errorValue,
                cacheEnabled: true,
                saved: false,
                cacheKey: null,
                cacheType,
                lookupElapsed,
                errorFactoryDuration.Elapsed);
        }

        if (read.Outcome == CacheReadOutcome.Hit)
        {
            await RecordReadMetricAsync(
                workspace,
                cacheType,
                CacheReadOutcome.Hit,
                mode,
                lookupElapsed,
                null,
                cacheKey,
                revision,
                cancellationToken);
            return CacheValueResult<T>.FromHit(read.Value, cacheType, cacheKey, lookupElapsed);
        }

        return await CreateAndMaybeSaveAsync(
            workspace,
            cacheType,
            read.Outcome,
            mode,
            revision,
            cacheKey,
            lookupElapsed,
            factory,
            shouldSave,
            save,
            cancellationToken);
    }

    private async Task<CacheValueResult<T>> CreateAndMaybeSaveAsync<T>(
        string workspace,
        string cacheType,
        string readOutcome,
        string? mode,
        long? revision,
        string cacheKey,
        TimeSpan lookupDuration,
        Func<CancellationToken, Task<T>> factory,
        Func<T, bool> shouldSave,
        Func<T, CancellationToken, Task<bool>> save,
        CancellationToken cancellationToken)
    {
        var factoryDuration = Stopwatch.StartNew();
        var value = await factory(cancellationToken);
        factoryDuration.Stop();
        await RecordReadMetricAsync(
            workspace,
            cacheType,
            readOutcome,
            mode,
            lookupDuration,
            factoryDuration.Elapsed,
            cacheKey,
            revision,
            cancellationToken);

        var saved = false;
        if (shouldSave(value))
        {
            var saveDuration = Stopwatch.StartNew();
            saved = await save(value, cancellationToken);
            saveDuration.Stop();
            if (saved)
            {
                await RecordSaveMetricAsync(
                    workspace,
                    cacheType,
                    mode,
                    saveDuration.Elapsed,
                    cacheKey,
                    revision,
                    cancellationToken);
            }
        }

        return CacheValueResult<T>.FromMiss(
            value,
            cacheEnabled: true,
            saved,
            saved ? cacheKey : null,
            cacheType,
            lookupDuration,
            factoryDuration.Elapsed);
    }

    private Task<CacheValueResult<string>> GetOrCreateIndexingAsync(
        string key,
        string canonicalPrompt,
        string cacheType,
        string? chunkId,
        Func<CancellationToken, Task<string>> factory,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(
            options.Value.Workspace,
            cacheType,
            LightRagCacheKeyBuilder.DefaultCacheMode,
            null,
            IsIndexingCacheEnabled(),
            key,
            async ct =>
            {
                var data = await llmCacheStore.GetByIdAsync(key, ct);
                if (data is null)
                {
                    return CacheReadResult<string>.Miss();
                }

                return LightRagCacheEntry.TryFromDictionary(data, out var entry)
                    && string.Equals(entry.CacheType, cacheType, StringComparison.Ordinal)
                        ? CacheReadResult<string>.Hit(entry.ReturnValue)
                        : CacheReadResult<string>.Invalid();
            },
            factory,
            response => !string.IsNullOrWhiteSpace(response),
            (response, ct) => SaveEntryAsync(
                key,
                new LightRagCacheEntry(
                    response,
                    cacheType,
                    canonicalPrompt,
                    null,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    chunkId),
                ct),
            cancellationToken);
    }

    private async Task<bool> SaveEntryAsync(
        string key,
        LightRagCacheEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [key] = entry.ToDictionary()
            }, cancellationToken);
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save {CacheType} cache entry {CacheKey}.", entry.CacheType, key);
            return false;
        }
    }

    private async Task RecordReadMetricAsync(
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        TimeSpan duration,
        TimeSpan? factoryDuration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await metricsRecorder.RecordReadAsync(
                workspace,
                cacheType,
                outcome,
                mode,
                duration,
                factoryDuration,
                cacheKey,
                revision,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record {CacheType} cache read metric.", cacheType);
        }
    }

    private async Task RecordSaveMetricAsync(
        string workspace,
        string cacheType,
        string? mode,
        TimeSpan duration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await metricsRecorder.RecordSaveAsync(
                workspace,
                cacheType,
                mode,
                duration,
                cacheKey,
                revision,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record {CacheType} cache save metric.", cacheType);
        }
    }

    private readonly record struct CacheReadResult<T>(string Outcome, T Value)
    {
        public static CacheReadResult<T> Hit(T value)
        {
            return new CacheReadResult<T>(CacheReadOutcome.Hit, value);
        }

        public static CacheReadResult<T> Miss()
        {
            return new CacheReadResult<T>(CacheReadOutcome.Miss, default!);
        }

        public static CacheReadResult<T> Invalid()
        {
            return new CacheReadResult<T>(CacheReadOutcome.Invalid, default!);
        }
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
        try
        {
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
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class NoopCacheMetricsRecorder : ICacheMetricsRecorder
    {
        public Task RecordReadAsync(
            string workspace,
            string cacheType,
            string outcome,
            string? mode,
            TimeSpan duration,
            TimeSpan? factoryDuration,
            string? cacheKey,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordSaveAsync(
            string workspace,
            string cacheType,
            string? mode,
            TimeSpan duration,
            string? cacheKey,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordClearAsync(
            string workspace,
            string cacheType,
            TimeSpan duration,
            long? revision,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class KeywordCachePayload
    {
        [JsonPropertyName("high_level_keywords")]
        public List<string>? HighLevelKeywords { get; init; }

        [JsonPropertyName("low_level_keywords")]
        public List<string>? LowLevelKeywords { get; init; }
    }
}
