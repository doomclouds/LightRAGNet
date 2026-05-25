using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed class OfflineRetrievalEvaluationTests
{
    [Fact]
    public async Task Naive_ReturnsExpectedArchitectureChunk()
    {
        var fixture = await RetrievalEvaluationFixture.CreateAsync();
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Naive_ReturnsExpectedArchitectureChunk",
            Query: "Which components are required in a RAG system?",
            Mode: QueryMode.Naive,
            HighLevelKeywords: [],
            LowLevelKeywords: [],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
            ExpectedEntityIds: [],
            ExpectedRelationshipPairs: [],
            ForbiddenChunkIds: ["chunk-operations-health-cache"],
            EnableRerank: false);

        var result = await fixture.RunAsync(evaluationCase);

        RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
        RetrievalEvaluationRunner.AssertChunkIds(
            result,
            evaluationCase,
            [
                "chunk-overview-hallucination",
                "chunk-architecture-rag-components"
            ]);
    }

    [Fact]
    public async Task Local_UsesLowLevelEntityFocus()
    {
        var fixture = await RetrievalEvaluationFixture.CreateAsync();
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Local_UsesLowLevelEntityFocus",
            Query: "How does the retrieval system work?",
            Mode: QueryMode.Local,
            HighLevelKeywords: [],
            LowLevelKeywords: ["RETRIEVAL_SYSTEM"],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
            ExpectedEntityIds: ["RETRIEVAL_SYSTEM"],
            ExpectedRelationshipPairs: [new ExpectedRelationshipPair("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")],
            ForbiddenChunkIds: [],
            EnableRerank: false);

        var result = await fixture.RunAsync(evaluationCase);

        RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
        fixture.VectorStore.QueryCalls.Should().Contain(
            call => call.Collection == "entities"
                    && call.Query == "RETRIEVAL_SYSTEM"
                    && call.TopK == 3,
            "Local evaluation should route low-level keywords to entity vector search");
    }

    [Fact]
    public async Task Global_UsesHighLevelRelationshipFocus()
    {
        var fixture = await RetrievalEvaluationFixture.CreateAsync();
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Global_UsesHighLevelRelationshipFocus",
            Query: "Which architecture relationship connects retrieval and embedding?",
            Mode: QueryMode.Global,
            HighLevelKeywords: ["rag architecture"],
            LowLevelKeywords: [],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
            ExpectedEntityIds: ["RETRIEVAL_SYSTEM", "EMBEDDING_MODEL"],
            ExpectedRelationshipPairs: [new ExpectedRelationshipPair("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")],
            ForbiddenChunkIds: [],
            EnableRerank: false);

        var result = await fixture.RunAsync(evaluationCase);

        RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
        fixture.VectorStore.QueryCalls.Should().Contain(
            call => call.Collection == "relationships"
                    && call.Query == "rag architecture"
                    && call.TopK == 3,
            "Global evaluation should route high-level keywords to relationship vector search");
    }

    [Fact]
    public async Task Mix_ReturnsKgEntityRelationshipAndRelatedChunk()
    {
        var fixture = await RetrievalEvaluationFixture.CreateAsync();
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Mix_ReturnsKgEntityRelationshipAndRelatedChunk",
            Query: "How do retrieval and embedding work together in RAG architecture?",
            Mode: QueryMode.Mix,
            HighLevelKeywords: ["rag architecture"],
            LowLevelKeywords: ["RETRIEVAL_SYSTEM"],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.ArchitecturePath],
            ExpectedEntityIds: ["RETRIEVAL_SYSTEM"],
            ExpectedRelationshipPairs: [new ExpectedRelationshipPair("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")],
            ForbiddenChunkIds: [],
            EnableRerank: false);

        var result = await fixture.RunAsync(evaluationCase);

        RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
    }

    [Fact]
    public async Task Rerank_KeepsRelevantChunkInFinalContext()
    {
        var rerankService = new DeterministicEvaluationRerankService(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["Operations include health checks, cache management, deployment readiness, and safe maintenance workflows."] = 0.99f,
            ["LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure."] = 0.10f,
            ["Evaluation tracks faithfulness, answer relevance, context recall, and context precision."] = 0.05f
        });
        var fixture = await RetrievalEvaluationFixture.CreateAsync(rerankService);
        fixture.VectorStore.QueryScoresByDocumentId["chunk-storage-vector-databases"] = 0.90f;
        fixture.VectorStore.QueryScoresByDocumentId["chunk-evaluation-quality-metrics"] = 0.80f;
        fixture.VectorStore.QueryScoresByDocumentId["chunk-operations-health-cache"] = 0.70f;
        fixture.VectorStore.QueryScoresByDocumentId["chunk-architecture-rag-components"] = 0.20f;
        fixture.VectorStore.QueryScoresByDocumentId["chunk-overview-hallucination"] = 0.10f;

        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Rerank_KeepsRelevantChunkInFinalContext",
            Query: "Which operational workflow covers cache and health checks?",
            Mode: QueryMode.Naive,
            HighLevelKeywords: [],
            LowLevelKeywords: [],
            TopK: 5,
            ChunkTopK: 3,
            ExpectedChunkIds: ["chunk-operations-health-cache"],
            ExpectedReferenceFilePaths: [RetrievalEvaluationCorpus.OperationsPath],
            ExpectedEntityIds: [],
            ExpectedRelationshipPairs: [],
            ForbiddenChunkIds: ["chunk-overview-hallucination"],
            EnableRerank: true);

        var result = await fixture.RunAsync(evaluationCase);

        RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
        RetrievalEvaluationRunner.AssertChunkIds(
            result,
            evaluationCase,
            [
                "chunk-operations-health-cache",
                "chunk-storage-vector-databases",
                "chunk-evaluation-quality-metrics"
            ]);
        fixture.VectorStore.QueryCalls.Should().Contain(
            call => call.Collection == "chunks"
                    && call.Query == evaluationCase.Query
                    && call.TopK == 3,
            "rerank evaluation should use the production-requested chunk candidate count");
    }

    [Fact]
    public void RetrievalEvaluationCase_CapturesExpectedOracleFields()
    {
        var evaluationCase = new RetrievalEvaluationCase(
            Name: "Naive_ReturnsExpectedArchitectureChunk",
            Query: "Which components are required in a RAG system?",
            Mode: QueryMode.Naive,
            HighLevelKeywords: [],
            LowLevelKeywords: [],
            TopK: 3,
            ChunkTopK: 2,
            ExpectedChunkIds: ["chunk-architecture-rag-components"],
            ExpectedReferenceFilePaths: ["docs/eval/02-rag-architecture.md"],
            ExpectedEntityIds: [],
            ExpectedRelationshipPairs: [],
            ForbiddenChunkIds: ["chunk-storage-vector-databases"],
            EnableRerank: false);

        evaluationCase.Name.Should().Be("Naive_ReturnsExpectedArchitectureChunk");
        evaluationCase.Mode.Should().Be(QueryMode.Naive);
        evaluationCase.ExpectedChunkIds.Should().ContainSingle("chunk-architecture-rag-components");
        evaluationCase.ForbiddenChunkIds.Should().ContainSingle("chunk-storage-vector-databases");
    }

    [Fact]
    public async Task RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks()
    {
        var fixture = await RetrievalEvaluationFixture.CreateAsync();
        var expectedChunks = new Dictionary<string, (string FilePath, string Content)>
        {
            ["chunk-overview-hallucination"] = (
                RetrievalEvaluationCorpus.OverviewPath,
                "LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references."),
            ["chunk-architecture-rag-components"] = (
                RetrievalEvaluationCorpus.ArchitecturePath,
                "A RAG system requires a retrieval system, an embedding model, and a generation model."),
            ["chunk-operations-health-cache"] = (
                RetrievalEvaluationCorpus.OperationsPath,
                "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows."),
            ["chunk-storage-vector-databases"] = (
                RetrievalEvaluationCorpus.StoragePath,
                "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure."),
            ["chunk-evaluation-quality-metrics"] = (
                RetrievalEvaluationCorpus.EvaluationPath,
                "Evaluation tracks faithfulness, answer relevance, context recall, and context precision.")
        };
        var seededVectors = new Dictionary<string, VectorDocument>();
        var seededTextChunks = new Dictionary<string, Dictionary<string, object>>();

        foreach (var (chunkId, expected) in expectedChunks)
        {
            var vectorDocument = fixture.VectorStore.Get(
                RetrievalEvaluationCorpus.ChunksCollection,
                chunkId);
            vectorDocument.Should().NotBeNull();
            vectorDocument!.Content.Should().Be(expected.Content);
            vectorDocument.Metadata[RetrievalEvaluationCorpus.ChunkIdKey]
                .Should()
                .Be(chunkId);
            vectorDocument.Metadata[RetrievalEvaluationCorpus.FilePathKey]
                .Should()
                .Be(expected.FilePath);
            seededVectors[chunkId] = vectorDocument;

            var textChunk = await fixture.TextChunks.GetByIdAsync(chunkId, CancellationToken.None);
            textChunk.Should().NotBeNull();
            textChunk![RetrievalEvaluationCorpus.ContentKey]
                .Should()
                .Be(expected.Content);
            textChunk[RetrievalEvaluationCorpus.FilePathKey]
                .Should()
                .Be(expected.FilePath);
            seededTextChunks[chunkId] = textChunk;
        }

        var architectureVector = seededVectors["chunk-architecture-rag-components"];
        architectureVector.Should().NotBeNull();
        architectureVector.Metadata[RetrievalEvaluationCorpus.FilePathKey]
            .Should()
            .Be(RetrievalEvaluationCorpus.ArchitecturePath);
        fixture.GraphStore.GetSeededNode("RETRIEVAL_SYSTEM")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")
            .Should()
            .NotBeNull();

        var architectureChunk = seededTextChunks["chunk-architecture-rag-components"];
        architectureChunk.Should().NotBeNull();
        architectureChunk[RetrievalEvaluationCorpus.ContentKey].Should().BeOfType<string>()
            .Which.Should().Contain("retrieval system");

        var vectorChunks = fixture.VectorStore.Collections[RetrievalEvaluationCorpus.ChunksCollection];
        var textChunkItems = fixture.TextChunks.Items;
        vectorChunks.Should().HaveCount(5);
        textChunkItems.Should().HaveCount(5);
        vectorChunks.Keys.Should().BeEquivalentTo(expectedChunks.Keys);
        textChunkItems.Keys.Should().BeEquivalentTo(expectedChunks.Keys);
        vectorChunks.Keys.Should().BeEquivalentTo(textChunkItems.Keys);

        foreach (var (chunkId, document) in vectorChunks)
        {
            document.Metadata.Should().ContainKey(RetrievalEvaluationCorpus.ChunkIdKey);
            document.Metadata.Should().ContainKey(RetrievalEvaluationCorpus.FilePathKey);
            document.Metadata[RetrievalEvaluationCorpus.ChunkIdKey]
                .Should()
                .Be(chunkId);
            document.Metadata[RetrievalEvaluationCorpus.FilePathKey]
                .Should()
                .BeOfType<string>()
                .Which.Should()
                .NotBeNullOrWhiteSpace();
        }

        foreach (var chunk in textChunkItems.Values)
        {
            chunk.Should().ContainKey(RetrievalEvaluationCorpus.ContentKey);
            chunk.Should().ContainKey(RetrievalEvaluationCorpus.FilePathKey);
            chunk[RetrievalEvaluationCorpus.ContentKey]
                .Should()
                .BeOfType<string>()
                .Which.Should()
                .NotBeNullOrWhiteSpace();
            chunk[RetrievalEvaluationCorpus.FilePathKey]
                .Should()
                .BeOfType<string>()
                .Which.Should()
                .NotBeNullOrWhiteSpace();
        }

        fixture.GraphStore.GetSeededNode("EMBEDDING_MODEL")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededNode("CACHE_MANAGEMENT")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededEdge("CACHE_MANAGEMENT", "RETRIEVAL_SYSTEM")
            .Should()
            .NotBeNull();
    }

    private sealed class DeterministicEvaluationRerankService(
        IReadOnlyDictionary<string, float> scoresByDocument) : IRerankService
    {
        public Task<List<RerankResult>> RerankAsync(
            string query,
            List<string> documents,
            int topN,
            CancellationToken cancellationToken = default)
        {
            var results = documents
                .Select((document, index) => new RerankResult
                {
                    Index = index,
                    RelevanceScore = scoresByDocument.TryGetValue(document, out var score) ? score : 0.0f
                })
                .OrderByDescending(result => result.RelevanceScore)
                .Take(topN)
                .ToList();

            return Task.FromResult(results);
        }
    }
}
