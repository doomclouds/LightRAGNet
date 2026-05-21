using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.GraphCuration;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.GraphCuration;

public sealed class GraphCurationServiceTests
{
    [Fact]
    public void GraphCurationVectorIds_EntityId_UsesPythonStyleHashPrefix()
    {
        var id = GraphCurationVectorIds.Entity("ALPHA");

        id.Should().StartWith("ent-");
        id.Should().HaveLength("ent-".Length + 32);
    }

    [Fact]
    public void GraphCurationVectorIds_RelationIds_ReturnsCanonicalAndLegacyIds()
    {
        var ids = GraphCurationVectorIds.RelationIds("BETA", "ALPHA").ToList();

        ids.Should().HaveCount(2);
        ids[0].Should().Be(GraphCurationVectorIds.Relation("ALPHA", "BETA"));
        ids[1].Should().Be(GraphCurationVectorIds.Relation("BETA", "ALPHA"));
    }

    [Fact]
    public void EntityEditRequest_WhenDescriptionIsBlank_IsInvalid()
    {
        var request = new GraphEntityEditRequest(
            EntityName: "ALPHA",
            UpdatedData: new Dictionary<string, object> { ["description"] = " " },
            AllowRename: true,
            AllowMerge: false);

        request.HasBlankDescription().Should().BeTrue();
    }

    [Fact]
    public async Task CreateEntityAsync_WhenEntityIsNew_WritesGraphVectorAndTracking()
    {
        var fixture = GraphCurationFixture.Create();

        var result = await fixture.Service.CreateEntityAsync(new GraphEntityCreateRequest(
            "ALPHA",
            new Dictionary<string, object>
            {
                ["description"] = "Alpha description",
                ["entity_type"] = "Concept",
                ["source_id"] = "chunk-a",
                ["file_path"] = "doc.md"
            }));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededNode("ALPHA")!.Properties["description"].Should().Be("Alpha description");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Content
            .Should().Be("ALPHA\nAlpha description");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Vector
            .Should().Equal(FakeEmbeddingService.Embedding);
        fixture.EntityChunks.Items["ALPHA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenDescriptionChanges_UpdatesGraphAndEntityVector()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["entity_type"] = "Concept",
            ["description"] = "old",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md"
        });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["description"] = "new" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededNode("ALPHA")!.Properties["description"].Should().Be("new");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Content
            .Should().Be("ALPHA\nnew");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Vector
            .Should().Equal(FakeEmbeddingService.Embedding);
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenRenameSucceeds_PreservesConnectedEdgesAndMovesEntityRecords()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["entity_type"] = "Concept",
            ["description"] = "alpha",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md"
        });
        fixture.Graph.SeedNode("GAMMA", new() { ["entity_id"] = "GAMMA", ["description"] = "gamma" });
        fixture.Graph.SeedEdge("ALPHA", "GAMMA", new()
        {
            ["description"] = "related",
            ["source_id"] = "chunk-r",
            ["weight"] = 1
        });
        fixture.VectorStore.Seed("entities", new VectorDocument
        {
            Id = GraphCurationVectorIds.Entity("ALPHA"),
            Content = "ALPHA\nalpha",
            Vector = [1.0f],
            Metadata = new Dictionary<string, object>()
        });
        fixture.FullEntities.Seed("ALPHA", new() { ["entity_id"] = "ALPHA" });
        fixture.EntityChunks.Seed("ALPHA", new()
        {
            ["chunk_ids"] = new List<string> { "chunk-a" },
            ["count"] = 1
        });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "BETA" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededNode("ALPHA").Should().BeNull();
        fixture.Graph.GetSeededNode("BETA")!.Properties["entity_id"].Should().Be("BETA");
        fixture.Graph.GetSeededEdge("ALPHA", "GAMMA").Should().BeNull();
        fixture.Graph.GetSeededEdge("BETA", "GAMMA")!.Properties["description"].Should().Be("related");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA")).Should().BeNull();
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("BETA"))!.Vector
            .Should().Equal(FakeEmbeddingService.Embedding);
        fixture.FullEntities.Items.Should().NotContainKey("ALPHA");
        fixture.FullEntities.Items.Should().ContainKey("BETA");
        fixture.EntityChunks.Items.Should().NotContainKey("ALPHA");
        fixture.EntityChunks.Items["BETA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenRenameConflictsAndMergeDisabled_ReturnsConflict()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "BETA" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be("conflict");
        fixture.Graph.GetSeededNode("ALPHA").Should().NotBeNull();
        fixture.VectorStore.UpsertCalls.Should().BeEmpty();
        fixture.VectorStore.DeleteCalls.Should().BeEmpty();
        fixture.FullEntities.UpsertCalls.Should().BeEmpty();
        fixture.FullEntities.DeleteCalls.Should().BeEmpty();
        fixture.EntityChunks.UpsertCalls.Should().BeEmpty();
        fixture.EntityChunks.DeleteCalls.Should().BeEmpty();
        fixture.QueryRevisionBumps.Should().Be(0);
    }

    private sealed class GraphCurationFixture
    {
        public InMemoryGraphStore Graph { get; } = new();
        public InMemoryVectorStore VectorStore { get; } = new();
        public FakeEmbeddingService EmbeddingService { get; } = new();
        public InMemoryKvStore FullEntities { get; } = new();
        public InMemoryKvStore FullRelations { get; } = new();
        public InMemoryKvStore EntityChunks { get; } = new();
        public InMemoryKvStore RelationChunks { get; } = new();
        public int QueryRevisionBumps { get; private set; }
        public GraphCurationService Service { get; }

        private GraphCurationFixture()
        {
            Service = new GraphCurationService(
                Graph,
                VectorStore,
                EmbeddingService,
                FullEntities,
                FullRelations,
                EntityChunks,
                RelationChunks,
                () =>
                {
                    QueryRevisionBumps++;
                    return Task.CompletedTask;
                },
                NullLogger<GraphCurationService>.Instance);
        }

        public static GraphCurationFixture Create() => new();
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public static readonly float[] Embedding = [0.25f, 0.75f];

        public int EmbeddingDimension => Embedding.Length;

        public int MaxTokenSize => 8192;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Embedding.ToArray());
        }

        public Task<float[][]> GenerateEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => Embedding.ToArray()).ToArray());
        }
    }
}
