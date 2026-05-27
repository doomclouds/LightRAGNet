using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

internal static class ApiRetrievalEvaluationTestDoubles
{
    public static LightRagServerFactory CreateServerFactory(ApiRetrievalEvaluationDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        var stores = Seed(dataSet);

        return new LightRagServerFactory(services =>
        {
            RemoveAll<IKVStore>(services);
            services.RemoveAll<IVectorStore>();
            services.RemoveAll<IGraphStore>();
            services.RemoveAll<ILLMService>();
            services.RemoveAll<IEmbeddingService>();
            services.RemoveAll<IRerankService>();
            services.RemoveAll<ITokenizer>();

            services.AddSingleton<IVectorStore>(stores.VectorStore);
            services.AddSingleton<IGraphStore>(stores.GraphStore);
            services.AddSingleton<ILLMService, ApiFakeLlmService>();
            services.AddSingleton<IEmbeddingService, ApiFakeEmbeddingService>();
            services.AddSingleton<IRerankService, ApiFakeRerankService>();
            services.AddSingleton<ITokenizer, ApiFakeTokenizer>();

            foreach (var kvStoreName in KVContracts.GetKVStoreNames())
            {
                var store = stores.KvStores.TryGetValue(kvStoreName, out var existing)
                    ? existing
                    : new ApiInMemoryKvStore();
                services.AddKeyedSingleton<IKVStore>(kvStoreName, (_, _) => store);
            }

            services.PostConfigure<LightRAGOptions>(options =>
            {
                options.KgChunkPickMethod = "WEIGHT";
            });
        });
    }

    private static ApiRetrievalEvaluationStores Seed(ApiRetrievalEvaluationDataSet dataSet)
    {
        var vectorStore = new ApiInMemoryVectorStore();
        var graphStore = new ApiInMemoryGraphStore();
        var textChunks = new ApiInMemoryKvStore();

        foreach (var chunk in dataSet.Chunks)
        {
            vectorStore.Seed("chunks", new VectorDocument
            {
                Id = chunk.Id,
                Content = chunk.Content,
                Metadata = new Dictionary<string, object>
                {
                    ["chunk_id"] = chunk.Id,
                    ["file_path"] = chunk.FilePath
                }
            });
            textChunks.Seed(chunk.Id, new Dictionary<string, object>
            {
                ["content"] = chunk.Content,
                ["file_path"] = chunk.FilePath
            });
        }

        foreach (var entity in dataSet.Entities)
        {
            graphStore.SeedNode(entity.Id, new Dictionary<string, object>
            {
                ["entity_id"] = entity.Id,
                ["entity_type"] = entity.Type,
                ["description"] = entity.Description,
                ["source_id"] = entity.SourceId,
                ["file_path"] = entity.FilePath
            });
            vectorStore.Seed("entities", new VectorDocument
            {
                Id = $"entity-{entity.Id}",
                Content = entity.Description,
                Metadata = new Dictionary<string, object>
                {
                    ["entity_name"] = entity.Id,
                    ["entity_type"] = entity.Type,
                    ["description"] = entity.Description,
                    ["source_id"] = entity.SourceId,
                    ["file_path"] = entity.FilePath
                }
            });
        }

        foreach (var relationship in dataSet.Relationships)
        {
            graphStore.SeedEdge(relationship.SourceId, relationship.TargetId, new Dictionary<string, object>
            {
                ["keywords"] = relationship.Keywords,
                ["description"] = relationship.Description,
                ["weight"] = relationship.Weight,
                ["source_id"] = relationship.SourceIdList
            });
            vectorStore.Seed("relationships", new VectorDocument
            {
                Id = $"relationship-{relationship.SourceId}-{relationship.TargetId}",
                Content = relationship.Description,
                Metadata = new Dictionary<string, object>
                {
                    ["src_id"] = relationship.SourceId,
                    ["tgt_id"] = relationship.TargetId,
                    ["keywords"] = relationship.Keywords,
                    ["description"] = relationship.Description,
                    ["weight"] = relationship.Weight,
                    ["source_id"] = relationship.SourceIdList
                }
            });
        }

        return new ApiRetrievalEvaluationStores(
            vectorStore,
            graphStore,
            new Dictionary<string, ApiInMemoryKvStore>(StringComparer.Ordinal)
            {
                [KVContracts.TextChunks] = textChunks
            });
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == typeof(T))
            {
                services.RemoveAt(index);
            }
        }
    }

    private sealed record ApiRetrievalEvaluationStores(
        ApiInMemoryVectorStore VectorStore,
        ApiInMemoryGraphStore GraphStore,
        Dictionary<string, ApiInMemoryKvStore> KvStores);

    private sealed class ApiFakeLlmService : ILLMService
    {
        public Task<string> GenerateAsync(
            string prompt,
            string? systemPrompt = null,
            List<ChatMessage>? historyMessages = null,
            float temperature = 1,
            bool enableCot = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("api evaluation fake response");
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            string? systemPrompt = null,
            List<ChatMessage>? historyMessages = null,
            float temperature = 1,
            bool enableCot = false,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return "api evaluation fake response";
            await Task.CompletedTask;
        }

        public Task<EntityExtractionResult> ExtractEntitiesAsync(
            string text,
            List<string> entityTypes,
            float temperature = 0.3f,
            int? maxEntities = null,
            int? maxRelationships = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EntityExtractionResult());
        }

        public Task<KeywordsResult> ExtractKeywordsAsync(
            string query,
            float temperature = 0.3f,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new KeywordsResult());
        }

        public Task<string> SummarizeAsync(
            string descriptionType,
            string descriptionName,
            List<string> descriptionList,
            int summaryLengthRecommended,
            float temperature = 0.3f,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(string.Join(" ", descriptionList));
        }
    }

    private sealed class ApiFakeEmbeddingService : IEmbeddingService
    {
        public int EmbeddingDimension => 3;

        public int MaxTokenSize => 8192;

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        }

        public Task<float[][]> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());
        }
    }

    private sealed class ApiFakeRerankService : IRerankService
    {
        public Task<List<RerankResult>> RerankAsync(
            string query,
            List<string> documents,
            int topN,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(documents
                .Select((_, index) => new RerankResult
                {
                    Index = index,
                    RelevanceScore = documents.Count - index
                })
                .Take(topN)
                .ToList());
        }
    }

    private sealed class ApiFakeTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var tokenCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return Enumerable.Range(1, tokenCount).ToList();
        }

        public string Decode(List<int> tokens)
        {
            return string.Join(" ", tokens.Select(token => $"t{token}"));
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }
    }
}

