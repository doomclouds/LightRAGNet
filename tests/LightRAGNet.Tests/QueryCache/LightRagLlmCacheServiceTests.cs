using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRagLlmCacheServiceTests
{
    [Fact]
    public void LightRagCacheEntry_ToDictionary_IncludesChunkIdWhenPresent()
    {
        var entry = new LightRagCacheEntry(
            "raw extract response",
            LightRagCacheKeyBuilder.ExtractCacheType,
            "canonical prompt",
            null,
            123,
            "chunk-a");

        var data = entry.ToDictionary();

        data["return"].Should().Be("raw extract response");
        data["cache_type"].Should().Be("extract");
        data["chunk_id"].Should().Be("chunk-a");
        data["original_prompt"].Should().Be("canonical prompt");
        data["queryparam"].Should().BeNull();
        data["create_time"].Should().Be(123);
    }

    [Fact]
    public void LightRagCacheEntry_TryFromDictionary_ReadsNullChunkId()
    {
        var data = new Dictionary<string, object>
        {
            ["return"] = "summary",
            ["cache_type"] = LightRagCacheKeyBuilder.SummaryCacheType,
            ["chunk_id"] = null!,
            ["original_prompt"] = "summary prompt",
            ["queryparam"] = null!,
            ["create_time"] = 456
        };

        var ok = LightRagCacheEntry.TryFromDictionary(data, out var entry);

        ok.Should().BeTrue();
        entry.ChunkId.Should().BeNull();
        entry.CacheType.Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
    }

    [Fact]
    public async Task GetOrCreateKeywordsAsync_WhenCacheHit_ReturnsKeywordsAndSkipsFactory()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var key = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "what is cache?");
        store.Seed(
            key,
            new LightRagCacheEntry(
                """{"high_level_keywords":["rag","cache"],"low_level_keywords":["chunk"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var factoryCalls = 0;

        var result = await service.GetOrCreateKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            "what is cache?",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult(new KeywordsResult
                {
                    HighLevelKeywords = ["fallback"],
                    LowLevelKeywords = []
                });
            });

        result.Hit.Should().BeTrue();
        result.Value.HighLevelKeywords.Should().Equal("rag", "cache");
        result.Value.LowLevelKeywords.Should().Equal("chunk");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateKeywordsAsync_WhenCacheMalformed_CallsFactoryAndSavesFallback()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var key = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "what is cache?");
        store.Seed(
            key,
            new LightRagCacheEntry(
                """{"highLevelKeywords":["wrong-shape"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.GetOrCreateKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            "what is cache?",
            _ => Task.FromResult(new KeywordsResult
            {
                HighLevelKeywords = ["fallback-high"],
                LowLevelKeywords = ["fallback-low"]
            }));

        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.Value.HighLevelKeywords.Should().Equal("fallback-high");
        result.Value.LowLevelKeywords.Should().Equal("fallback-low");
    }

    [Fact]
    public async Task GetOrCreateKeywordsAsync_WhenCacheMiss_PersistsChinesePayloadWithoutUnicodeEscapes()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);

        await service.GetOrCreateKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            "请用100字简述采集流程",
            _ => Task.FromResult(new KeywordsResult
            {
                HighLevelKeywords = ["采集流程", "简述"],
                LowLevelKeywords = ["100字"]
            }));

        store.Items.Should().ContainSingle();
        var entry = store.Items.Values.Single();
        var payload = entry["return"].Should().BeOfType<string>().Subject;
        payload.Should().Contain("采集流程");
        payload.Should().Contain("100字");
        payload.Should().NotContain("\\u91C7");
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenCacheMiss_RoundTripsResponseAndSnapshot()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Mix,
            ResponseType = "Single Paragraph",
            TopK = 7,
            ChunkTopK = 3,
            MaxEntityTokens = 100,
            MaxRelationTokens = 200,
            MaxTotalTokens = 300,
            UserPrompt = "answer shortly",
            EnableRerank = false
        };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["rag"],
            LowLevelKeywords = ["cache"]
        };

        var miss = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            2,
            "what is cache?",
            queryParam,
            keywords,
            _ => Task.FromResult("cached answer"));
        var hit = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            2,
            "what is cache?",
            queryParam,
            keywords,
            _ => throw new InvalidOperationException("Factory should not run on cache hit."));

        miss.Saved.Should().BeTrue();
        hit.Hit.Should().BeTrue();
        hit.Value.Should().Be("cached answer");
        store.Items.Should().ContainSingle();
        var entry = store.Items.Values.Single();
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.QueryCacheType);
        entry["original_prompt"].Should().Be("what is cache?");
        var queryParamSnapshot = entry["queryparam"].Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        queryParamSnapshot.Should().ContainKeys(
            "mode",
            "response_type",
            "top_k",
            "chunk_top_k",
            "max_entity_tokens",
            "max_relation_tokens",
            "max_total_tokens",
            "hl_keywords",
            "ll_keywords",
            "user_prompt",
            "enable_rerank",
            "workspace_query_revision");
        queryParamSnapshot["workspace_query_revision"].Should().Be(2L);
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenCacheHit_DoesNotCallFactory()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Mix,
            ResponseType = "Single Paragraph",
            TopK = 7
        };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["rag"],
            LowLevelKeywords = ["cache"]
        };
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 3, "what is cache?", queryParam, keywords);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "cached answer",
                LightRagCacheKeyBuilder.QueryCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);
        var factoryCalls = 0;

        var result = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            3,
            "what is cache?",
            queryParam,
            keywords,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("factory answer");
            });

        result.Value.Should().Be("cached answer");
        result.Hit.Should().BeTrue();
        result.Saved.Should().BeFalse();
        result.CacheKey.Should().Be(key);
        factoryCalls.Should().Be(0);
        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.CacheType.Should().Be(LightRagCacheKeyBuilder.QueryCacheType);
        read.Outcome.Should().Be(CacheReadOutcome.Hit);
        read.FactoryDuration.Should().BeNull();
        read.CacheKey.Should().Be(key);
        read.Revision.Should().Be(3);
        recorder.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenCacheHit_ReturnsRawResponse()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical extract prompt";
        var key = keyBuilder.BuildExtractKey(canonicalPrompt);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.GetOrCreateExtractAsync(
            canonicalPrompt,
            "chunk-a",
            _ => throw new InvalidOperationException("Factory should not run on cache hit."));

        result.Hit.Should().BeTrue();
        result.Value.Should().Be("raw extract response");
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenCacheMiss_PersistsPythonStyleEntry()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(store, keyBuilder: keyBuilder);
        const string canonicalPrompt = "canonical extract prompt";

        var result = await service.GetOrCreateExtractAsync(
            canonicalPrompt,
            "chunk-a",
            _ => Task.FromResult("raw extract response"));
        var key = result.CacheKey;

        key.Should().Be(keyBuilder.BuildExtractKey(canonicalPrompt));
        store.Items.Should().ContainKey(key!);
        var entry = store.Items[key!];
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.ExtractCacheType);
        entry["chunk_id"].Should().Be("chunk-a");
        entry["original_prompt"].Should().Be(canonicalPrompt);
        entry["return"].Should().Be("raw extract response");
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenCacheMiss_CallsFactoryAndSavesKey()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);
        const string canonicalPrompt = "canonical extract prompt";
        var expectedKey = keyBuilder.BuildExtractKey(canonicalPrompt);
        var factoryCalls = 0;

        var result = await service.GetOrCreateExtractAsync(
            canonicalPrompt,
            "chunk-a",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("raw extract response");
            });

        result.Value.Should().Be("raw extract response");
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.CacheKey.Should().Be(expectedKey);
        factoryCalls.Should().Be(1);
        store.Items.Should().ContainKey(expectedKey);
        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.CacheType.Should().Be(LightRagCacheKeyBuilder.ExtractCacheType);
        read.Outcome.Should().Be(CacheReadOutcome.Miss);
        read.FactoryDuration.Should().NotBeNull();
        read.CacheKey.Should().Be(expectedKey);
        var save = recorder.Saves.Should().ContainSingle().Subject;
        save.CacheType.Should().Be(LightRagCacheKeyBuilder.ExtractCacheType);
        save.CacheKey.Should().Be(expectedKey);
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenCacheMiss_RecordsLookupDurationWithoutFactoryTime()
    {
        var store = new InMemoryKvStore();
        var recorder = new RecordingCacheMetricsRecorder();
        var service = CreateService(store, recorder: recorder);

        await service.GetOrCreateExtractAsync(
            "canonical extract prompt",
            "chunk-a",
            async _ =>
            {
                await Task.Delay(80);
                return "raw extract response";
            });

        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.Outcome.Should().Be(CacheReadOutcome.Miss);
        read.FactoryDuration.Should().NotBeNull();
        read.Duration.Should().BeLessThan(read.FactoryDuration!.Value);
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenIndexingCacheDisabled_DoesNotReadStore()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical extract prompt";
        store.Seed(
            keyBuilder.BuildExtractKey(canonicalPrompt),
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(
            store,
            new LightRAGOptions
            {
                EnableLlmCache = true,
                EnableLlmCacheForEntityExtract = false
            },
            keyBuilder);

        var result = await service.GetOrCreateExtractAsync(
            canonicalPrompt,
            "chunk-a",
            _ => Task.FromResult("factory extract response"));

        result.Value.Should().Be("factory extract response");
        result.CacheEnabled.Should().BeFalse();
        store.GetByIdCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateExtractAsync_WhenGlobalCacheDisabled_DoesNotWriteStore()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(
            store,
            new LightRAGOptions
            {
                EnableLlmCache = false,
                EnableLlmCacheForEntityExtract = true
            });

        var result = await service.GetOrCreateExtractAsync(
            "canonical extract prompt",
            "chunk-a",
            _ => Task.FromResult("raw extract response"));

        result.Value.Should().Be("raw extract response");
        result.CacheKey.Should().BeNull();
        store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateSummaryAsync_WhenCacheMiss_PersistsChunkIdAsNull()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(store, keyBuilder: keyBuilder);
        const string canonicalPrompt = "canonical summary prompt";

        var result = await service.GetOrCreateSummaryAsync(
            canonicalPrompt,
            _ => Task.FromResult("summary result"));
        var key = result.CacheKey;

        key.Should().Be(keyBuilder.BuildSummaryKey(canonicalPrompt));
        store.Items.Should().ContainKey(key!);
        var entry = store.Items[key!];
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        entry["chunk_id"].Should().BeNull();
        entry["original_prompt"].Should().Be(canonicalPrompt);
        entry["return"].Should().Be("summary result");
    }

    [Fact]
    public async Task GetOrCreateSummaryAsync_WhenIndexingCacheDisabled_CallsFactoryWithoutSaving()
    {
        var store = new InMemoryKvStore();
        var recorder = new RecordingCacheMetricsRecorder();
        var service = CreateService(
            store,
            new LightRAGOptions
            {
                EnableLlmCache = true,
                EnableLlmCacheForEntityExtract = false
            },
            recorder: recorder);
        var factoryCalls = 0;

        var result = await service.GetOrCreateSummaryAsync(
            "canonical summary prompt",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("summary result");
            });

        result.Value.Should().Be("summary result");
        result.CacheEnabled.Should().BeFalse();
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeFalse();
        result.CacheKey.Should().BeNull();
        factoryCalls.Should().Be(1);
        store.Items.Should().BeEmpty();
        store.GetByIdCalls.Should().BeEmpty();
        store.UpsertCalls.Should().BeEmpty();
        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.CacheType.Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        read.Outcome.Should().Be(CacheReadOutcome.Disabled);
        read.FactoryDuration.Should().NotBeNull();
        read.CacheKey.Should().BeNull();
        recorder.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateSummaryAsync_WhenCacheEntryInvalid_RecordsInvalidAndSavesFactoryValue()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        const string canonicalPrompt = "canonical summary prompt";
        var key = keyBuilder.BuildSummaryKey(canonicalPrompt);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);
        var factoryCalls = 0;

        var result = await service.GetOrCreateSummaryAsync(
            canonicalPrompt,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("summary result");
            });

        result.Value.Should().Be("summary result");
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.CacheKey.Should().Be(key);
        factoryCalls.Should().Be(1);
        store.Items[key]["cache_type"].Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.CacheType.Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        read.Outcome.Should().Be(CacheReadOutcome.Invalid);
        read.FactoryDuration.Should().NotBeNull();
        read.CacheKey.Should().Be(key);
        var save = recorder.Saves.Should().ContainSingle().Subject;
        save.CacheType.Should().Be(LightRagCacheKeyBuilder.SummaryCacheType);
        save.CacheKey.Should().Be(key);
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenCacheTypeMismatch_RecordsInvalidAndCallsFactory()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        var queryParam = new QueryParam { Mode = QueryMode.Mix };
        var keywords = new KeywordsResult();
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 1, "what is cache?", queryParam, keywords);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "wrong entry",
                LightRagCacheKeyBuilder.SummaryCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);

        var result = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            1,
            "what is cache?",
            queryParam,
            keywords,
            _ => Task.FromResult("factory answer"));

        result.Value.Should().Be("factory answer");
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        recorder.Reads.Should().ContainSingle().Which.Outcome.Should().Be(CacheReadOutcome.Invalid);
        recorder.Saves.Should().ContainSingle();
    }

    [Fact]
    public async Task GetOrCreateKeywordsAsync_WhenPayloadMalformed_RecordsInvalidAndSavesFactoryValue()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        var key = keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, "what is cache?");
        store.Seed(
            key,
            new LightRagCacheEntry(
                """{"highLevelKeywords":["wrong-shape"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);

        var result = await service.GetOrCreateKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            "what is cache?",
            _ => Task.FromResult(new KeywordsResult
            {
                HighLevelKeywords = ["rag"],
                LowLevelKeywords = ["cache"]
            }));

        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.Value.HighLevelKeywords.Should().Equal("rag");
        recorder.Reads.Should().ContainSingle().Which.Outcome.Should().Be(CacheReadOutcome.Invalid);
        recorder.Saves.Should().ContainSingle().Which.CacheType.Should().Be(LightRagCacheKeyBuilder.KeywordsCacheType);
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenLookupThrows_RecordsErrorWithoutSaving()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var recorder = new RecordingCacheMetricsRecorder();
        var queryParam = new QueryParam { Mode = QueryMode.Mix };
        var keywords = new KeywordsResult();
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 1, "what is cache?", queryParam, keywords);
        store.ThrowOnGetKey = key;
        var service = CreateService(store, keyBuilder: keyBuilder, recorder: recorder);
        var factoryCalls = 0;

        var result = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            1,
            "what is cache?",
            queryParam,
            keywords,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult("factory answer");
            });

        result.Value.Should().Be("factory answer");
        result.Hit.Should().BeFalse();
        result.Saved.Should().BeFalse();
        result.CacheKey.Should().BeNull();
        factoryCalls.Should().Be(1);
        store.UpsertCalls.Should().BeEmpty();
        var read = recorder.Reads.Should().ContainSingle().Subject;
        read.CacheType.Should().Be(LightRagCacheKeyBuilder.QueryCacheType);
        read.Outcome.Should().Be(CacheReadOutcome.Error);
        read.FactoryDuration.Should().NotBeNull();
        read.CacheKey.Should().Be(key);
        recorder.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenFactoryThrowsOnMiss_DoesNotRetryFactoryOrSave()
    {
        var store = new InMemoryKvStore();
        var recorder = new RecordingCacheMetricsRecorder();
        var queryParam = new QueryParam { Mode = QueryMode.Mix };
        var keywords = new KeywordsResult();
        var service = CreateService(store, recorder: recorder);
        var factoryCalls = 0;

        var act = async () => await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            1,
            "what is cache?",
            queryParam,
            keywords,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("factory failed");
            });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("factory failed");
        factoryCalls.Should().Be(1);
        store.UpsertCalls.Should().BeEmpty();
        recorder.Reads.Should().NotContain(read => read.Outcome == CacheReadOutcome.Error);
        recorder.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateSummaryAsync_WhenCacheTypeMismatch_CallsFactoryAndSavesFallback()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        const string canonicalPrompt = "canonical summary prompt";
        store.Seed(
            keyBuilder.BuildSummaryKey(canonicalPrompt),
            new LightRagCacheEntry(
                "raw extract response",
                LightRagCacheKeyBuilder.ExtractCacheType,
                canonicalPrompt,
                null,
                123,
                "chunk-a")
            .ToDictionary());
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.GetOrCreateSummaryAsync(
            canonicalPrompt,
            _ => Task.FromResult("summary result"));

        result.Hit.Should().BeFalse();
        result.Saved.Should().BeTrue();
        result.Value.Should().Be("summary result");
    }

    [Fact]
    public async Task GetWorkspaceQueryRevisionAsync_WhenMissing_ReturnsZero()
    {
        var service = CreateService(new InMemoryKvStore());

        var result = await service.GetWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(0);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_IncrementsRevision()
    {
        var store = new InMemoryKvStore();
        var service = CreateService(store);

        var first = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");
        var second = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        first.Should().Be(1);
        second.Should().Be(2);
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(2);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_WhenRevisionReadFails_DoesNotOverwriteExistingRevision()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var revisionKey = keyBuilder.BuildRevisionKey("workspace-a");
        store.Seed(revisionKey, new Dictionary<string, object>
        {
            ["revision"] = 5L,
            ["updated_at"] = "2026-05-19T00:00:00.0000000Z"
        });
        store.ThrowOnGetKey = revisionKey;
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(0);
        store.UpsertCalls.Should().BeEmpty();
        store.ThrowOnGetKey = null;
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(5);
    }

    [Fact]
    public async Task BumpWorkspaceQueryRevisionAsync_WhenRevisionWriteFails_ReturnsCurrentRevision()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var revisionKey = keyBuilder.BuildRevisionKey("workspace-a");
        store.Seed(revisionKey, new Dictionary<string, object>
        {
            ["revision"] = 2L,
            ["updated_at"] = "2026-05-19T00:00:00.0000000Z"
        });
        store.ThrowOnUpsertKey = revisionKey;
        var service = CreateService(store, keyBuilder: keyBuilder);

        var result = await service.BumpWorkspaceQueryRevisionAsync("workspace-a");

        result.Should().Be(2);
        (await service.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateQueryResponseAsync_WhenCacheDisabled_ReturnsFactoryValueWithoutReading()
    {
        var store = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var queryParam = new QueryParam { Mode = QueryMode.Mix };
        var keywords = new KeywordsResult();
        var key = keyBuilder.BuildRagQueryKey("workspace-a", 1, "what is cache?", queryParam, keywords);
        store.Seed(
            key,
            new LightRagCacheEntry(
                "cached answer",
                LightRagCacheKeyBuilder.QueryCacheType,
                "what is cache?",
                null,
                123)
            .ToDictionary());
        var service = CreateService(
            store,
            new LightRAGOptions { EnableLlmCache = false },
            keyBuilder);

        var result = await service.GetOrCreateQueryResponseAsync(
            "workspace-a",
            1,
            "what is cache?",
            queryParam,
            keywords,
            _ => Task.FromResult("factory answer"));

        result.Value.Should().Be("factory answer");
        result.CacheEnabled.Should().BeFalse();
        result.CacheKey.Should().BeNull();
        store.GetByIdCalls.Should().BeEmpty();
    }

    private static LightRagLlmCacheService CreateService(
        InMemoryKvStore store,
        LightRAGOptions? options = null,
        LightRagCacheKeyBuilder? keyBuilder = null,
        ICacheMetricsRecorder? recorder = null)
    {
        return new LightRagLlmCacheService(
            store,
            Options.Create(options ?? new LightRAGOptions()),
            keyBuilder ?? new LightRagCacheKeyBuilder(),
            recorder ?? new NoopCacheMetricsRecorder(),
            NullLogger<LightRagLlmCacheService>.Instance);
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

    private sealed class RecordingCacheMetricsRecorder : ICacheMetricsRecorder
    {
        public List<RecordedRead> Reads { get; } = [];

        public List<RecordedSave> Saves { get; } = [];

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
            Reads.Add(new RecordedRead(
                workspace,
                cacheType,
                outcome,
                mode,
                duration,
                factoryDuration,
                cacheKey,
                revision));
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
            Saves.Add(new RecordedSave(workspace, cacheType, mode, duration, cacheKey, revision));
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

    private sealed record RecordedRead(
        string Workspace,
        string CacheType,
        string Outcome,
        string? Mode,
        TimeSpan Duration,
        TimeSpan? FactoryDuration,
        string? CacheKey,
        long? Revision);

    private sealed record RecordedSave(
        string Workspace,
        string CacheType,
        string? Mode,
        TimeSpan Duration,
        string? CacheKey,
        long? Revision);
}
