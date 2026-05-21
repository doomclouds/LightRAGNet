using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRAGKeywordCacheIntegrationTests
{
    [Fact]
    public async Task QueryAsync_WhenKeywordCacheHit_UsesCachedKeywordsAndSkipsExtraction()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped on cache hit."));
        var fixture = CreateLightRag(llmService: llmService);
        var query = "what does cached retrieval use?";
        await fixture.CacheService.SaveKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            query,
            new KeywordsResult
            {
                HighLevelKeywords = ["cached-high"],
                LowLevelKeywords = ["cached-low"]
            });

        var result = await fixture.Rag.QueryAsync(query, ContextOnlyMix());

        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "cached-high" });
        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "cached-low" });
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenKeywordCacheMiss_SavesExtractedKeywords()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult
            {
                HighLevelKeywords = ["extracted-high"],
                LowLevelKeywords = ["extracted-low"]
            });
        var fixture = CreateLightRag(llmService: llmService);
        var query = "what should be saved?";

        var result = await fixture.Rag.QueryAsync(query, ContextOnlyMix());

        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "extracted-high" });
        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "extracted-low" });
        var cachedKeywords = await fixture.CacheService.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, query);
        cachedKeywords.Should().NotBeNull();
        cachedKeywords!.HighLevelKeywords.Should().Equal("extracted-high");
        cachedKeywords.LowLevelKeywords.Should().Equal("extracted-low");
        await llmService.Received(1).ExtractKeywordsAsync(query, Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenKeywordCacheMalformed_FallsBackToLiveExtractionAndSavesKeywords()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult
            {
                HighLevelKeywords = ["live-high"],
                LowLevelKeywords = ["live-low"]
            });
        var fixture = CreateLightRag(llmService: llmService);
        var query = "what should fall back?";
        var keyBuilder = new LightRagCacheKeyBuilder();
        fixture.LlmCacheStore.Seed(
            keyBuilder.BuildKeywordKey("workspace-a", QueryMode.Mix, query),
            new LightRagCacheEntry(
                """{"highLevelKeywords":["wrong-shape"]}""",
                LightRagCacheKeyBuilder.KeywordsCacheType,
                query,
                null,
                123)
            .ToDictionary());

        var result = await fixture.Rag.QueryAsync(query, ContextOnlyMix());

        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "live-high" });
        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "live-low" });
        var cachedKeywords = await fixture.CacheService.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, query);
        cachedKeywords.Should().NotBeNull();
        cachedKeywords!.HighLevelKeywords.Should().Equal("live-high");
        cachedKeywords.LowLevelKeywords.Should().Equal("live-low");
        await llmService.Received(1).ExtractKeywordsAsync(query, Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenLiveKeywordsNormalizeForKg_SavesNormalizedKeywords()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult());
        var fixture = CreateLightRag(llmService: llmService);
        var query = "short cache query";

        var result = await fixture.Rag.QueryAsync(query, ContextOnlyMix());

        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { query });
        var cachedKeywords = await fixture.CacheService.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, query);
        cachedKeywords.Should().NotBeNull();
        cachedKeywords!.HighLevelKeywords.Should().BeEmpty();
        cachedKeywords.LowLevelKeywords.Should().Equal(query);
    }

    [Fact]
    public async Task QueryAsync_WhenExplicitKeywordsProvided_SkipsKeywordCacheAndExtraction()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Explicit keywords should skip extraction."));
        var fixture = CreateLightRag(llmService: llmService);
        var query = "what should explicit keywords use?";
        await fixture.CacheService.SaveKeywordsAsync(
            "workspace-a",
            QueryMode.Mix,
            query,
            new KeywordsResult
            {
                HighLevelKeywords = ["cached-high"],
                LowLevelKeywords = ["cached-low"]
            });
        fixture.LlmCacheStore.UpsertCalls.Clear();

        var result = await fixture.Rag.QueryAsync(
            query,
            ContextOnlyMix(
                highLevelKeywords: ["explicit-high"],
                lowLevelKeywords: ["explicit-low"]));

        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "explicit-high" });
        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "explicit-low" });
        fixture.LlmCacheStore.GetByIdCalls.Should().BeEmpty();
        fixture.LlmCacheStore.UpsertCalls.Should().BeEmpty();
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public async Task QueryAsync_WhenNaiveOrBypass_SkipsKeywordCache(QueryMode mode)
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Naive and Bypass should skip keyword extraction."));
        llmService
            .GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("direct answer");
        var fixture = CreateLightRag(llmService: llmService);

        var result = await fixture.Rag.QueryAsync(
            "what should not use keyword cache?",
            new QueryParam
            {
                Mode = mode,
                OnlyNeedContext = true,
                EnableRerank = false
            });

        result.Content.Should().NotBeNull();
        fixture.LlmCacheStore.GetByIdCalls.Should().BeEmpty();
        fixture.LlmCacheStore.UpsertCalls.Should().BeEmpty();
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenKeywordCacheDisabled_ExtractsKeywordsWithoutSaving()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult
            {
                HighLevelKeywords = ["disabled-high"],
                LowLevelKeywords = ["disabled-low"]
            });
        var fixture = CreateLightRag(
            options: new LightRAGOptions
            {
                Workspace = "workspace-a",
                EnableKeywordCache = false
            },
            llmService: llmService);
        var query = "what should not be saved?";

        var result = await fixture.Rag.QueryAsync(query, ContextOnlyMix());

        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "disabled-high" });
        result.Metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "disabled-low" });
        (await fixture.CacheService.TryGetKeywordsAsync("workspace-a", QueryMode.Mix, query)).Should().BeNull();
        fixture.LlmCacheStore.UpsertCalls.Should().BeEmpty();
        await llmService.Received(1).ExtractKeywordsAsync(query, Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    private static QueryParam ContextOnlyMix(
        List<string>? highLevelKeywords = null,
        List<string>? lowLevelKeywords = null)
    {
        return new QueryParam
        {
            Mode = QueryMode.Mix,
            OnlyNeedContext = true,
            EnableRerank = false,
            HighLevelKeywords = highLevelKeywords ?? [],
            LowLevelKeywords = lowLevelKeywords ?? []
        };
    }

    private static LightRagFixture CreateLightRag(
        LightRAGOptions? options = null,
        ILLMService? llmService = null)
    {
        options ??= new LightRAGOptions { Workspace = "workspace-a" };
        options.Workspace = "workspace-a";
        var optionsMonitor = Options.Create(options);
        var tokenizer = new FakeTokenizer();
        var graphStore = new InMemoryGraphStore();
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed(
            "chunks",
            new VectorDocument
            {
                Id = "chunk-a",
                Content = "cached keyword integration context",
                Vector = [1.0f, 0.5f],
                Metadata = new Dictionary<string, object>
                {
                    ["file_path"] = "docs/cache.md"
                }
            });
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        var fullEntitiesStore = new InMemoryKvStore();
        var fullRelationsStore = new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();
        llmService ??= Substitute.For<ILLMService>();
        var cacheKeyBuilder = new LightRagCacheKeyBuilder();
        var cacheService = new LightRagLlmCacheService(
            llmCacheStore,
            optionsMonitor,
            cacheKeyBuilder,
            NullLogger<LightRagLlmCacheService>.Instance);
        var statusStore = Substitute.For<IDocumentStatusStore>();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            optionsMonitor,
            NullLogger<DocumentLifecycleService>.Instance);

        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            cacheService,
            cacheKeyBuilder,
            optionsMonitor,
            NullLogger<DocumentProcessingService>.Instance);

        var loggerFactory = NullLoggerFactory.Instance;
        var knowledgeGraphMergeService = new KnowledgeGraphMergeService(
            graphStore,
            vectorStore,
            embeddingService,
            llmService,
            tokenizer,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            optionsMonitor,
            cacheService,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);

        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService,
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);
        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankCoordinator,
            tokenizer,
            textChunksStore,
            optionsMonitor,
            loggerFactory);

        var documentDeletionService = new DocumentDeletionService(
            vectorStore,
            graphStore,
            embeddingService,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            NullLogger<DocumentDeletionService>.Instance);

        var rag = new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(
                vectorStore,
                rerankCoordinator,
                tokenizer),
            cacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);

        return new LightRagFixture(rag, cacheService, llmCacheStore);
    }

    private sealed record LightRagFixture(
        LightRAG Rag,
        LightRagLlmCacheService CacheService,
        InMemoryKvStore LlmCacheStore);
}
