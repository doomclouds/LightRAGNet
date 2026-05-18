using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
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

    private static LightRAG CreateLightRag(
        DocumentLifecycleService lifecycleService,
        IKVStore? textChunksStore = null,
        IKVStore? fullDocsStore = null,
        IVectorStore? vectorStore = null,
        ITokenizer? tokenizer = null,
        IEmbeddingService? embeddingService = null)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        tokenizer ??= new FakeTokenizer();

        var llmService = Substitute.For<ILLMService>();
        llmService.ExtractEntitiesAsync(
                Arg.Any<string>(),
                Arg.Any<List<string>>(),
                Arg.Any<float>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new EntityExtractionResult());

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
        var fullEntitiesStore = Substitute.For<IKVStore>();
        var fullRelationsStore = Substitute.For<IKVStore>();
        var entityChunksStore = Substitute.For<IKVStore>();
        var relationChunksStore = Substitute.For<IKVStore>();
        var llmCacheStore = Substitute.For<IKVStore>();

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

        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
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
