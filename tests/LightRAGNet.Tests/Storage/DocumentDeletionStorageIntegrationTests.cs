using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Storage;
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
        finally
        {
            await DeleteQdrantCollectionIfExistsAsync(client, $"lightrag_vdb_dotnet_{collection}_4d", cancellation.Token);
        }
    }

    [Fact]
    public async Task Neo4jGraphStore_UpsertAsync_PrunesSourceIds()
    {
        if (!RunStorageIntegration)
        {
            return;
        }

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

        try
        {
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
                "ENTITY_A",
                new Dictionary<string, object>
                {
                    ["entity_id"] = "ENTITY_A",
                    ["entity_type"] = "Entity",
                    ["source_id"] = "chunk-b",
                    ["description"] = "entity a"
                },
                cancellation.Token);

            var node = await store.GetNodeAsync("ENTITY_A", cancellation.Token);
            node.Should().NotBeNull();
            node!.Properties["source_id"].Should().Be("chunk-b");

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
            await store.UpsertEdgeAsync(
                "ENTITY_A",
                "ENTITY_B",
                new Dictionary<string, object>
                {
                    ["source_id"] = "chunk-b",
                    ["description"] = "edge",
                    ["keywords"] = "edge"
                },
                cancellation.Token);

            var edge = await store.GetEdgeAsync("ENTITY_A", "ENTITY_B", cancellation.Token);
            edge.Should().NotBeNull();
            edge!.Properties["source_id"].Should().Be("chunk-b");
        }
        finally
        {
            await DeleteNeo4jWorkspaceIfExistsAsync(driver, options.Workspace, cancellation.Token);
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
        await using var session = driver.AsyncSession();
        await session.RunAsync($"MATCH (n:`{workspace}`) DETACH DELETE n");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
