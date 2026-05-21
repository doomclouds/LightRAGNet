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
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "doc-1" });

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
        ReadStrings(fixture.FullEntities.Items["doc-1"], "entity_names").Should().Contain("ALPHA");
        fixture.FullEntities.Items["doc-1"]["count"].Should().Be(1);
        fixture.FullEntities.Items.Should().NotContainKey("ALPHA");
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenDescriptionChanges_UpdatesGraphAndEntityVector()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "doc-1" });
        fixture.FullEntities.Seed("doc-1", new()
        {
            ["entity_names"] = new List<string> { "ALPHA" },
            ["count"] = 1
        });
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
        ReadStrings(fixture.FullEntities.Items["doc-1"], "entity_names").Should().Contain("ALPHA");
        fixture.FullEntities.Items.Should().NotContainKey("ALPHA");
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenRenameSucceeds_PreservesConnectedEdgesAndMovesEntityRecords()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "doc-1" });
        fixture.TextChunks.Seed("chunk-r", new() { ["full_doc_id"] = "doc-1" });
        fixture.TextChunks.Seed("chunk-s", new() { ["full_doc_id"] = "doc-1" });
        fixture.Graph.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["entity_type"] = "Concept",
            ["description"] = "alpha",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md"
        });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.Graph.SeedNode("GAMMA", new() { ["entity_id"] = "GAMMA", ["description"] = "gamma" });
        fixture.Graph.SeedEdge("ALPHA", "GAMMA", new()
        {
            ["description"] = "related",
            ["keywords"] = "uses",
            ["source_id"] = "chunk-r<SEP>chunk-s",
            ["weight"] = 1.25,
            ["file_path"] = "rel.md"
        });
        var oldRelationKey = "ALPHA<SEP>GAMMA";
        var newRelationKey = "BETA_RENAMED<SEP>GAMMA";
        var oldRelationVectorId = GraphCurationVectorIds.Relation("ALPHA", "GAMMA");
        var newRelationVectorId = GraphCurationVectorIds.Relation("BETA_RENAMED", "GAMMA");
        fixture.FullEntities.Seed("doc-1", new()
        {
            ["entity_names"] = new List<string> { "ALPHA", "GAMMA" },
            ["count"] = 2
        });
        fixture.FullRelations.Seed("doc-1", new()
        {
            ["relation_pairs"] = new List<string[]> { new[] { "ALPHA", "GAMMA" } },
            ["count"] = 1
        });
        fixture.VectorStore.Seed("relationships", new VectorDocument
        {
            Id = oldRelationVectorId,
            Content = "ALPHA\nGAMMA\nuses\nrelated",
            Vector = [1.0f],
            Metadata = new Dictionary<string, object>
            {
                ["src_id"] = "ALPHA",
                ["tgt_id"] = "GAMMA"
            }
        });
        fixture.VectorStore.Seed("entities", new VectorDocument
        {
            Id = GraphCurationVectorIds.Entity("ALPHA"),
            Content = "ALPHA\nalpha",
            Vector = [1.0f],
            Metadata = new Dictionary<string, object>()
        });
        fixture.EntityChunks.Seed("ALPHA", new()
        {
            ["chunk_ids"] = new List<string> { "chunk-a" },
            ["count"] = 1
        });
        fixture.RelationChunks.Seed(oldRelationKey, new()
        {
            ["chunk_ids"] = new List<string> { "chunk-r", "chunk-s" },
            ["count"] = 2
        });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "BETA_RENAMED" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededNode("ALPHA").Should().BeNull();
        fixture.Graph.GetSeededNode("BETA_RENAMED")!.Properties["entity_id"].Should().Be("BETA_RENAMED");
        fixture.Graph.GetSeededEdge("ALPHA", "GAMMA").Should().BeNull();
        fixture.Graph.GetSeededEdge("BETA_RENAMED", "GAMMA")!.Properties["description"].Should().Be("related");
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA")).Should().BeNull();
        fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("BETA_RENAMED"))!.Vector
            .Should().Equal(FakeEmbeddingService.Embedding);
        fixture.VectorStore.Get("relationships", oldRelationVectorId).Should().BeNull();
        var newRelationVector = fixture.VectorStore.Get("relationships", newRelationVectorId);
        newRelationVector.Should().NotBeNull();
        newRelationVector!.Vector.Should().Equal(FakeEmbeddingService.Embedding);
        newRelationVector.Metadata["src_id"].Should().Be("BETA_RENAMED");
        newRelationVector.Metadata["tgt_id"].Should().Be("GAMMA");
        fixture.FullEntities.Items.Should().NotContainKey("ALPHA");
        ReadStrings(fixture.FullEntities.Items["doc-1"], "entity_names")
            .Should().BeEquivalentTo(new[] { "BETA_RENAMED", "GAMMA" });
        fixture.FullEntities.Items["doc-1"]["count"].Should().Be(2);
        fixture.EntityChunks.Items.Should().NotContainKey("ALPHA");
        fixture.EntityChunks.Items["BETA_RENAMED"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
        fixture.RelationChunks.Items.Should().NotContainKey(oldRelationKey);
        fixture.RelationChunks.Items[newRelationKey]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-r", "chunk-s" });
        ReadRelationPairs(fixture.FullRelations.Items["doc-1"], "relation_pairs")
            .Should().BeEquivalentTo(new[] { new[] { "BETA_RENAMED", "GAMMA" } });
        fixture.FullRelations.Items["doc-1"]["count"].Should().Be(1);
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task CreateEntityAsync_WhenSourceHasNoDocumentId_DoesNotWriteEntityNameKey()
    {
        var fixture = GraphCurationFixture.Create();

        var result = await fixture.Service.CreateEntityAsync(new GraphEntityCreateRequest(
            "MANUAL",
            new Dictionary<string, object>
            {
                ["description"] = "Manual description",
                ["entity_type"] = "Concept",
                ["source_id"] = "manual_creation",
                ["file_path"] = ""
            }));

        result.Succeeded.Should().BeTrue();
        fixture.FullEntities.Items.Should().NotContainKey("MANUAL");
        fixture.FullEntities.Items.Should().BeEmpty();
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
        public InMemoryKvStore TextChunks { get; } = new();
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
                TextChunks,
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

    private static IReadOnlyList<string> ReadStrings(Dictionary<string, object> data, string key)
    {
        return data[key] switch
        {
            IEnumerable<string> strings => strings.ToList(),
            IEnumerable<object> objects => objects.Select(item => item.ToString() ?? string.Empty).ToList(),
            _ => []
        };
    }

    private static IReadOnlyList<string[]> ReadRelationPairs(Dictionary<string, object> data, string key)
    {
        return data[key] switch
        {
            IEnumerable<string[]> pairs => pairs.Select(pair => pair.ToArray()).ToList(),
            IEnumerable<object> objects => objects
                .OfType<IEnumerable<object>>()
                .Select(pair => pair.Select(item => item.ToString() ?? string.Empty).ToArray())
                .ToList(),
            _ => []
        };
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
