using System.Data;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Server.Tests;

internal sealed class LightRagServerFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? configureTestServices;
    private readonly IReadOnlyDictionary<string, string?> configurationOverrides;
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));

    public LightRagServerFactory(
        Action<IServiceCollection>? configureTestServices = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        this.configureTestServices = configureTestServices;
        this.configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var testConfiguration = new Dictionary<string, string?>
            {
                ["LLM:ApiKey"] = "test-key",
                ["Embedding:ApiKey"] = "test-key",
                ["Rerank:ApiKey"] = "test-key",
                ["Neo4j:Uri"] = "neo4j://localhost:7687",
                ["Neo4j:User"] = "neo4j",
                ["Neo4j:Password"] = "test-password",
                ["LightRAG:WorkingDir"] = workingDirectory
            };

            foreach (var (key, value) in configurationOverrides)
            {
                testConfiguration[key] = value;
            }

            configuration.AddInMemoryCollection(testConfiguration);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<IHostedService>();
            services.RemoveAll<QdrantClient>();
            services.RemoveAll<IDriver>();
            services.RemoveAll<IVectorStore>();
            services.RemoveAll<IGraphStore>();
            services.AddSingleton<IVectorStore, ThrowingVectorStore>();
            services.AddSingleton<IGraphStore, ThrowingGraphStore>();

            services.RemoveAll<IRagExternalStorageCleaner>();
            services.AddSingleton<IRagExternalStorageCleaner, NoOpRagExternalStorageCleaner>();

            configureTestServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            connection.Dispose();
            TryDeleteWorkingDirectory();
        }
    }

    private void TryDeleteWorkingDirectory()
    {
        try
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NoOpRagExternalStorageCleaner : IRagExternalStorageCleaner
    {
        public Task<IReadOnlyList<string>> ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class ThrowingVectorStore : IVectorStore
    {
        public Task<List<SearchResult>> QueryAsync(
            string collection,
            string query,
            int topK,
            float[]? queryEmbedding = null,
            float threshold = 0.2f,
            CancellationToken cancellationToken = default)
        {
            return Fail<List<SearchResult>>();
        }

        public Task UpsertAsync(
            string collection,
            IEnumerable<VectorDocument> documents,
            CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task DeleteAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task<VectorDocument?> GetByIdAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            return Fail<VectorDocument?>();
        }

        public Task<List<VectorDocument>> GetByIdsAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            return Fail<List<VectorDocument>>();
        }
    }

    private sealed class ThrowingGraphStore : IGraphStore
    {
        public Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Fail<bool>();
        }

        public Task<bool> HasEdgeAsync(string sourceNodeId, string targetNodeId, CancellationToken cancellationToken = default)
        {
            return Fail<bool>();
        }

        public Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Fail<int>();
        }

        public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Fail<GraphNode?>();
        }

        public Task<GraphEdge?> GetEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            CancellationToken cancellationToken = default)
        {
            return Fail<GraphEdge?>();
        }

        public Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
            string sourceNodeId,
            CancellationToken cancellationToken = default)
        {
            return Fail<List<(string SourceId, string TargetId)>>();
        }

        public Task UpsertNodeAsync(
            string nodeId,
            Dictionary<string, object> nodeData,
            CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task UpsertEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            Dictionary<string, object> edgeData,
            CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task RemoveEdgesAsync(List<(string SourceId, string TargetId)> edges, CancellationToken cancellationToken = default)
        {
            return Fail();
        }

        public Task<KnowledgeGraph> GetKnowledgeGraphAsync(
            string nodeLabel,
            int maxDepth = 3,
            int maxNodes = 1000,
            CancellationToken cancellationToken = default)
        {
            return Fail<KnowledgeGraph>();
        }

        public Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
        {
            return Fail<List<string>>();
        }

        public Task<List<string>> GetPopularLabelsAsync(int limit = 300, CancellationToken cancellationToken = default)
        {
            return Fail<List<string>>();
        }

        public Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            return Fail<Dictionary<string, GraphNode>>();
        }

        public Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            return Fail<Dictionary<string, int>>();
        }

        public Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            return Fail<Dictionary<string, List<(string SourceId, string TargetId)>>>();
        }

        public Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default)
        {
            return Fail<Dictionary<(string SourceId, string TargetId), GraphEdge>>();
        }

        public Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default)
        {
            return Fail<Dictionary<(string SourceId, string TargetId), int>>();
        }
    }

    private static Task Fail()
    {
        return Task.FromException(CreateExternalStorageException());
    }

    private static Task<T> Fail<T>()
    {
        return Task.FromException<T>(CreateExternalStorageException());
    }

    private static InvalidOperationException CreateExternalStorageException()
    {
        return new InvalidOperationException(
            "Server tests must not use real external RAG storage. Register an explicit test double for this test.");
    }
}
