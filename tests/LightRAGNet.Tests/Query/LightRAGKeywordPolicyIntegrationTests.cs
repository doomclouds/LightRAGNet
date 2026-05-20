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

namespace LightRAGNet.Tests.Query;

public sealed class LightRAGKeywordPolicyIntegrationTests
{
    private const string NoContextMessage = "Sorry, I'm not able to provide an answer to that question.[no-context]";

    [Fact]
    public async Task QueryAsync_WhenKgKeywordsEmptyAndLongQuery_ReturnsNoContextWithoutRetrieval()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService.ExtractKeywordsAsync(
                Arg.Any<string>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>())
            .Returns(new KeywordsResult());
        var embeddingService = Substitute.For<IEmbeddingService>();
        var vectorStore = Substitute.For<IVectorStore>();
        var rag = CreateLightRag(
            llmService,
            embeddingService: embeddingService,
            vectorStore: vectorStore);
        var query = new string('a', 50);

        var result = await rag.QueryAsync(query, new QueryParam { Mode = QueryMode.Mix });

        result.Content.Should().Be(NoContextMessage);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
        await embeddingService.DidNotReceiveWithAnyArgs().GenerateEmbeddingAsync(default!);
        await vectorStore.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default);
    }

    [Fact]
    public async Task QueryAsync_WhenNaive_RoutesAroundKgKeywordPolicy()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Naive queries should skip keyword extraction."));
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore
            .QueryAsync(
                "chunks",
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<float[]?>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new SearchResult
                {
                    Id = "chunk-a",
                    Content = "naive vector context",
                    Metadata = new Dictionary<string, object>
                    {
                        ["file_path"] = "docs/a.md"
                    }
                }
            ]);
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);
        var queryParam = new QueryParam
        {
            Mode = QueryMode.Naive,
            OnlyNeedContext = true,
            EnableRerank = false
        };

        var result = await rag.QueryAsync(new string('a', 50), queryParam);

        result.Content.Should().Contain("naive vector context");
        result.Metadata["query_mode"].Should().Be("Naive");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    private static LightRAG CreateLightRag(
        ILLMService llmService,
        IEmbeddingService? embeddingService = null,
        IVectorStore? vectorStore = null)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        var tokenizer = new FakeTokenizer();
        var graphStore = new InMemoryGraphStore();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        var fullEntitiesStore = new InMemoryKvStore();
        var fullRelationsStore = new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();
        var statusStore = Substitute.For<IDocumentStatusStore>();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            options,
            NullLogger<DocumentLifecycleService>.Instance);

        embeddingService ??= Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        vectorStore ??= Substitute.For<IVectorStore>();
        var cacheKeyBuilder = new LightRagCacheKeyBuilder();
        var llmCacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            cacheKeyBuilder,
            NullLogger<LightRagLlmCacheService>.Instance);

        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheService,
            cacheKeyBuilder,
            options,
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
            options,
            llmCacheService,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);

        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankService,
            tokenizer,
            textChunksStore,
            options,
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
        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(vectorStore, rerankService, tokenizer),
            llmCacheService,
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
    }
}
