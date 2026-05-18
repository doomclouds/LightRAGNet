using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Storage;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Tests.Storage;

public sealed class DocumentDeletionStorageIntegrationTests
{
    private static bool RunStorageIntegration =>
        Environment.GetEnvironmentVariable("LIGHTRAGNET_RUN_STORAGE_INTEGRATION") == "1";

    [Fact]
    public async Task QdrantVectorStore_DeleteAsync_RemovesUpsertedVector()
    {
        if (!RunStorageIntegration)
        {
            return;
        }

        Exception? testFailure = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var collection = $"deletion_integration_{Guid.NewGuid():N}";
        var options = new QdrantOptions
        {
            Host = Environment.GetEnvironmentVariable("LIGHTRAGNET_QDRANT_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("LIGHTRAGNET_QDRANT_PORT"), out var port) ? port : 6334,
            EmbeddingDimension = 4,
            Workspace = $"integration_{Guid.NewGuid():N}"
        };
        var client = new QdrantClient(options.Host, options.Port);
        var store = new QdrantVectorStore(
            client,
            NullLogger<QdrantVectorStore>.Instance,
            Options.Create(options));

        try
        {
            await store.UpsertAsync(
                collection,
                [
                    new VectorDocument
                    {
                        Id = "chunk-a",
                        Vector = [0.1f, 0.2f, 0.3f, 0.4f],
                        Content = "content",
                        Metadata = new Dictionary<string, object>
                        {
                            ["id"] = "chunk-a",
                            ["content"] = "content"
                        }
                    }
                ],
                cancellation.Token);

            (await store.GetByIdAsync(collection, "chunk-a", cancellation.Token)).Should().NotBeNull();

            await store.DeleteAsync(collection, ["chunk-a"], cancellation.Token);

            (await store.GetByIdAsync(collection, "chunk-a", cancellation.Token)).Should().BeNull();
        }
        catch (Exception ex)
        {
            testFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await DeleteQdrantCollectionIfExistsAsync(
                    client,
                    $"lightrag_vdb_dotnet_{collection}_4d",
                    cleanup.Token);
            }
            catch when (testFailure is not null)
            {
            }
        }
    }

