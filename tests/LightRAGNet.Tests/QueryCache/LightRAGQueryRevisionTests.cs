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

public sealed class LightRAGQueryRevisionTests
{
    [Fact]
    public async Task InsertAsync_WhenNewDocumentSucceeds_BumpsWorkspaceQueryRevision()
    {
        var fixture = CreateLightRag();

        var result = await fixture.Rag.InsertAsync(
            "alpha beta gamma delta epsilon",
            docId: "doc-new",
            filePath: "new.md");

        result.Should().Be("doc-new");
        (await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WhenProcessedDuplicate_DoesNotBumpWorkspaceQueryRevision()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync(
            "original content",
            docId: "doc-duplicate",
            filePath: "original.md");
        await lifecycleService.MarkProcessedAsync("workspace-a", "doc-duplicate");
        var fixture = CreateLightRag(lifecycleService: lifecycleService);
        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");

        var result = await fixture.Rag.InsertAsync(
            "replacement content",
            docId: "doc-duplicate",
            filePath: "replacement.md");

        result.Should().Be("doc-duplicate");
        (await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WhenProcessingFails_DoesNotBumpWorkspaceQueryRevision()
    {
        var vectorStore = new InMemoryVectorStore { ThrowOnUpsertCollection = "chunks" };
        var fixture = CreateLightRag(vectorStore: vectorStore);
        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");

        var act = async () => await fixture.Rag.InsertAsync(
            "alpha beta gamma delta epsilon",
            docId: "doc-fail",
            filePath: "fail.md");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(1);
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenIndexedDocumentSucceeds_BumpsWorkspaceQueryRevision()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync(
            "alpha beta gamma",
            docId: "doc-delete",
            filePath: "delete.md");
        await lifecycleService.RecordChunksAsync("workspace-a", "doc-delete",
        [
            new Chunk
            {
                Id = "chunk-a",
                Content = "alpha",
                FullDocId = "doc-delete",
                FilePath = "delete.md",
                Tokens = 1,
                ChunkOrderIndex = 0
            }
        ]);
        await lifecycleService.MarkProcessedAsync("workspace-a", "doc-delete");
        var textChunksStore = new InMemoryKvStore();
        textChunksStore.Seed("chunk-a", new() { ["content"] = "alpha" });
        var fullDocsStore = new InMemoryKvStore();
        fullDocsStore.Seed("doc-delete", new() { ["content"] = "alpha beta gamma" });
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha",
            Vector = [1.0f, 0.5f]
        });
        var fixture = CreateLightRag(
            lifecycleService: lifecycleService,
            textChunksStore: textChunksStore,
            fullDocsStore: fullDocsStore,
            vectorStore: vectorStore);
        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");

        var result = await fixture.Rag.DeleteDocumentAsync("doc-delete");

        result.Succeeded.Should().BeTrue();
        result.Found.Should().BeTrue();
        (await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(2);
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenDocumentIsUnknown_DoesNotBumpWorkspaceQueryRevision()
    {
        var fixture = CreateLightRag();
        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");

        var result = await fixture.Rag.DeleteDocumentAsync("missing-doc");

        result.Succeeded.Should().BeFalse();
        result.Found.Should().BeFalse();
        (await fixture.CacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(1);
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveCacheWasSavedBeforeRevisionBump_GeneratesFreshAnswer()
    {
        var query = "what is in the indexed document?";
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
            .Returns("fresh answer after revision bump");
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "fresh context",
            Vector = [1.0f, 0.5f],
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/fresh.md"
            }
        });
        var fixture = CreateLightRag(llmService: llmService, vectorStore: vectorStore);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            queryParam,
            new KeywordsResult(),
            "stale cached answer");

        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");
        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("fresh answer after revision bump");
        await llmService.Received(1).GenerateAsync(
            query,
            Arg.Any<string?>(),
            Arg.Any<List<ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenKgCacheWasSavedBeforeRevisionBump_GeneratesFreshAnswer()
    {
        var query = "what does the graph know?";
        var queryParam = new QueryParam { Mode = QueryMode.Mix, EnableRerank = false };
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["graph"],
            LowLevelKeywords = ["know"]
        };
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(query, Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(keywords);
        llmService
            .GenerateAsync(
                query,
                Arg.Any<string?>(),
                Arg.Any<List<ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("fresh kg answer after revision bump");
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "fresh graph context",
            Vector = [1.0f, 0.5f],
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/kg.md"
            }
        });
        var fixture = CreateLightRag(llmService: llmService, vectorStore: vectorStore);
        await fixture.CacheService.SaveQueryResponseAsync(
            "workspace-a",
            0,
            query,
            queryParam,
            keywords,
            "stale kg cached answer");

        await fixture.CacheService.BumpWorkspaceQueryRevisionAsync("workspace-a");
        var result = await fixture.Rag.QueryAsync(query, queryParam);

        result.Content.Should().Be("fresh kg answer after revision bump");
        await llmService.Received(1).GenerateAsync(
            query,
            Arg.Any<string?>(),
            Arg.Any<List<ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    private static LightRagFixture CreateLightRag(
        DocumentLifecycleService? lifecycleService = null,
        IKVStore? textChunksStore = null,
        IKVStore? fullDocsStore = null,
        IKVStore? fullEntitiesStore = null,
        IKVStore? fullRelationsStore = null,
        InMemoryVectorStore? vectorStore = null,
        ILLMService? llmService = null)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        var tokenizer = new FakeTokenizer();
        lifecycleService ??= CreateLifecycleService(new InMemoryDocumentStatusStore());
        var hasProvidedLlmService = llmService is not null;
        llmService ??= Substitute.For<ILLMService>();
        if (!hasProvidedLlmService)
        {
            llmService.ExtractEntitiesAsync(
                    Arg.Any<string>(),
                    Arg.Any<List<string>>(),
                    Arg.Any<float>(),
                    Arg.Any<int?>(),
                    Arg.Any<int?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new EntityExtractionResult());
        }

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        vectorStore ??= new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var rerankService = Substitute.For<IRerankService>();
        textChunksStore ??= new InMemoryKvStore();
        fullDocsStore ??= new InMemoryKvStore();
        fullEntitiesStore ??= new InMemoryKvStore();
        fullRelationsStore ??= new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();

        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheStore,
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
        var cacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);

        var rag = new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(vectorStore, rerankService, tokenizer),
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

        return new LightRagFixture(rag, cacheService);
    }

    private static DocumentLifecycleService CreateLifecycleService(InMemoryDocumentStatusStore statusStore)
    {
        return new DocumentLifecycleService(
            statusStore,
            Options.Create(new LightRAGOptions
            {
                Workspace = "workspace-a"
            }),
            NullLogger<DocumentLifecycleService>.Instance);
    }

    private sealed record LightRagFixture(
        LightRAG Rag,
        LightRagLlmCacheService CacheService);
}
