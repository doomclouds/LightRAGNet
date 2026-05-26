using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationFixture
{
    // The offline KG oracle uses WEIGHT to keep related chunk selection deterministic.
    private const string EvaluationKgChunkPickMethod = "WEIGHT";

    private readonly NaiveQueryService naiveQueryService;
    private readonly RetrievalContextService retrievalContextService;

    private RetrievalEvaluationFixture(
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        NaiveQueryService naiveQueryService,
        RetrievalContextService retrievalContextService)
    {
        VectorStore = vectorStore;
        GraphStore = graphStore;
        TextChunks = textChunks;
        this.naiveQueryService = naiveQueryService;
        this.retrievalContextService = retrievalContextService;
    }

    public InMemoryVectorStore VectorStore { get; }

    public InMemoryGraphStore GraphStore { get; }

    public InMemoryKvStore TextChunks { get; }

    public static Task<RetrievalEvaluationFixture> CreateAsync()
    {
        return CreateCoreAsync(dataSet: null, rerankService: null);
    }

    public static Task<RetrievalEvaluationFixture> CreateAsync(IRerankService? rerankService)
    {
        return CreateCoreAsync(dataSet: null, rerankService: rerankService);
    }

    public static Task<RetrievalEvaluationFixture> CreateFromDataSetAsync(
        RetrievalEvaluationDataSet dataSet,
        IRerankService? rerankService = null)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        return CreateCoreAsync(dataSet, rerankService);
    }

    private static async Task<RetrievalEvaluationFixture> CreateCoreAsync(
        RetrievalEvaluationDataSet? dataSet,
        IRerankService? rerankService)
    {
        dataSet ??= RetrievalEvaluationDataLoader.LoadDefault();

        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        var tokenizer = new FakeTokenizer();
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService ?? Substitute.For<IRerankService>(),
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f, 0.3f]);

        await RetrievalEvaluationCorpus.SeedAsync(dataSet, vectorStore, graphStore, textChunks);
        SeedKnowledgeGraphVectors(dataSet, vectorStore);

        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankCoordinator,
            tokenizer,
            textChunks,
            Options.Create(new LightRAGOptions { KgChunkPickMethod = EvaluationKgChunkPickMethod }),
            NullLoggerFactory.Instance);

        return new RetrievalEvaluationFixture(
            vectorStore,
            graphStore,
            textChunks,
            new NaiveQueryService(vectorStore, rerankCoordinator, tokenizer),
            retrievalContextService);
    }

    private static void SeedKnowledgeGraphVectors(
        RetrievalEvaluationDataSet dataSet,
        InMemoryVectorStore vectorStore)
    {
        foreach (var entity in dataSet.Entities)
        {
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

        var keywords = new KeywordsResult
        {
            HighLevelKeywords = [.. evaluationCase.HighLevelKeywords],
            LowLevelKeywords = [.. evaluationCase.LowLevelKeywords]
        };

        var contextResult = await retrievalContextService.BuildQueryContextAsync(
            evaluationCase.Query,
            keywords,
            queryParam,
            CancellationToken.None);

        return RetrievalEvaluationResult.FromRawData(contextResult?.RawData);
    }
}

public sealed record RetrievalEvaluationResult(Dictionary<string, object>? RawData)
{
    public static RetrievalEvaluationResult FromRawData(Dictionary<string, object>? rawData)
    {
        return new RetrievalEvaluationResult(rawData);
    }
}
