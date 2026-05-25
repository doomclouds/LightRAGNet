using FluentAssertions;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed class OfflineRetrievalEvaluationTests
{
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

        fixture.VectorStore.Get("chunks", "chunk-architecture-rag-components")
            .Should()
            .NotBeNull();
        fixture.VectorStore.Get("chunks", "chunk-architecture-rag-components")!
            .Metadata["file_path"]
            .Should()
            .Be("docs/eval/02-rag-architecture.md");
        fixture.GraphStore.GetSeededNode("RETRIEVAL_SYSTEM")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")
            .Should()
            .NotBeNull();

        var architectureChunk = await fixture.TextChunks.GetByIdAsync(
            "chunk-architecture-rag-components",
            CancellationToken.None);
        architectureChunk.Should().NotBeNull();
        architectureChunk!["content"].Should().BeOfType<string>()
            .Which.Should().Contain("retrieval system");
    }
}
