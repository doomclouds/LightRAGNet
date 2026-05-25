using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationFixture
{
    private RetrievalEvaluationFixture(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks)
    {
        VectorStore = vectorStore;
        GraphStore = graphStore;
        TextChunks = textChunks;
    }

    public InMemoryVectorStore VectorStore { get; }

    public InMemoryGraphStore GraphStore { get; }

    public InMemoryKvStore TextChunks { get; }

    public static async Task<RetrievalEvaluationFixture> CreateAsync()
    {
        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();

        await RetrievalEvaluationCorpus.SeedAsync(vectorStore, graphStore, textChunks);

        return new RetrievalEvaluationFixture(vectorStore, graphStore, textChunks);
    }
}
