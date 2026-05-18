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
    public async Task InsertAsync_DuplicateLifecycleStatus_DoesNotProcessDocument()
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = CreateLifecycleService(statusStore);
        await lifecycleService.PrepareIngestionAsync("original content", docId: "doc-duplicate", filePath: "original.md");
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

    private static LightRAG CreateLightRag(
        DocumentLifecycleService lifecycleService,
        IKVStore? fullDocsStore = null,
        ITokenizer? tokenizer = null)
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

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);

        var vectorStore = Substitute.For<IVectorStore>();
        var graphStore = Substitute.For<IGraphStore>();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = Substitute.For<IKVStore>();
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
