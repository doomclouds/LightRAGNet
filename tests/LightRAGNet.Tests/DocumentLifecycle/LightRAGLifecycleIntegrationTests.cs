using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
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

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class LightRAGLifecycleIntegrationTests
{
    [Fact]
    public async Task InsertAsync_ProcessedLifecycleStatus_DoesNotProcessDocument()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync("original content", docId: "doc-duplicate", filePath: "original.md");
        await lifecycleService.MarkProcessedAsync("workspace-a", "doc-duplicate");
        var fullDocsStore = Substitute.For<IKVStore>();
        var rag = CreateLightRag(
            lifecycleService,
            fullDocsStore: fullDocsStore,
            tokenizer: new ThrowingTokenizer());

        var result = await rag.InsertAsync(
            "replacement content",
            docId: "doc-duplicate",
            filePath: "replacement.md");

        result.Should().Be("doc-duplicate");
        await fullDocsStore.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
        await fullDocsStore.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task InsertAsync_ProcessedLifecycleStatusWithCompleteChunkVectors_DoesNotProcessDocument()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync("original content", docId: "doc-complete", filePath: "original.md");
        await lifecycleService.RecordChunksAsync("workspace-a", "doc-complete",
        [
            new Chunk
            {
                Id = "chunk-present",
                Content = "original",
                FullDocId = "doc-complete",
                FilePath = "original.md",
                Tokens = 1,
                ChunkOrderIndex = 0
            }
        ]);
        await lifecycleService.MarkProcessedAsync("workspace-a", "doc-complete");
        var fullDocsStore = Substitute.For<IKVStore>();
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-present",
            Content = "original",
            Vector = [1.0f]
        });
        var rag = CreateLightRag(
            lifecycleService,
            fullDocsStore: fullDocsStore,
            vectorStore: vectorStore,
            tokenizer: new ThrowingTokenizer());

        var result = await rag.InsertAsync(
            "replacement content",
            docId: "doc-complete",
            filePath: "replacement.md");

        result.Should().Be("doc-complete");
        vectorStore.UpsertCalls.Should().BeEmpty();
        await fullDocsStore.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task InsertAsync_ProcessedLifecycleStatusWithMissingChunkVectors_RebuildsVectorsWithExtractCache()
    {
        var content = "alpha beta gamma delta epsilon";
        var docId = "doc-repair";
        var firstChunkId = HashUtils.ComputeMd5Hash("t1 t2 t3", "chunk-");
        var secondChunkId = HashUtils.ComputeMd5Hash("t1 t2 t3 t5", "chunk-");
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync(content, docId: docId, filePath: "original.md");
        await lifecycleService.RecordChunksAsync("workspace-a", docId,
        [
            new Chunk
            {
                Id = firstChunkId,
                Content = "t1 t2 t3",
                FullDocId = docId,
                FilePath = "original.md",
                Tokens = 3,
                ChunkOrderIndex = 0
            },
            new Chunk
            {
                Id = secondChunkId,
                Content = "t1 t2 t3 t5",
                FullDocId = docId,
                FilePath = "original.md",
                Tokens = 4,
                ChunkOrderIndex = 1
            }
        ]);
        await lifecycleService.MarkProcessedAsync("workspace-a", docId);
        var llmCacheStore = new InMemoryKvStore();
        var cacheKeyBuilder = new LightRagCacheKeyBuilder();
        SeedExtractCache(llmCacheStore, cacheKeyBuilder, "t1 t2 t3", firstChunkId);
        SeedExtractCache(llmCacheStore, cacheKeyBuilder, "t1 t2 t3 t5", secondChunkId);
        var vectorStore = new InMemoryVectorStore();
        var llmService = Substitute.For<ILLMService>();
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        var rag = CreateLightRag(
            lifecycleService,
            vectorStore: vectorStore,
            llmCacheStore: llmCacheStore,
            llmService: llmService,
            embeddingService: embeddingService);

        var result = await rag.InsertAsync(content, docId: docId, filePath: "replacement.md");

        result.Should().Be(docId);
        vectorStore.Get("chunks", firstChunkId).Should().NotBeNull();
        vectorStore.Get("chunks", secondChunkId).Should().NotBeNull();
        var expectedChunkIds = new[] { firstChunkId, secondChunkId };
        vectorStore.UpsertCalls.Should().Contain(call =>
            call.Collection == "chunks" &&
            call.Documents.Select(document => document.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedChunkIds));
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(
            default!,
            default!,
            default,
            default,
            default,
            default);
        await embeddingService.Received().GenerateEmbeddingAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertAsync_FailedLifecycleStatus_ReprocessesDocument()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync("old failed content", docId: "doc-retry", filePath: "old.md");
        await lifecycleService.MarkFailedAsync("workspace-a", "doc-retry", "process_chunks", "previous failure");
        var textChunksStore = Substitute.For<IKVStore>();
        var fullDocsStore = Substitute.For<IKVStore>();
        var vectorStore = Substitute.For<IVectorStore>();
        var rag = CreateLightRag(
            lifecycleService,
            textChunksStore: textChunksStore,
            fullDocsStore: fullDocsStore,
            vectorStore: vectorStore);
        var retryContent = "new retry alpha beta gamma delta";

        var result = await rag.InsertAsync(
            retryContent,
            docId: "doc-retry",
            filePath: "new.md");

        result.Should().Be("doc-retry");
        await fullDocsStore.Received(1).UpsertAsync(
            Arg.Is<Dictionary<string, Dictionary<string, object>>>(data =>
                data.ContainsKey("doc-retry")
                && data["doc-retry"]["content"].Equals(retryContent)
                && data["doc-retry"]["file_path"].Equals("new.md")),
            Arg.Any<CancellationToken>());
        await textChunksStore.Received(1).UpsertAsync(
            Arg.Is<Dictionary<string, Dictionary<string, object>>>(data =>
                data.Count > 0
                && data.Values.All(chunk =>
                    chunk["file_path"].Equals("new.md")
                    && chunk["full_doc_id"].Equals("doc-retry"))),
            Arg.Any<CancellationToken>());
        await vectorStore.Received(1).UpsertAsync(
            "chunks",
            Arg.Is<IEnumerable<VectorDocument>>(documents =>
                documents.Any()
                && documents.All(document =>
                    document.Metadata["file_path"].Equals("new.md")
                    && document.Metadata["full_doc_id"].Equals("doc-retry")
                    && document.Metadata["content"].ToString()!.StartsWith("t", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
        var status = await statusStore.GetAsync("workspace-a", "doc-retry");
        status.Should().NotBeNull();
        status!.Status.Should().Be(DocumentLifecycleStatus.Processed);
        status.ContentLength.Should().Be(retryContent.Length);
        status.ContentSummary.Should().Be(retryContent);
        status.FilePath.Should().Be("new.md");
        status.ErrorMessage.Should().BeEmpty();
        status.Metadata.Should().NotContainKey("failure_stage");
        status.ChunkSnapshots.Should().OnlyContain(snapshot => snapshot.FilePath == "new.md");
    }

    [Fact]
    public async Task InsertAsync_NewDocument_RecordsLifecycleChunksAndProcessedStatus()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        var fullDocsStore = Substitute.For<IKVStore>();
        var rag = CreateLightRag(lifecycleService, fullDocsStore: fullDocsStore);

        var result = await rag.InsertAsync(
            "alpha beta gamma delta epsilon",
            docId: "doc-new",
            filePath: "new.md");

        result.Should().Be("doc-new");
        var status = await statusStore.GetAsync("workspace-a", "doc-new");
        status.Should().NotBeNull();
        status!.Status.Should().Be(DocumentLifecycleStatus.Processed);
        status.FilePath.Should().Be("new.md");
        status.ChunksCount.Should().Be(2);
        status.ChunksList.Should().HaveCount(2);
        status.ChunkSnapshots.Should().OnlyContain(snapshot => snapshot.FilePath == "new.md");
    }

    [Fact]
    public async Task InsertAsync_WritesExtractCacheKeysToTextChunks()
    {
        var textChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();
        var llmService = Substitute.For<ILLMService>();
        llmService.GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("entity<|#|>Alpha<|#|>Concept<|#|>Alpha description\n<|COMPLETE|>");
        var rag = CreateLightRag(
            CreateLifecycleService(new InMemoryDocumentStatusStore()),
            textChunksStore: textChunksStore,
            llmCacheStore: llmCacheStore,
            llmService: llmService);

        await rag.InsertAsync("alpha beta gamma", docId: "doc-cache-list", filePath: "alpha.md");

        textChunksStore.Items.Should().NotBeEmpty();
        var chunk = textChunksStore.Items.Values.Single();
        chunk.Should().ContainKey("llm_cache_list");
        var cacheKeys = chunk["llm_cache_list"].Should().BeAssignableTo<List<object>>().Subject;
        cacheKeys.Should().ContainSingle(key =>
            key.ToString()!.StartsWith("default:extract:", StringComparison.Ordinal));
        llmCacheStore.Items.Should().ContainKey(cacheKeys.Single().ToString()!);
    }

    [Fact]
    public async Task InsertAsync_WhenExtractCacheDisabled_WritesEmptyLlmCacheListToTextChunks()
    {
        var textChunksStore = new InMemoryKvStore();
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 5,
            ChunkOverlapTokenSize = 1,
            EnableLlmCacheForEntityExtract = false
        });
        var rag = CreateLightRag(
            CreateLifecycleService(new InMemoryDocumentStatusStore()),
            textChunksStore: textChunksStore,
            options: options);

        await rag.InsertAsync("alpha beta gamma", docId: "doc-cache-disabled", filePath: "alpha.md");

        textChunksStore.Items.Should().NotBeEmpty();
        var chunk = textChunksStore.Items.Values.Single();
        chunk.Should().ContainKey("llm_cache_list");
        chunk["llm_cache_list"].Should().BeAssignableTo<List<object>>().Subject.Should().BeEmpty();
    }

    [Fact]
    public async Task InsertAsync_ProcessChunksFailure_MarksFailedAndPreservesChunkSnapshots()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<float[]>>(_ => throw new InvalidOperationException("embedding failed"));
        var rag = CreateLightRag(lifecycleService, embeddingService: embeddingService);

        var act = async () => await rag.InsertAsync(
            "alpha beta gamma delta epsilon",
            docId: "doc-failing",
            filePath: "failing.md");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("embedding failed");
        var status = await statusStore.GetAsync("workspace-a", "doc-failing");
        status.Should().NotBeNull();
        status!.Status.Should().Be(DocumentLifecycleStatus.Failed);
        status.ErrorMessage.Should().Be("embedding failed");
        status.Metadata.Should().Contain("failure_stage", "process_chunks");
        status.ChunksCount.Should().Be(2);
        status.ChunksList.Should().HaveCount(2);
        status.ChunkSnapshots.Should().OnlyContain(snapshot => snapshot.FilePath == "failing.md");
    }

    [Fact]
    public async Task DeleteDocumentAsync_ProcessedDocument_UsesLifecycleChunkIdsAndDeletesStorage()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync("alpha beta gamma", docId: "doc-delete", filePath: "delete.md");
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
        var textChunks = new InMemoryKvStore();
        textChunks.Seed("chunk-a", new() { ["content"] = "alpha" });
        var fullDocs = new InMemoryKvStore();
        fullDocs.Seed("doc-delete", new() { ["content"] = "alpha beta gamma" });
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha",
            Vector = [1.0f, 0.5f]
        });
        var emittedStages = new List<TaskStage>();
        var deletingDocumentObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rag = CreateLightRag(
            lifecycleService,
            textChunksStore: textChunks,
            fullDocsStore: fullDocs,
            vectorStore: vectorStore);
        rag.TaskStateChanged += (_, state) =>
        {
            emittedStages.Add(state.Stage);
            if (state.Stage == TaskStage.DeletingDocument && state.DocId == "doc-delete")
            {
                deletingDocumentObserved.TrySetResult();
            }
        };

        var result = await rag.DeleteDocumentAsync("doc-delete");
        await deletingDocumentObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        result.Found.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        result.Workspace.Should().Be("workspace-a");
        emittedStages.Should().Contain(TaskStage.DeletingDocument);
        textChunks.Items.Should().NotContainKey("chunk-a");
        fullDocs.Items.Should().NotContainKey("doc-delete");
        vectorStore.Get("chunks", "chunk-a").Should().BeNull();
        var status = await statusStore.GetAsync("workspace-a", "doc-delete");
        status.Should().BeNull();
    }

    [Fact]
    public async Task DeleteDocumentAsync_UnknownDocument_ReturnsNotFoundWithoutDeletingStorage()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        var textChunks = new InMemoryKvStore();
        textChunks.Seed("chunk-a", new() { ["content"] = "alpha" });
        var fullDocs = new InMemoryKvStore();
        fullDocs.Seed("doc-existing", new() { ["content"] = "alpha beta" });
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha",
            Vector = [1.0f, 0.5f]
        });
        var rag = CreateLightRag(
            lifecycleService,
            textChunksStore: textChunks,
            fullDocsStore: fullDocs,
            vectorStore: vectorStore);

        var result = await rag.DeleteDocumentAsync("missing-doc");

        result.Found.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.Workspace.Should().Be("workspace-a");
        textChunks.DeleteCalls.Should().BeEmpty();
        fullDocs.DeleteCalls.Should().BeEmpty();
        vectorStore.DeleteCalls.Should().BeEmpty();
        textChunks.Items.Should().ContainKey("chunk-a");
        fullDocs.Items.Should().ContainKey("doc-existing");
        vectorStore.Get("chunks", "chunk-a").Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteDocumentAsync_MissingLifecycleStatusButFullDocExists_DeletesStorage()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        var textChunks = new InMemoryKvStore();
        textChunks.Seed("chunk-orphan", new() { ["content"] = "orphan content" });
        var fullDocs = new InMemoryKvStore();
        fullDocs.Seed("doc-orphan", new()
        {
            ["content"] = "orphan content",
            ["chunks_list"] = new List<string> { "chunk-orphan" }
        });
        var fullEntities = new InMemoryKvStore();
        fullEntities.Seed("doc-orphan", new()
        {
            ["entity_names"] = new List<string>()
        });
        var fullRelations = new InMemoryKvStore();
        fullRelations.Seed("doc-orphan", new()
        {
            ["relation_pairs"] = new List<object>()
        });
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-orphan",
            Content = "orphan content",
            Vector = [1.0f, 0.5f]
        });
        var rag = CreateLightRag(
            lifecycleService,
            textChunksStore: textChunks,
            fullDocsStore: fullDocs,
            fullEntitiesStore: fullEntities,
            fullRelationsStore: fullRelations,
            vectorStore: vectorStore);

        var result = await rag.DeleteDocumentAsync("doc-orphan");

        result.Found.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        textChunks.Items.Should().NotContainKey("chunk-orphan");
        fullDocs.Items.Should().NotContainKey("doc-orphan");
        fullEntities.Items.Should().NotContainKey("doc-orphan");
        fullRelations.Items.Should().NotContainKey("doc-orphan");
        vectorStore.Get("chunks", "chunk-orphan").Should().BeNull();
    }

    private static LightRAG CreateLightRag(
        DocumentLifecycleService lifecycleService,
        IKVStore? textChunksStore = null,
        IKVStore? fullDocsStore = null,
        IKVStore? fullEntitiesStore = null,
        IKVStore? fullRelationsStore = null,
        IVectorStore? vectorStore = null,
        ITokenizer? tokenizer = null,
        IEmbeddingService? embeddingService = null,
        IKVStore? llmCacheStore = null,
        ILLMService? llmService = null,
        IOptions<LightRAGOptions>? options = null)
    {
        options ??= Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        tokenizer ??= new FakeTokenizer();

        llmService ??= Substitute.For<ILLMService>();
        llmService.GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("<|COMPLETE|>");

        if (embeddingService is null)
        {
            embeddingService = Substitute.For<IEmbeddingService>();
            embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns([1.0f, 0.5f]);
        }

        vectorStore ??= Substitute.For<IVectorStore>();
        var graphStore = Substitute.For<IGraphStore>();
        var rerankService = Substitute.For<IRerankService>();
        textChunksStore ??= Substitute.For<IKVStore>();
        fullDocsStore ??= Substitute.For<IKVStore>();
        fullEntitiesStore ??= Substitute.For<IKVStore>();
        fullRelationsStore ??= Substitute.For<IKVStore>();
        var entityChunksStore = Substitute.For<IKVStore>();
        var relationChunksStore = Substitute.For<IKVStore>();
        llmCacheStore ??= Substitute.For<IKVStore>();
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
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        return new LightRAG(
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

    private static void SeedExtractCache(
        InMemoryKvStore store,
        LightRagCacheKeyBuilder keyBuilder,
        string content,
        string chunkId)
    {
        var prompt = EntityExtractionPromptBuilder.Build(
            content,
            DefaultEntityTypes(),
            maxEntities: 45,
            maxRelationships: 60);

        store.Seed(
            keyBuilder.BuildExtractKey(prompt.CanonicalPrompt),
            new LightRagCacheEntry(
                "<|COMPLETE|>",
                LightRagCacheKeyBuilder.ExtractCacheType,
                prompt.CanonicalPrompt,
                null,
                123,
                chunkId)
            .ToDictionary());
    }

    private static List<string> DefaultEntityTypes()
    {
        return
        [
            "Person", "Creature", "Organization", "Location", "Event",
            "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"
        ];
    }

    private sealed class ThrowingTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            throw new InvalidOperationException("Document chunking should not run for lifecycle duplicates.");
        }

        public string Decode(List<int> tokens)
        {
            throw new InvalidOperationException("Document chunking should not run for lifecycle duplicates.");
        }

        public int CountTokens(string text)
        {
            throw new InvalidOperationException("Document chunking should not run for lifecycle duplicates.");
        }
    }
}
