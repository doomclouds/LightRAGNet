using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationFixture
{
    private readonly NaiveQueryService naiveQueryService;

    private RetrievalEvaluationFixture(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        NaiveQueryService naiveQueryService)
    {
        VectorStore = vectorStore;
        GraphStore = graphStore;
        TextChunks = textChunks;
        this.naiveQueryService = naiveQueryService;
    }

    public InMemoryVectorStore VectorStore { get; }

    public InMemoryGraphStore GraphStore { get; }

    public InMemoryKvStore TextChunks { get; }

    public static async Task<RetrievalEvaluationFixture> CreateAsync(
        IRerankService? rerankService = null)
    {
        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        var tokenizer = new FakeTokenizer();
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService ?? Substitute.For<IRerankService>(),
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);

        await RetrievalEvaluationCorpus.SeedAsync(vectorStore, graphStore, textChunks);

        return new RetrievalEvaluationFixture(
            vectorStore,
            graphStore,
            textChunks,
            new NaiveQueryService(vectorStore, rerankCoordinator, tokenizer));
    }

    public async Task<RetrievalEvaluationResult> RunAsync(RetrievalEvaluationCase evaluationCase)
    {
        var queryParam = new QueryParam
        {
            Mode = evaluationCase.Mode,
            TopK = evaluationCase.TopK,
            ChunkTopK = evaluationCase.ChunkTopK,
            HighLevelKeywords = [.. evaluationCase.HighLevelKeywords],
            LowLevelKeywords = [.. evaluationCase.LowLevelKeywords],
            EnableRerank = evaluationCase.EnableRerank
        };

        if (evaluationCase.Mode == QueryMode.Naive)
        {
            var result = await naiveQueryService.BuildContextAsync(
                evaluationCase.Query,
                queryParam,
                CancellationToken.None);

            return RetrievalEvaluationResult.FromRawData(result?.RawData);
        }

        throw new NotSupportedException($"Evaluation mode '{evaluationCase.Mode}' is not wired yet.");
    }
}

public sealed record RetrievalEvaluationResult(Dictionary<string, object>? RawData)
{
    public static RetrievalEvaluationResult FromRawData(Dictionary<string, object>? rawData)
    {
        return new RetrievalEvaluationResult(rawData);
    }
}