internal sealed class ApiInMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, Dictionary<string, VectorDocument>> collections = new(StringComparer.Ordinal);

    public void Seed(string collection, VectorDocument document)
    {
        GetCollection(collection)[document.Id] = Clone(document);
    }

    public Task<List<SearchResult>> QueryAsync(
        string collection,
        string query,
        int topK,
        float[]? queryEmbedding = null,
        float threshold = 0.2f,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetCollection(collection)
            .Values
            .Take(topK)
            .Select(document => new SearchResult
            {
                Id = document.Id,
                Score = 1,
                Content = document.Content,
                Metadata = Clone(document.Metadata)
            })
            .ToList());
    }

    public Task UpsertAsync(
        string collection,
        IEnumerable<VectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var document in documents)
        {
            Seed(collection, document);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = GetCollection(collection);
        foreach (var id in ids)
        {
            items.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<VectorDocument?> GetByIdAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetCollection(collection).TryGetValue(id, out var document) ? Clone(document) : null);
    }

    public Task<List<VectorDocument>> GetByIdsAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ids
            .Select(id => GetCollection(collection).TryGetValue(id, out var document) ? Clone(document) : null)
            .OfType<VectorDocument>()
            .ToList());
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
            Vector = [.. document.Vector],
            Metadata = Clone(document.Metadata)
        };
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}

internal sealed class ApiInMemoryGraphStore : IGraphStore
{
    private readonly Dictionary<string, GraphNode> nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphEdge> edges = new(StringComparer.Ordinal);

    public void SeedNode(string nodeId, Dictionary<string, object> properties)
    {
        nodes[nodeId] = new GraphNode
        {
            Id = nodeId,
            Properties = Clone(properties)
        };
    }

