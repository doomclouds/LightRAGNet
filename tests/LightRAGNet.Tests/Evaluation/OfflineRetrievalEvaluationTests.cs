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
        var architectureVector = fixture.VectorStore.Get(
            RetrievalEvaluationCorpus.ChunksCollection,
            "chunk-architecture-rag-components");

        architectureVector.Should().NotBeNull();
        architectureVector!.Metadata[RetrievalEvaluationCorpus.FilePathKey]
            .Should()
            .Be(RetrievalEvaluationCorpus.ArchitecturePath);
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
        architectureChunk![RetrievalEvaluationCorpus.ContentKey].Should().BeOfType<string>()
            .Which.Should().Contain("retrieval system");

        var vectorChunks = fixture.VectorStore.Collections[RetrievalEvaluationCorpus.ChunksCollection];
        var textChunkItems = fixture.TextChunks.Items;
        vectorChunks.Should().HaveCount(5);
        textChunkItems.Should().HaveCount(5);
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
}
