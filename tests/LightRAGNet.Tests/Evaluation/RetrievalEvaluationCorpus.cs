using LightRAGNet.Core.Interfaces;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationCorpus
{
    public const string OverviewPath = "docs/eval/01-lightrag-overview.md";
    public const string ArchitecturePath = "docs/eval/02-rag-architecture.md";
    public const string OperationsPath = "docs/eval/03-operations.md";
    public const string StoragePath = "docs/eval/04-supported-storage.md";
    public const string EvaluationPath = "docs/eval/05-evaluation.md";

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
        SeedChunk(
            vectorStore,
            "chunk-overview-hallucination",
            OverviewPath,
            "LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references.");
        SeedChunk(
            vectorStore,
            "chunk-architecture-rag-components",
            ArchitecturePath,
            "A RAG system requires a retrieval system, an embedding model, and a generation model.");
        SeedChunk(
            vectorStore,
            "chunk-operations-health-cache",
            OperationsPath,
            "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.");
        SeedChunk(
            vectorStore,
            "chunk-storage-vector-databases",
            StoragePath,
            "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure.");
        SeedChunk(
            vectorStore,
            "chunk-evaluation-quality-metrics",
            EvaluationPath,
            "Evaluation tracks faithfulness, answer relevance, context recall, and context precision.");
    }

    private static void SeedChunk(
        InMemoryVectorStore vectorStore,
        string chunkId,
        string filePath,
        string content)
    {
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = chunkId,
            Content = content,
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["chunk_id"] = chunkId
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
            ["file_path"] = ArchitecturePath
        });
        graphStore.SeedNode("EMBEDDING_MODEL", new Dictionary<string, object>
        {
            ["entity_id"] = "EMBEDDING_MODEL",
            ["entity_type"] = "Component",
            ["description"] = "Converts text into vectors for similarity retrieval.",
            ["source_id"] = "chunk-architecture-rag-components",
            ["file_path"] = ArchitecturePath
        });
        graphStore.SeedNode("CACHE_MANAGEMENT", new Dictionary<string, object>
        {
            ["entity_id"] = "CACHE_MANAGEMENT",
            ["entity_type"] = "Operation",
            ["description"] = "Manages cache visibility and safe maintenance.",
            ["source_id"] = "chunk-operations-health-cache",
            ["file_path"] = OperationsPath
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
        return textChunks.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["chunk-overview-hallucination"] = Chunk("LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references.", OverviewPath),
            ["chunk-architecture-rag-components"] = Chunk("A RAG system requires a retrieval system, an embedding model, and a generation model.", ArchitecturePath),
            ["chunk-operations-health-cache"] = Chunk("Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.", OperationsPath),
            ["chunk-storage-vector-databases"] = Chunk("LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure.", StoragePath),
            ["chunk-evaluation-quality-metrics"] = Chunk("Evaluation tracks faithfulness, answer relevance, context recall, and context precision.", EvaluationPath)
        }, cancellationToken);
    }

    private static Dictionary<string, object> Chunk(string content, string filePath)
    {
        return new Dictionary<string, object>
        {
            ["content"] = content,
            ["file_path"] = filePath
        };
    }
}
