using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed class OfflineRetrievalEvaluationTests
{
    [Fact]
    public async Task JsonOracleCases_MatchRawRetrievalData()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
        var fixture = await RetrievalEvaluationFixture.CreateFromDataSetAsync(dataSet);

        dataSet.Cases.Select(evaluationCase => evaluationCase.Name).Should().Contain(
            [
                "Local_UsesLowLevelEntityFocus",
                "Global_UsesHighLevelRelationshipFocus",
                "Rerank_KeepsRelevantChunkInFinalContext"
            ],
            "routing assertions should stay attached to their JSON cases");

        foreach (var evaluationCase in dataSet.Cases)
        {
            fixture.ApplyRankingHints(evaluationCase);
            var queryCallCountBefore = fixture.VectorStore.QueryCalls.Count;
            var result = await fixture.RunAsync(evaluationCase);

            RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
            AssertRoutingCalls(fixture, evaluationCase, queryCallCountBefore);
        }
    }

    [Fact]
    public void RetrievalEvaluationCase_LoadsExpectedOracleFieldsFromJson()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
        var evaluationCase = dataSet.Cases.Should()
            .ContainSingle(item => item.Name == "Rerank_KeepsRelevantChunkInFinalContext")
            .Subject;

        evaluationCase.ExpectedDocumentNames.Should().Equal("03_lightrag_improvements.md");
        evaluationCase.ExpectedChunkIds.Should().Equal("chunk-operations-health-cache");
        evaluationCase.ExpectedChunkOrder.Should().Equal(
            [
                "chunk-operations-health-cache",
                "chunk-storage-vector-databases",
                "chunk-evaluation-quality-metrics"
            ]);
        evaluationCase.VectorScoresByChunkId.Should().Contain(
            "chunk-storage-vector-databases",
            0.90f);
        evaluationCase.RerankScoresByContent.Should().Contain(
            "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.",
            0.99f);
    }

    [Fact]
    public async Task RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
        var fixture = await RetrievalEvaluationFixture.CreateFromDataSetAsync(dataSet);
        var expectedChunks = dataSet.Chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => (chunk.FilePath, chunk.Content),
            StringComparer.Ordinal);
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
            .Be(expectedChunks["chunk-architecture-rag-components"].FilePath);

        var architectureChunk = seededTextChunks["chunk-architecture-rag-components"];
        architectureChunk.Should().NotBeNull();
        architectureChunk[RetrievalEvaluationCorpus.ContentKey].Should().BeOfType<string>()
            .Which.Should().Contain("retrieval system");

        var vectorChunks = fixture.VectorStore.Collections[RetrievalEvaluationCorpus.ChunksCollection];
        var textChunkItems = fixture.TextChunks.Items;
        vectorChunks.Should().HaveCount(5);
        textChunkItems.Should().HaveCount(5);
        vectorChunks.Keys.Should().BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
        textChunkItems.Keys.Should().BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
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

        foreach (var entity in dataSet.Entities)
        {
            var node = fixture.GraphStore.GetSeededNode(entity.Id);
            node.Should().NotBeNull();
            node!.Properties["source_id"].Should().Be(entity.SourceId);
            node.Properties[RetrievalEvaluationCorpus.FilePathKey].Should().Be(entity.FilePath);
            node.Properties["entity_type"].Should().Be(entity.Type);
        }

        foreach (var relationship in dataSet.Relationships)
        {
            var edge = fixture.GraphStore.GetSeededEdge(relationship.SourceId, relationship.TargetId);
            edge.Should().NotBeNull();
            edge!.Properties["source_id"].Should().Be(relationship.SourceIdList);
            edge.Properties["keywords"].Should().Be(relationship.Keywords);
            edge.Properties["description"].Should().Be(relationship.Description);
            edge.Properties["weight"].Should().Be(relationship.Weight);
        }

        var entityVectors = fixture.VectorStore.Collections["entities"];
        entityVectors.Keys.Should().BeEquivalentTo(dataSet.Entities.Select(entity => $"entity-{entity.Id}"));
        foreach (var entity in dataSet.Entities)
        {
            var vector = entityVectors[$"entity-{entity.Id}"];
            vector.Metadata["entity_name"].Should().Be(entity.Id);
            vector.Metadata["entity_type"].Should().Be(entity.Type);
            vector.Metadata["description"].Should().Be(entity.Description);
            vector.Metadata["source_id"].Should().Be(entity.SourceId);
            vector.Metadata["file_path"].Should().Be(entity.FilePath);
        }

        var relationshipVectors = fixture.VectorStore.Collections["relationships"];
        relationshipVectors.Keys.Should().BeEquivalentTo(
            dataSet.Relationships.Select(relationship => $"relationship-{relationship.SourceId}-{relationship.TargetId}"));
        foreach (var relationship in dataSet.Relationships)
        {
            var vector = relationshipVectors[$"relationship-{relationship.SourceId}-{relationship.TargetId}"];
            vector.Metadata["src_id"].Should().Be(relationship.SourceId);
            vector.Metadata["tgt_id"].Should().Be(relationship.TargetId);
            vector.Metadata["keywords"].Should().Be(relationship.Keywords);
            vector.Metadata["description"].Should().Be(relationship.Description);
            vector.Metadata["source_id"].Should().Be(relationship.SourceIdList);
            vector.Metadata["weight"].Should().Be(relationship.Weight);
        }
    }

    private static void AssertRoutingCalls(
        RetrievalEvaluationFixture fixture,
        RetrievalEvaluationCase evaluationCase,
        int queryCallCountBefore)
    {
        var newQueryCalls = fixture.VectorStore.QueryCalls.Skip(queryCallCountBefore);

        switch (evaluationCase.Name)
        {
            case "Local_UsesLowLevelEntityFocus":
                newQueryCalls.Should().Contain(
                    call => call.Collection == "entities"
                            && call.Query == "RETRIEVAL_SYSTEM"
                            && call.TopK == 3,
                    "Local evaluation should route low-level keywords to entity vector search");
                break;

            case "Global_UsesHighLevelRelationshipFocus":
                newQueryCalls.Should().Contain(
                    call => call.Collection == "relationships"
                            && call.Query == "rag architecture"
                            && call.TopK == 3,
                    "Global evaluation should route high-level keywords to relationship vector search");
                break;

            case "Rerank_KeepsRelevantChunkInFinalContext":
                newQueryCalls.Should().Contain(
                    call => call.Collection == "chunks"
                            && call.Query == evaluationCase.Query
                            && call.TopK == evaluationCase.ChunkTopK,
                    "rerank evaluation should use the production-requested chunk candidate count");
                break;
        }
    }
}
