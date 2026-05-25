using LightRAGNet.Core.Interfaces;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationCorpus
{
    public const string ChunksCollection = "chunks";
    public const string FilePathKey = "file_path";
    public const string ChunkIdKey = "chunk_id";
    public const string ContentKey = "content";

    public const string OverviewPath = "docs/eval/01-lightrag-overview.md";
    public const string ArchitecturePath = "docs/eval/02-rag-architecture.md";
    public const string OperationsPath = "docs/eval/03-operations.md";
    public const string StoragePath = "docs/eval/04-supported-storage.md";
    public const string EvaluationPath = "docs/eval/05-evaluation.md";

    private static readonly IReadOnlyList<ChunkSpec> Chunks =
    [
        new(
            "chunk-overview-hallucination",
            OverviewPath,
            "LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references."),
        new(
            "chunk-architecture-rag-components",
            ArchitecturePath,
            "A RAG system requires a retrieval system, an embedding model, and a generation model."),
        new(
            "chunk-operations-health-cache",
            OperationsPath,
            "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows."),
        new(
            "chunk-storage-vector-databases",
            StoragePath,
            "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure."),
        new(
            "chunk-evaluation-quality-metrics",
            EvaluationPath,
            "Evaluation tracks faithfulness, answer relevance, context recall, and context precision.")
    ];

    public static async Task SeedAsync(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken = default)
    {
        SeedChunks(vectorStore);
        SeedGraph(graphStore);
        await SeedTextChunksAsync(textChunks, cancellationToken);
    }

    private static void SeedChunks(InMemoryVectorStore vectorStore)
    {
        foreach (var chunk in Chunks)
        {
            SeedChunk(vectorStore, chunk);
        }
    }

    private static void SeedChunk(InMemoryVectorStore vectorStore, ChunkSpec chunk)
    {
        vectorStore.Seed(ChunksCollection, new VectorDocument
        {
            Id = chunk.Id,
            Content = chunk.Content,
            Metadata = new Dictionary<string, object>
            {
                [FilePathKey] = chunk.FilePath,
                [ChunkIdKey] = chunk.Id
            }
        });
    }

    private static void SeedGraph(InMemoryGraphStore graphStore)
    {
        graphStore.SeedNode("RETRIEVAL_SYSTEM", new Dictionary<string, object>
        {
            ["entity_id"] = "RETRIEVAL_SYSTEM",
            ["entity_type"] = "Component",
            ["description"] = "Retrieves relevant documents for a query.",
            ["source_id"] = "chunk-architecture-rag-components",
            [FilePathKey] = ArchitecturePath
        });
        graphStore.SeedNode("EMBEDDING_MODEL", new Dictionary<string, object>
        {
            ["entity_id"] = "EMBEDDING_MODEL",
            ["entity_type"] = "Component",
            ["description"] = "Converts text into vectors for similarity retrieval.",
            ["source_id"] = "chunk-architecture-rag-components",
            [FilePathKey] = ArchitecturePath
        });
        graphStore.SeedNode("CACHE_MANAGEMENT", new Dictionary<string, object>
        {
            ["entity_id"] = "CACHE_MANAGEMENT",
            ["entity_type"] = "Operation",
            ["description"] = "Manages cache visibility and safe maintenance.",
            ["source_id"] = "chunk-operations-health-cache",
            [FilePathKey] = OperationsPath
        });

        graphStore.SeedEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL", new Dictionary<string, object>
        {
            ["keywords"] = "rag architecture",
            ["description"] = "Retrieval systems depend on embedding models for vector search.",
            ["weight"] = 3.0d,
            ["source_id"] = "chunk-architecture-rag-components"
        });
        graphStore.SeedEdge("CACHE_MANAGEMENT", "RETRIEVAL_SYSTEM", new Dictionary<string, object>
        {
            ["keywords"] = "operations retrieval",
            ["description"] = "Cache management protects retrieval operations during maintenance.",
            ["weight"] = 2.0d,
            ["source_id"] = "chunk-operations-health-cache"
        });
    }

    private static Task SeedTextChunksAsync(
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken)
    {
        var data = Chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => Chunk(chunk),
            StringComparer.Ordinal);

        return textChunks.UpsertAsync(data, cancellationToken);
    }

    private static Dictionary<string, object> Chunk(ChunkSpec chunk)
    {
        return new Dictionary<string, object>
        {
            [ContentKey] = chunk.Content,
            [FilePathKey] = chunk.FilePath
        };
    }

    private sealed record ChunkSpec(string Id, string FilePath, string Content);
}