    public void SeedEdge(string sourceId, string targetId, Dictionary<string, object> properties)
    {
        edges[GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId)] = new GraphEdge
        {
            SourceId = sourceId,
            TargetId = targetId,
            Properties = Clone(properties)
        };
    }

    public Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodes.ContainsKey(nodeId));
    }

    public Task<bool> HasEdgeAsync(string sourceNodeId, string targetNodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(edges.ContainsKey(GraphSourceReferenceParser.MakeRelationKey(sourceNodeId, targetNodeId)));
    }

    public Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetNodeDegree(nodeId));
    }

    public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodes.TryGetValue(nodeId, out var node) ? Clone(node) : null);
    }

    public Task<GraphEdge?> GetEdgeAsync(string sourceNodeId, string targetNodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(edges.TryGetValue(GraphSourceReferenceParser.MakeRelationKey(sourceNodeId, targetNodeId), out var edge) ? Clone(edge) : null);
    }

    public Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
        string sourceNodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(edges.Values
            .Where(edge => edge.SourceId == sourceNodeId || edge.TargetId == sourceNodeId)
            .Select(edge => (edge.SourceId, edge.TargetId))
            .ToList());
    }

    public Task UpsertNodeAsync(string nodeId, Dictionary<string, object> nodeData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SeedNode(nodeId, nodeData);
        return Task.CompletedTask;
    }

    public Task UpsertEdgeAsync(
        string sourceNodeId,
        string targetNodeId,
        Dictionary<string, object> edgeData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SeedEdge(sourceNodeId, targetNodeId, edgeData);
        return Task.CompletedTask;
    }

    public Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nodes.Remove(nodeId);
        return Task.CompletedTask;
    }

    public Task RemoveEdgesAsync(List<(string SourceId, string TargetId)> edgePairs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (sourceId, targetId) in edgePairs)
        {
            edges.Remove(GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId));
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
        return Task.FromResult(new KnowledgeGraph
        {
            Nodes = nodes.Values.Select(node => new KnowledgeGraphNode
            {
                Id = node.Id,
                Properties = Clone(node.Properties)
            }).Take(maxNodes).ToList(),
            Edges = edges.Values.Select(edge => new KnowledgeGraphEdge
            {
                Source = edge.SourceId,
                Target = edge.TargetId,
                Properties = Clone(edge.Properties)
            }).ToList(),
            IsTruncated = nodes.Count > maxNodes
        });
    }

    public Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new List<string>());
    }

    public Task<List<string>> GetPopularLabelsAsync(int limit = 300, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new List<string>());
    }

    public Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodeIds
            .Where(nodes.ContainsKey)
            .ToDictionary(nodeId => nodeId, nodeId => Clone(nodes[nodeId]), StringComparer.Ordinal));
    }

    public Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodeIds.ToDictionary(nodeId => nodeId, GetNodeDegree, StringComparer.Ordinal));
    }

    public Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
        List<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => edges.Values
                .Where(edge => edge.SourceId == nodeId || edge.TargetId == nodeId)
                .Select(edge => (edge.SourceId, edge.TargetId))
                .ToList(),
            StringComparer.Ordinal));
    }

    public Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
        List<(string SourceId, string TargetId)> edgePairs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<(string SourceId, string TargetId), GraphEdge>();
        foreach (var pair in edgePairs)
        {
            if (edges.TryGetValue(GraphSourceReferenceParser.MakeRelationKey(pair.SourceId, pair.TargetId), out var edge))
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
        return Task.FromResult(edgePairs.ToDictionary(pair => pair, pair => GetNodeDegree(pair.SourceId) + GetNodeDegree(pair.TargetId)));
    }

    private int GetNodeDegree(string nodeId)
    {
        return edges.Values.Count(edge => edge.SourceId == nodeId || edge.TargetId == nodeId);
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static GraphNode Clone(GraphNode node)
    {
        return new GraphNode
        {
            Id = node.Id,
            Properties = Clone(node.Properties)
        };
    }

    private static GraphEdge Clone(GraphEdge edge)
    {
        return new GraphEdge
        {
            SourceId = edge.SourceId,
            TargetId = edge.TargetId,
            Properties = Clone(edge.Properties)
        };
    }
}

internal sealed class ApiInMemoryKvStore : IKVStore
{
    private readonly Dictionary<string, Dictionary<string, object>> items = new(StringComparer.Ordinal);

    public void Seed(string id, Dictionary<string, object> value)
    {
        items[id] = Clone(value);
    }

    public Task<Dictionary<string, object>?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.TryGetValue(id, out var item) ? Clone(item) : null);
    }

    public Task<List<Dictionary<string, object>>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ids
            .Where(items.ContainsKey)
            .Select(id => Clone(items[id]))
            .ToList());
    }

    public Task<HashSet<string>> FilterKeysAsync(HashSet<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(keys.Where(key => !items.ContainsKey(key)).ToHashSet(StringComparer.Ordinal));
    }

    public Task UpsertAsync(Dictionary<string, Dictionary<string, object>> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (id, value) in data)
        {
            items[id] = Clone(value);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var id in ids)
        {
            items.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count == 0);
    }

    public Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DropAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        items.Clear();
        return Task.CompletedTask;
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
