using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class GraphControllerTests
{
    [Fact]
    public async Task EntityExists_WhenEntityMissing_ReturnsFalse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GraphEntityExistsResponse>(
            "/api/graph/entity/exists?name=ALPHA");

        result!.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task CreateEntity_WhenDescriptionMissing_ReturnsBadRequestWithValidationStatus()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/graph/entity",
            new GraphEntityCreateDto(
                EntityName: "ALPHA",
                EntityData: new Dictionary<string, object>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<GraphCurationResponse>();
        body!.Succeeded.Should().BeFalse();
        body.Status.Should().Be("validation_error");
        body.FailureStage.Should().Be("validation");
    }

    [Fact]
    public async Task CreateRelation_WhenEndpointsExist_ReturnsSuccessAndCreatesRelation()
    {
        var graphStore = new InMemoryGraphStore();
        graphStore.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["entity_name"] = "ALPHA",
            ["description"] = "Alpha entity"
        });
        graphStore.SeedNode("BETA", new()
        {
            ["entity_id"] = "BETA",
            ["entity_name"] = "BETA",
            ["description"] = "Beta entity"
        });

        await using var factory = CreateFactory(graphStore);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/graph/relation",
            new GraphRelationCreateDto(
                SourceEntity: "ALPHA",
                TargetEntity: "BETA",
                RelationData: new Dictionary<string, object>
                {
                    ["description"] = "Alpha is related to beta",
                    ["keywords"] = "alpha,beta",
                    ["weight"] = 1
                }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GraphCurationResponse>();
        body!.Succeeded.Should().BeTrue();
        body.Status.Should().Be("success");
        graphStore.GetSeededEdge("ALPHA", "BETA").Should().NotBeNull();
    }

    [Fact]
    public async Task EditEntity_WhenEntityMissing_ReturnsNotFound()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            "/api/graph/entity/ALPHA",
            new GraphEntityEditDto(new Dictionary<string, object>
            {
                ["description"] = "Updated alpha"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("not_found");
    }

    [Fact]
    public async Task CreateEntity_WhenDuplicate_ReturnsConflict()
    {
        var graphStore = new InMemoryGraphStore();
        graphStore.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["entity_name"] = "ALPHA",
            ["description"] = "Existing alpha"
        });

        await using var factory = CreateFactory(graphStore);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/graph/entity",
            new GraphEntityCreateDto(
                EntityName: "ALPHA",
                EntityData: new Dictionary<string, object>
                {
                    ["description"] = "Duplicate alpha"
                }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("conflict");
    }

    [Fact]
    public async Task DeleteRelation_WhenQueryMissing_ReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/graph/relation?source=&target=BETA");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("validation_error");
    }

    [Fact]
    public async Task Labels_ReturnsPopularLabels()
    {
        var graphStore = new InMemoryGraphStore
        {
            PopularLabels = ["BETA", "ALPHA"]
        };

        await using var factory = CreateFactory(graphStore);
        var client = factory.CreateClient();

        var labels = await client.GetFromJsonAsync<List<string>>("/api/graph/labels");

        labels.Should().Equal("BETA", "ALPHA");
        graphStore.PopularLabelsCalls.Should().ContainSingle().Which.Should().Be(300);
        graphStore.AllLabelsCallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteEntity_WhenMissing_ReturnsNotFound()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/graph/entity/ALPHA");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("not_found");
    }

    [Fact]
    public async Task CreateEntity_WhenBodyIsNull_ReturnsValidationResponse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/graph/entity", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("validation_error");
    }

    [Fact]
    public async Task CreateEntity_WhenEntityDataMissing_ReturnsValidationResponse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/graph/entity", new
        {
            EntityName = "ALPHA"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadCurationResponseAsync(response);
        body.Succeeded.Should().BeFalse();
        body.Status.Should().Be("validation_error");
    }

    private static async Task<GraphCurationResponse> ReadCurationResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<GraphCurationResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static LightRagServerFactory CreateFactory(InMemoryGraphStore? graphStore = null)
    {
        graphStore ??= new InMemoryGraphStore();
        var vectorStore = new InMemoryVectorStore();
        var embeddingService = new FakeEmbeddingService();

        return new LightRagServerFactory(services =>
        {
            services.RemoveAll<IGraphStore>();
            services.RemoveAll<IVectorStore>();
            services.RemoveAll<IEmbeddingService>();
            services.AddSingleton<IGraphStore>(graphStore);
            services.AddSingleton<IVectorStore>(vectorStore);
            services.AddSingleton<IEmbeddingService>(embeddingService);
        });
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
            return Task.FromResult(new[] { 0.25f, 0.75f });
        }

        public Task<float[][]> GenerateEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => new[] { 0.25f, 0.75f }).ToArray());
        }
    }

    private sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly Dictionary<string, Dictionary<string, VectorDocument>> collections = [];

        public Task<List<SearchResult>> QueryAsync(
            string collection,
            string query,
            int topK,
            float[]? queryEmbedding = null,
            float threshold = 0.2f,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = GetCollection(collection)
                .Values
                .Take(topK)
                .Select(document => new SearchResult
                {
                    Id = document.Id,
                    Content = document.Content,
                    Metadata = GraphControllerTests.Clone(document.Metadata),
                    Score = 1.0f
                })
                .ToList();

            return Task.FromResult(results);
        }

        public Task UpsertAsync(
            string collection,
            IEnumerable<VectorDocument> documents,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectionItems = GetCollection(collection);
            foreach (var document in documents)
            {
                collectionItems[document.Id] = Clone(document);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectionItems = GetCollection(collection);
            foreach (var id in ids)
            {
                collectionItems.Remove(id);
            }

            return Task.CompletedTask;
        }

        public Task<VectorDocument?> GetByIdAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetCollection(collection).TryGetValue(id, out var document)
                ? Clone(document)
                : null);
        }

        public Task<List<VectorDocument>> GetByIdsAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = ids
                .Select(id => GetCollection(collection).TryGetValue(id, out var document) ? Clone(document) : null)
                .OfType<VectorDocument>()
                .ToList();

            return Task.FromResult(documents);
        }

        private Dictionary<string, VectorDocument> GetCollection(string collection)
        {
            if (!collections.TryGetValue(collection, out var items))
            {
                items = new Dictionary<string, VectorDocument>(StringComparer.Ordinal);
                collections[collection] = items;
            }

            return items;
        }

        private static VectorDocument Clone(VectorDocument document)
        {
            return new VectorDocument
            {
                Id = document.Id,
                Content = document.Content,
                Metadata = GraphControllerTests.Clone(document.Metadata),
                Vector = [.. document.Vector]
            };
        }
    }

    private sealed class InMemoryGraphStore : IGraphStore
    {
        private readonly Dictionary<string, GraphNode> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphEdge> edges = new(StringComparer.Ordinal);

        public List<string> PopularLabels { get; set; } = [];
        public List<int> PopularLabelsCalls { get; } = [];
        public int AllLabelsCallCount { get; private set; }

        public void SeedNode(string nodeId, Dictionary<string, object> properties)
        {
            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Properties = GraphControllerTests.Clone(properties)
            };
        }

        public GraphEdge? GetSeededEdge(string sourceId, string targetId)
        {
            return edges.TryGetValue(GetEdgeKey(sourceId, targetId), out var edge) ? Clone(edge) : null;
        }

        public Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(nodes.ContainsKey(nodeId));
        }

        public Task<bool> HasEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(edges.ContainsKey(GetEdgeKey(sourceNodeId, targetNodeId)));
        }

        public Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId));
        }

        public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(nodes.TryGetValue(nodeId, out var node) ? Clone(node) : null);
        }

        public Task<GraphEdge?> GetEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetSeededEdge(sourceNodeId, targetNodeId));
        }

        public Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
            string sourceNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = edges.Values
                .Where(edge => edge.SourceId == sourceNodeId || edge.TargetId == sourceNodeId)
                .Select(edge => (edge.SourceId, edge.TargetId))
                .ToList();

            return Task.FromResult(result);
        }

        public Task UpsertNodeAsync(
            string nodeId,
            Dictionary<string, object> nodeData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Properties = GraphControllerTests.Clone(nodeData)
            };

            return Task.CompletedTask;
        }

        public Task UpsertEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            Dictionary<string, object> edgeData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            edges[GetEdgeKey(sourceNodeId, targetNodeId)] = new GraphEdge
            {
                SourceId = sourceNodeId,
                TargetId = targetNodeId,
                Properties = GraphControllerTests.Clone(edgeData)
            };

            return Task.CompletedTask;
        }

        public Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodes.Remove(nodeId);
            var connectedEdges = edges.Values
                .Where(edge => edge.SourceId == nodeId || edge.TargetId == nodeId)
                .Select(edge => GetEdgeKey(edge.SourceId, edge.TargetId))
                .ToList();
            foreach (var key in connectedEdges)
            {
                edges.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveEdgesAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (sourceId, targetId) in edgePairs)
            {
                edges.Remove(GetEdgeKey(sourceId, targetId));
            }

            return Task.CompletedTask;
        }

        public Task<KnowledgeGraph> GetKnowledgeGraphAsync(
            string nodeLabel,
            int maxDepth = 3,
            int maxNodes = 1000,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graph = new KnowledgeGraph
            {
                Nodes = nodes.Values
                    .Take(maxNodes)
                    .Select(node => new KnowledgeGraphNode
                    {
                        Id = node.Id,
                        Properties = GraphControllerTests.Clone(node.Properties)
                    })
                    .ToList(),
                Edges = edges.Values
                    .Select(edge => new KnowledgeGraphEdge
                    {
                        Source = edge.SourceId,
                        Target = edge.TargetId,
                        Properties = GraphControllerTests.Clone(edge.Properties)
                    })
                    .ToList(),
                IsTruncated = nodes.Count > maxNodes
            };

            return Task.FromResult(graph);
        }

        public Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AllLabelsCallCount++;
            return Task.FromResult(nodes.Keys.Order(StringComparer.Ordinal).ToList());
        }

        public Task<List<string>> GetPopularLabelsAsync(
            int limit = 300,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PopularLabelsCalls.Add(limit);
            var labels = PopularLabels.Count > 0
                ? PopularLabels
                : nodes.Keys.Order(StringComparer.Ordinal).Take(limit).ToList();

            return Task.FromResult(labels.Take(limit).ToList());
        }

        public Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = nodeIds
                .Where(nodes.ContainsKey)
                .ToDictionary(nodeId => nodeId, nodeId => Clone(nodes[nodeId]), StringComparer.Ordinal);

            return Task.FromResult(result);
        }

        public Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = nodeIds.ToDictionary(
                nodeId => nodeId,
                nodeId => edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId),
                StringComparer.Ordinal);

            return Task.FromResult(result);
        }

        public Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = nodeIds.ToDictionary(
                nodeId => nodeId,
                nodeId => edges.Values
                    .Where(edge => edge.SourceId == nodeId || edge.TargetId == nodeId)
                    .Select(edge => (edge.SourceId, edge.TargetId))
                    .ToList(),
                StringComparer.Ordinal);

            return Task.FromResult(result);
        }

        public Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new Dictionary<(string SourceId, string TargetId), GraphEdge>();
            foreach (var pair in edgePairs)
            {
                if (edges.TryGetValue(GetEdgeKey(pair.SourceId, pair.TargetId), out var edge))
                {
                    result[pair] = Clone(edge);
                }
            }

            return Task.FromResult(result);
        }

        public Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = edgePairs.ToDictionary(
                pair => pair,
                pair => edges.Values.Count(edge => edge.SourceId == pair.SourceId || edge.TargetId == pair.SourceId)
                    + edges.Values.Count(edge => edge.SourceId == pair.TargetId || edge.TargetId == pair.TargetId));

            return Task.FromResult(result);
        }

        private static string GetEdgeKey(string sourceId, string targetId)
        {
            return $"{sourceId}{(char)31}{targetId}";
        }

        private static GraphNode Clone(GraphNode node)
        {
            return new GraphNode
            {
                Id = node.Id,
                Properties = GraphControllerTests.Clone(node.Properties)
            };
        }

        private static GraphEdge Clone(GraphEdge edge)
        {
            return new GraphEdge
            {
                SourceId = edge.SourceId,
                TargetId = edge.TargetId,
                Properties = GraphControllerTests.Clone(edge.Properties)
            };
        }
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => CloneValue(pair.Value),
            StringComparer.Ordinal);
    }

    private static object CloneValue(object value)
    {
        return value switch
        {
            Dictionary<string, object> dictionary => Clone(dictionary),
            List<object> list => list.Select(CloneValue).ToList(),
            List<string> list => list.ToList(),
            float[] vector => vector.ToArray(),
            _ => value
        };
    }
}