    [Fact]
    public async Task Neo4jGraphStore_DeleteAsync_PrunesSourceIdsThroughDeletionService()
    {
        if (!RunStorageIntegration)
        {
            return;
        }

        Exception? testFailure = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var options = new Neo4JOptions
        {
            Uri = Environment.GetEnvironmentVariable("LIGHTRAGNET_NEO4J_URI") ?? "neo4j://localhost:7687",
            User = Environment.GetEnvironmentVariable("LIGHTRAGNET_NEO4J_USER") ?? "neo4j",
            Password = Environment.GetEnvironmentVariable("LIGHTRAGNET_NEO4J_PASSWORD") ??
                       Environment.GetEnvironmentVariable("Neo4j__Password") ??
                       string.Empty,
            Workspace = $"integration_{Guid.NewGuid():N}"
        };
        await using var driver = GraphDatabase.Driver(
            options.Uri,
            AuthTokens.Basic(options.User, options.Password));
        var store = new Neo4JGraphStore(
            driver,
            NullLogger<Neo4JGraphStore>.Instance,
            Options.Create(options));
        var textChunks = new InMemoryKvStore();
        var fullDocs = new InMemoryKvStore();
        var fullEntities = new InMemoryKvStore();
        var fullRelations = new InMemoryKvStore();
        var entityChunks = new InMemoryKvStore();
        var relationChunks = new InMemoryKvStore();
        var llmCache = new InMemoryKvStore();
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            Options.Create(new LightRAGOptions { Workspace = options.Workspace }),
            NullLogger<DocumentLifecycleService>.Instance);
        await lifecycleService.PrepareIngestionAsync(
            "chunk a chunk b",
            docId: "doc-1",
            filePath: "doc-1.md",
            cancellationToken: cancellation.Token);
        await lifecycleService.RecordChunksAsync(
            options.Workspace!,
            "doc-1",
            [
                new Chunk
                {
                    Id = "chunk-a",
                    Content = "chunk a",
                    Tokens = 1,
                    ChunkOrderIndex = 0,
                    FilePath = "doc-1.md"
                },
                new Chunk
                {
                    Id = "chunk-b",
                    Content = "chunk b",
                    Tokens = 1,
                    ChunkOrderIndex = 1,
                    FilePath = "doc-1.md"
                }
            ],
            cancellation.Token);
        await lifecycleService.MarkProcessedAsync(options.Workspace!, "doc-1", cancellation.Token);
        var deletionService = new DocumentDeletionService(
            new InMemoryVectorStore(),
            store,
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

        try
        {
            textChunks.Seed("chunk-a", new() { ["content"] = "chunk a" });
            textChunks.Seed("chunk-b", new() { ["content"] = "chunk b" });
            fullDocs.Seed("doc-1", new() { ["content"] = "chunk a chunk b" });
            fullEntities.Seed("doc-1", new()
            {
                ["entity_names"] = new List<object> { "ENTITY_A" },
                ["count"] = 1
            });
            fullRelations.Seed("doc-1", new()
            {
                ["relation_pairs"] = new List<object> { new List<object> { "ENTITY_A", "ENTITY_B" } },
                ["count"] = 1
            });
            entityChunks.Seed("ENTITY_A", new()
            {
                ["chunk_ids"] = new List<object> { "chunk-a", "chunk-b" },
                ["count"] = 2
            });
            relationChunks.Seed("ENTITY_A<SEP>ENTITY_B", new()
            {
                ["chunk_ids"] = new List<object> { "chunk-a", "chunk-b" },
                ["count"] = 2
            });
            await store.UpsertNodeAsync(
                "ENTITY_A",
                new Dictionary<string, object>
                {
                    ["entity_id"] = "ENTITY_A",
                    ["entity_type"] = "Entity",
                    ["source_id"] = "chunk-a<SEP>chunk-b",
                    ["description"] = "entity a"
                },
                cancellation.Token);
            await store.UpsertNodeAsync(
                "ENTITY_B",
                new Dictionary<string, object>
                {
                    ["entity_id"] = "ENTITY_B",
                    ["entity_type"] = "Entity",
                    ["source_id"] = "chunk-b",
                    ["description"] = "entity b"
                },
                cancellation.Token);
            await store.UpsertEdgeAsync(
                "ENTITY_A",
                "ENTITY_B",
                new Dictionary<string, object>
                {
                    ["source_id"] = "chunk-a<SEP>chunk-b",
                    ["description"] = "edge",
                    ["keywords"] = "edge"
                },
                cancellation.Token);

            var result = await deletionService.DeleteAsync(
                new DocumentDeletionRequest(options.Workspace!, "doc-1", ["chunk-a"], DeleteLlmCache: false),
                cancellation.Token);
            var node = await store.GetNodeAsync("ENTITY_A", cancellation.Token);
            var edge = await store.GetEdgeAsync("ENTITY_A", "ENTITY_B", cancellation.Token);

            result.Succeeded.Should().BeTrue();
            node.Should().NotBeNull();
            node!.Properties["source_id"].Should().Be("chunk-b");
            edge.Should().NotBeNull();
            edge!.Properties["source_id"].Should().Be("chunk-b");
            entityChunks.Items["ENTITY_A"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-b" });
            relationChunks.Items["ENTITY_A<SEP>ENTITY_B"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-b" });
        }
        catch (Exception ex)
        {
            testFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await DeleteNeo4jWorkspaceIfExistsAsync(driver, options.Workspace!, cleanup.Token);
            }
            catch when (testFailure is not null)
            {
            }
        }
    }

    private static async Task DeleteQdrantCollectionIfExistsAsync(
        QdrantClient client,
        string collectionName,
        CancellationToken cancellationToken)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken);
        if (collections.Contains(collectionName, StringComparer.Ordinal))
        {
            await client.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }
    }

    private static async Task DeleteNeo4jWorkspaceIfExistsAsync(
        IDriver driver,
        string workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync($"MATCH (n:`{workspace}`) DETACH DELETE n");
        await cursor.ConsumeAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public int EmbeddingDimension => 4;

        public int MaxTokenSize => 8192;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<float[][]> GenerateEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToArray());
        }
    }
}
