using LightRAGNet.Core.Interfaces;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationCorpus
{
    public const string ChunksCollection = "chunks";
    public const string FilePathKey = "file_path";
    public const string ChunkIdKey = "chunk_id";
    public const string ContentKey = "content";

    public static async Task SeedAsync(
        RetrievalEvaluationDataSet dataSet,
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken = default)
    {
        SeedChunks(dataSet, vectorStore);
        SeedGraph(dataSet, graphStore);
        await SeedTextChunksAsync(dataSet, textChunks, cancellationToken);
    }

    private static void SeedChunks(
        RetrievalEvaluationDataSet dataSet,
        InMemoryVectorStore vectorStore)
    {
        foreach (var chunk in dataSet.Chunks)
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
    }

    private static void SeedGraph(
        RetrievalEvaluationDataSet dataSet,
        InMemoryGraphStore graphStore)
    {
        foreach (var entity in dataSet.Entities)
        {
            graphStore.SeedNode(entity.Id, new Dictionary<string, object>
            {
                ["entity_id"] = entity.Id,
                ["entity_type"] = entity.Type,
                ["description"] = entity.Description,
                ["source_id"] = entity.SourceId,
                [FilePathKey] = entity.FilePath
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
        }
    }

    private static Task SeedTextChunksAsync(
        RetrievalEvaluationDataSet dataSet,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken)
    {
        var data = dataSet.Chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => Chunk(chunk),
            StringComparer.Ordinal);

        return textChunks.UpsertAsync(data, cancellationToken);
    }

    private static Dictionary<string, object> Chunk(RetrievalEvaluationChunkSpec chunk)
    {
        return new Dictionary<string, object>
        {
            [ContentKey] = chunk.Content,
            [FilePathKey] = chunk.FilePath
        };
    }
}
