using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentDeletion;

public sealed class DocumentDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_WhenEntityAndRelationOnlyUseDeletedChunks_RemovesGraphVectorsAndTracking()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(
            chunkIds: ["chunk-a", "chunk-b"]);
        fixture.FullEntities.Seed("doc-1", new()
        {
            ["entity_names"] = new List<object> { "ALPHA", "BETA" },
            ["count"] = 2
        });
        fixture.FullRelations.Seed("doc-1", new()
        {
            ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } },
            ["count"] = 1
        });
        fixture.EntityChunks.Seed("ALPHA", new() { ["chunk_ids"] = new List<object> { "chunk-a" }, ["count"] = 1 });
        fixture.EntityChunks.Seed("BETA", new() { ["chunk_ids"] = new List<object> { "chunk-b" }, ["count"] = 1 });
        fixture.RelationChunks.Seed("ALPHA<SEP>BETA", new() { ["chunk_ids"] = new List<object> { "chunk-a" }, ["count"] = 1 });
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["source_id"] = "chunk-a", ["description"] = "alpha desc" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["source_id"] = "chunk-b", ["description"] = "beta desc" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["source_id"] = "chunk-a", ["description"] = "rel desc", ["keywords"] = "rel" });

        var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest(
            Workspace: "workspace-a",
            DocId: "doc-1",
            ChunkIds: ["chunk-a", "chunk-b"],
            DeleteLlmCache: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.DeletedNodes.Should().BeEquivalentTo("ALPHA", "BETA");
        fixture.Graph.DeletedEdges.Should().Contain(("ALPHA", "BETA"));
        var expectedChunkIds = new[] { "chunk-a", "chunk-b" };
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "chunks" && call.Ids.SequenceEqual(expectedChunkIds));
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "entities" && call.Ids.Contains("ALPHA") && call.Ids.Contains("BETA"));
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "relationships" && call.Ids.Contains("ALPHA<SEP>BETA"));
        fixture.EntityChunks.Items.Should().NotContainKey("ALPHA");
        fixture.RelationChunks.Items.Should().NotContainKey("ALPHA<SEP>BETA");
    }

    [Fact]
    public async Task DeleteAsync_WhenEntityAndRelationHaveSharedChunks_PrunesSourceIdsAndKeepsGraph()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
        fixture.FullEntities.Seed("doc-1", new() { ["entity_names"] = new List<object> { "ALPHA" }, ["count"] = 1 });
        fixture.FullRelations.Seed("doc-1", new() { ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } }, ["count"] = 1 });
        fixture.EntityChunks.Seed("ALPHA", new() { ["chunk_ids"] = new List<object> { "chunk-a", "chunk-z" }, ["count"] = 2 });
        fixture.RelationChunks.Seed("ALPHA<SEP>BETA", new() { ["chunk_ids"] = new List<object> { "chunk-a", "chunk-z" }, ["count"] = 2 });
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["source_id"] = "chunk-a<SEP>chunk-z", ["description"] = "alpha desc" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["source_id"] = "chunk-z", ["description"] = "beta desc" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["source_id"] = "chunk-a<SEP>chunk-z", ["description"] = "rel desc", ["keywords"] = "rel" });

        var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.DeletedNodes.Should().BeEmpty();
        fixture.Graph.GetSeededNode("ALPHA")!.Properties["source_id"].Should().Be("chunk-z");
        fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["source_id"].Should().Be("chunk-z");
        fixture.EntityChunks.Items["ALPHA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-z" });
        fixture.RelationChunks.Items["ALPHA<SEP>BETA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-z" });
        fixture.VectorStore.UpsertCalls.Should().Contain(call => call.Collection == "entities");
        fixture.VectorStore.UpsertCalls.Should().Contain(call => call.Collection == "relationships");
    }

    [Fact]
    public async Task DeleteAsync_WhenDeleteLlmCacheFalse_DoesNotDeleteCacheIds()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
        fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object> { "cache-a" } });

        await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

        fixture.LlmCache.DeleteCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_WhenDeleteLlmCacheTrue_DeletesChunkCacheIds()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
        fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object> { "cache-a", "cache-b" } });
        fixture.LlmCache.Seed("cache-a", new() { ["return_value"] = "a" });
        fixture.LlmCache.Seed("cache-b", new() { ["return_value"] = "b" });

        await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: true));

        fixture.LlmCache.DeleteCalls.SelectMany(call => call).Should().BeEquivalentTo("cache-a", "cache-b");
    }

    [Fact]
    public async Task DeleteAsync_WhenVectorDeleteFails_RecordsFailureStage()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
        fixture.VectorStore.ThrowOnDeleteCollection = "chunks";

        var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

        result.Succeeded.Should().BeFalse();
        result.Stage.Should().Be(DocumentDeletionStage.DeleteChunkVectors);
        var status = await fixture.StatusStore.GetAsync("workspace-a", "doc-1");
        status!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        status.Metadata["deletion_failure_stage"].Should().Be(DocumentDeletionStage.DeleteChunkVectors);
    }

    [Fact]
    public async Task DeleteAsync_WhenImpactAnalysisFails_DoesNotRunDestructiveDeletes()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
        fixture.FullRelations.Seed("doc-1", new()
        {
            ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } },
            ["count"] = 1
        });
        fixture.RelationChunks.ThrowOnGetKey = "ALPHA<SEP>BETA";

        var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

        result.Succeeded.Should().BeFalse();
        result.Stage.Should().Be(DocumentDeletionStage.AnalyzeGraphReferences);
        fixture.VectorStore.DeleteCalls.Should().NotContain(call => call.Collection == "chunks");
        fixture.TextChunks.DeleteCalls.Should().BeEmpty();
        fixture.FullDocs.DeleteCalls.Should().BeEmpty();
    }

    private sealed class DocumentDeletionFixture
    {
        private DocumentDeletionFixture(
            DocumentDeletionService service,
            InMemoryDocumentStatusStore statusStore,
            InMemoryVectorStore vectorStore,
            InMemoryGraphStore graph,
            InMemoryKvStore textChunks,
            InMemoryKvStore fullDocs,
            InMemoryKvStore fullEntities,
            InMemoryKvStore fullRelations,
            InMemoryKvStore entityChunks,
            InMemoryKvStore relationChunks,
            InMemoryKvStore llmCache)
        {
            Service = service;
            StatusStore = statusStore;
            VectorStore = vectorStore;
            Graph = graph;
            TextChunks = textChunks;
            FullDocs = fullDocs;
            FullEntities = fullEntities;
            FullRelations = fullRelations;
            EntityChunks = entityChunks;
            RelationChunks = relationChunks;
            LlmCache = llmCache;
        }

        public DocumentDeletionService Service { get; }
        public InMemoryDocumentStatusStore StatusStore { get; }
        public InMemoryVectorStore VectorStore { get; }
        public InMemoryGraphStore Graph { get; }
        public InMemoryKvStore TextChunks { get; }
        public InMemoryKvStore FullDocs { get; }
        public InMemoryKvStore FullEntities { get; }
        public InMemoryKvStore FullRelations { get; }
        public InMemoryKvStore EntityChunks { get; }
        public InMemoryKvStore RelationChunks { get; }
        public InMemoryKvStore LlmCache { get; }

        public static async Task<DocumentDeletionFixture> CreateProcessedDocumentAsync(IReadOnlyList<string> chunkIds)
        {
            var statusStore = new InMemoryDocumentStatusStore();
            var lifecycleService = new DocumentLifecycleService(
                statusStore,
                Options.Create(new LightRAGOptions { Workspace = "workspace-a" }),
                NullLogger<DocumentLifecycleService>.Instance);
            await lifecycleService.PrepareIngestionAsync(
                "alpha beta",
                docId: "doc-1",
                filePath: "doc-1.md");
            await lifecycleService.RecordChunksAsync(
                "workspace-a",
                "doc-1",
                chunkIds.Select((chunkId, index) => new Chunk
                {
                    Id = chunkId,
                    Content = $"content {chunkId}",
                    Tokens = 1,
                    ChunkOrderIndex = index,
                    FilePath = "doc-1.md"
                }).ToList());
            await lifecycleService.MarkProcessedAsync("workspace-a", "doc-1");

            var vectorStore = new InMemoryVectorStore();
            var graph = new InMemoryGraphStore();
            var textChunks = new InMemoryKvStore();
            var fullDocs = new InMemoryKvStore();
            var fullEntities = new InMemoryKvStore();
            var fullRelations = new InMemoryKvStore();
            var entityChunks = new InMemoryKvStore();
            var relationChunks = new InMemoryKvStore();
            var llmCache = new InMemoryKvStore();

            fullDocs.Seed("doc-1", new() { ["content"] = "alpha beta" });
            foreach (var chunkId in chunkIds)
            {
                textChunks.Seed(chunkId, new() { ["content"] = $"content {chunkId}" });
                vectorStore.Seed("chunks", new VectorDocument
                {
                    Id = chunkId,
                    Content = $"content {chunkId}",
                    Vector = [0.1f, 0.2f]
                });
            }

            var service = new DocumentDeletionService(
                vectorStore,
                graph,
                new FakeEmbeddingService(),
                textChunks,
                fullDocs,
                fullEntities,
                fullRelations,
                entityChunks,
                relationChunks,
                llmCache,
                lifecycleService,
                NullLogger<DocumentDeletionService>.Instance);

            return new DocumentDeletionFixture(
                service,
                statusStore,
                vectorStore,
                graph,
                textChunks,
                fullDocs,
                fullEntities,
                fullRelations,
                entityChunks,
                relationChunks,
                llmCache);
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public int EmbeddingDimension => 2;

        public int MaxTokenSize => 8192;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { 0.1f, 0.2f });
        }

        public Task<float[][]> GenerateEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f }).ToArray());
        }
    }
}
