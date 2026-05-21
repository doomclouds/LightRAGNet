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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.QueryCache;

public sealed class LightRAGQueryCacheIntegrationTests
{
    [Fact]
    public async Task QueryAsync_WhenKgNonStreamingCacheHit_SkipsFinalGenerateAsync()
    {
        var query = "what does query answer cache use?";
        var queryParam = new QueryParam { Mode = QueryMode.Mix, EnableRerank = false };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["cache"],
            LowLevelKeywords = ["answer"]
        };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(keywords);
        llmService
            .GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("Final GenerateAsync should be skipped."));
        var fixture = CreateLightRag(llmService: llmService);
        var revision = await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a");
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            revision,
            query,
            queryParam,
            keywords,
            "cached kg answer");

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("cached kg answer");
        result.Metadata["query_mode"].Should().Be("Mix");
        result.Metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "cache" });
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveNonStreamingCacheHit_SkipsFinalGenerateAsync()
    {
        var query = "what does naive cache use?";
        var queryParam = new QueryParam { Mode = QueryMode.Naive, EnableRerank = false };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("Final GenerateAsync should be skipped."));
        var fixture = CreateLightRag(llmService: llmService);
        var revision = await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a");
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            revision,
            query,
            queryParam,
            new KeywordsResult(),
            "cached naive answer");

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("cached naive answer");
        result.Metadata["query_mode"].Should().Be("Naive");
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenBypassNonStreamingCacheHit_SkipsFinalGenerateAsync()
    {
        var query = "what does bypass cache use?";
        var queryParam = new QueryParam { Mode = QueryMode.Bypass };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("Final GenerateAsync should be skipped."));
        var fixture = CreateLightRag(llmService: llmService);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            queryParam,
            new KeywordsResult(),
            "cached bypass answer");

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("cached bypass answer");
        result.Metadata["query_mode"].Should().Be("Bypass");
        result.RawData.Should().NotBeNull();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenCacheMiss_StoresGeneratedNonStreamingResponse()
    {
        var query = "what should be stored after miss?";
        var queryParam = new QueryParam { Mode = QueryMode.Naive, EnableRerank = false };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateAsync(
                query,
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("live naive answer");
        var fixture = CreateLightRag(llmService: llmService);

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("live naive answer");
        var cached = await fixture.CacheService.TryGetQueryResponseAsync(
            "workspace-a",
            await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a"),
            query,
            queryParam,
            new KeywordsResult());
        cached.Should().Be("live naive answer");
        await llmService.Received(1).GenerateAsync(
            query,
            Arg.Any<string?>(),
            Arg.Any<List<ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenRagRevisionReadFails_SkipsRevisionZeroCache()
    {
        var query = "revision read should not trust zero";
        var queryParam = new QueryParam { Mode = QueryMode.Naive, EnableRerank = false };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateAsync(
                query,
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("fresh answer");
        var fixture = CreateLightRag(llmService: llmService);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            queryParam,
            new KeywordsResult(),
            "stale cached answer");
        fixture.LlmCacheStore.ThrowOnGetKey = new LightRagCacheKeyBuilder().BuildRevisionKey("workspace-a");

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("fresh answer");
        await llmService.Received(1).GenerateAsync(
            query,
            Arg.Any<string?>(),
            Arg.Any<List<ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenStreamTrue_SkipsQueryAnswerCache()
    {
        var query = "stream should bypass answer cache";
        var queryParam = new QueryParam { Mode = QueryMode.Bypass, Stream = true };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateStreamAsync(
                query,
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(AsyncValues("live chunk"));
        var fixture = CreateLightRag(llmService: llmService);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            new QueryParam { Mode = QueryMode.Bypass },
            new KeywordsResult(),
            "cached answer");
        fixture.LlmCacheStore.GetByIdCalls.Clear();

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.IsStreaming.Should().BeTrue();
        (await ReadAllAsync(result.ResponseIterator)).Should().Equal("live chunk");
        fixture.LlmCacheStore.GetByIdCalls.Should().BeEmpty();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Theory]
    [InlineData(true, false, "")]
    [InlineData(false, true, "prompt should bypass answer cache")]
    public async Task QueryAsync_WhenOnlyNeedContextOrOnlyNeedPrompt_SkipsQueryAnswerCache(
        bool onlyNeedContext,
        bool onlyNeedPrompt,
        string expectedContent)
    {
        var query = "partial query should bypass answer cache";
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Bypass,
            OnlyNeedContext = onlyNeedContext,
            OnlyNeedPrompt = onlyNeedPrompt
        };
        var llmService = Substitute.For<ILLMService>();
        var fixture = CreateLightRag(llmService: llmService);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            new QueryParam { Mode = QueryMode.Bypass },
            new KeywordsResult(),
            "cached answer");
        fixture.LlmCacheStore.GetByIdCalls.Clear();

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be(onlyNeedPrompt ? query : expectedContent);
        result.Metadata["query_mode"].Should().Be("Bypass");
        fixture.LlmCacheStore.GetByIdCalls.Should().BeEmpty();
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenConversationHistoryNonEmpty_SkipsQueryAnswerCache()
    {
        var query = "history should bypass answer cache";
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Bypass,
            ConversationHistory = [new ChatMessage(ChatRole.User, "previous question")]
        };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .GenerateAsync(
                query,
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("live answer with history");
        var fixture = CreateLightRag(llmService: llmService);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            new QueryParam { Mode = QueryMode.Bypass },
            new KeywordsResult(),
            "cached answer");
        fixture.LlmCacheStore.GetByIdCalls.Clear();

        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("live answer with history");
        fixture.LlmCacheStore.GetByIdCalls.Should().BeEmpty();
        await llmService.Received(1).GenerateAsync(
            query,
            Arg.Any<string?>(),
            queryParam.ConversationHistory,
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
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
                Content = "query cache integration context",
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

        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankService,
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

        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rag = new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(
                vectorStore,
                new RerankCoordinator(
                    rerankService,
                    new RerankDocumentChunker(tokenizer, rerankOptions),
                    rerankOptions),
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

    private static async IAsyncEnumerable<string> AsyncValues(params string[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async Task<List<string>> ReadAllAsync(IAsyncEnumerable<string>? values)
    {
        values.Should().NotBeNull();

        var result = new List<string>();
        await foreach (var value in values!)
        {
            result.Add(value);
        }

        return result;
    }

    private sealed record LightRagFixture(
        LightRAG Rag,
        LightRagLlmCacheService CacheService,
        InMemoryKvStore LlmCacheStore);
}
