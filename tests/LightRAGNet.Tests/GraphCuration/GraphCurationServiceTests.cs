using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
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
    public async Task EditEntityAsync_WhenDocumentIdMatchesOldEntityName_DoesNotDeleteDocumentEntityIndex()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "ALPHA" });
        fixture.Graph.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["description"] = "alpha",
            ["source_id"] = "chunk-a"
        });
        fixture.FullEntities.Seed("ALPHA", new()
        {
            ["entity_names"] = new List<string> { "ALPHA", "OMEGA" },
            ["count"] = 2
        });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "BETA_RENAMED" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeTrue();
        ReadStrings(fixture.FullEntities.Items["ALPHA"], "entity_names")
            .Should().BeEquivalentTo(new[] { "BETA_RENAMED", "OMEGA" });
        fixture.FullEntities.Items["ALPHA"]["count"].Should().Be(2);
    }

    [Fact]
    public async Task EditEntityAsync_WhenSourceIdChanges_ReturnsValidationErrorWithoutMutation()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new()
        {
            ["entity_id"] = "ALPHA",
            ["description"] = "alpha",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md",
            ["created_at"] = "created"
        });

        var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["source_id"] = "chunk-b" },
            AllowRename: true,
            AllowMerge: false));

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be("validation_error");
        fixture.Graph.GetSeededNode("ALPHA")!.Properties["source_id"].Should().Be("chunk-a");
        fixture.VectorStore.UpsertCalls.Should().BeEmpty();
        fixture.VectorStore.DeleteCalls.Should().BeEmpty();
        fixture.FullEntities.UpsertCalls.Should().BeEmpty();
        fixture.EntityChunks.UpsertCalls.Should().BeEmpty();
        fixture.QueryRevisionBumps.Should().Be(0);
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

    [Fact]
    public async Task CreateRelationAsync_WhenEndpointsExist_WritesGraphVectorAndTracking()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "doc-1" });

        var result = await fixture.Service.CreateRelationAsync(new GraphRelationCreateRequest(
            "BETA",
            "ALPHA",
            new Dictionary<string, object>
            {
                ["description"] = "Alpha relates to beta",
                ["keywords"] = "related",
                ["weight"] = 2.5,
                ["source_id"] = "chunk-a",
                ["file_path"] = "doc.md"
            }));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["description"].Should().Be("Alpha relates to beta");
        var vector = fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA"));
        vector.Should().NotBeNull();
        vector!.Vector.Should().Equal(GraphCurationFixture.Embedding);
        vector.Content.Should().Contain("related");
        fixture.RelationChunks.Items["ALPHA<SEP>BETA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
        ReadRelationPairs(fixture.FullRelations.Items["doc-1"], "relation_pairs")
            .Should().BeEquivalentTo(new[] { new[] { "ALPHA", "BETA" } });
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task EditRelationAsync_WhenDescriptionChanges_UpdatesGraphAndRelationVector()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.TextChunks.Seed("chunk-a", new() { ["full_doc_id"] = "doc-1" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new()
        {
            ["description"] = "old",
            ["keywords"] = "old-keyword",
            ["source_id"] = "chunk-a",
            ["weight"] = 1.0
        });

        var result = await fixture.Service.EditRelationAsync(new GraphRelationEditRequest(
            "BETA",
            "ALPHA",
            new Dictionary<string, object> { ["description"] = "new", ["keywords"] = "new-keyword" }));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["description"].Should().Be("new");
        fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA"))!.Content
            .Should().Contain("new-keyword");
        ReadRelationPairs(fixture.FullRelations.Items["doc-1"], "relation_pairs")
            .Should().BeEquivalentTo(new[] { new[] { "ALPHA", "BETA" } });
        fixture.QueryRevisionBumps.Should().Be(1);
    }

    [Fact]
    public async Task CreateRelationAsync_WhenEndpointMissing_ReturnsValidationError()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });

        var result = await fixture.Service.CreateRelationAsync(new GraphRelationCreateRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object> { ["description"] = "rel" }));

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be("validation_error");
        fixture.QueryRevisionBumps.Should().Be(0);
    }

    [Fact]
    public async Task EditRelationAsync_WhenDescriptionIsBlank_ReturnsValidationErrorWithoutMutation()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new()
        {
            ["description"] = "old",
            ["keywords"] = "old-keyword",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md",
            ["created_at"] = "created"
        });

        var result = await fixture.Service.EditRelationAsync(new GraphRelationEditRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object> { ["description"] = " " }));

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be("validation_error");
        fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["description"].Should().Be("old");
        fixture.VectorStore.UpsertCalls.Should().BeEmpty();
        fixture.VectorStore.DeleteCalls.Should().BeEmpty();
        fixture.FullRelations.UpsertCalls.Should().BeEmpty();
        fixture.RelationChunks.UpsertCalls.Should().BeEmpty();
        fixture.QueryRevisionBumps.Should().Be(0);
    }

    [Theory]
    [InlineData("source_id", "chunk-b")]
    [InlineData("file_path", "other.md")]
    [InlineData("created_at", "later")]
    public async Task EditRelationAsync_WhenProvenanceFieldChanges_ReturnsValidationErrorWithoutMutation(
        string fieldName,
        string value)
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new()
        {
            ["description"] = "old",
            ["keywords"] = "old-keyword",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md",
            ["created_at"] = "created"
        });

        var result = await fixture.Service.EditRelationAsync(new GraphRelationEditRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object> { [fieldName] = value }));

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be("validation_error");
        fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties[fieldName].Should().NotBe(value);
        fixture.VectorStore.UpsertCalls.Should().BeEmpty();
        fixture.VectorStore.DeleteCalls.Should().BeEmpty();
        fixture.FullRelations.UpsertCalls.Should().BeEmpty();
        fixture.RelationChunks.UpsertCalls.Should().BeEmpty();
        fixture.QueryRevisionBumps.Should().Be(0);
    }

    [Fact]
    public async Task CreateRelationAsync_WhenSourceHasNoDocumentId_DoesNotWriteRelationKeyAsDocumentKey()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });

        var result = await fixture.Service.CreateRelationAsync(new GraphRelationCreateRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object>
            {
                ["description"] = "manual relation",
                ["source_id"] = "manual_creation"
            }));

        result.Succeeded.Should().BeTrue();
        fixture.FullRelations.Items.Should().NotContainKey("ALPHA<SEP>BETA");
        fixture.FullRelations.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EditRelationAsync_WhenSourceHasNoDocumentId_DoesNotWriteRelationKeyAsDocumentKey()
    {
        var fixture = GraphCurationFixture.Create();
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new()
        {
            ["description"] = "old",
            ["keywords"] = "old-keyword",
            ["source_id"] = "manual_creation"
        });

        var result = await fixture.Service.EditRelationAsync(new GraphRelationEditRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object> { ["description"] = "new" }));

        result.Succeeded.Should().BeTrue();
        fixture.FullRelations.Items.Should().NotContainKey("ALPHA<SEP>BETA");
        fixture.FullRelations.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRelationAsync_WhenEndpointRenameInProgress_WaitsForEntityLockAndRevalidates()
    {
        var graph = new PausingGraphStore("ALPHA_RENAMED");
        graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        var vectorStore = new InMemoryVectorStore();
        var textChunks = new InMemoryKvStore();
        var fullEntities = new InMemoryKvStore();
        var fullRelations = new InMemoryKvStore();
        var entityChunks = new InMemoryKvStore();
        var relationChunks = new InMemoryKvStore();
        var bumps = 0;
        var service = new GraphCurationService(
            graph,
            vectorStore,
            new FakeEmbeddingService(),
            textChunks,
            fullEntities,
            fullRelations,
            entityChunks,
            relationChunks,
            () =>
            {
                bumps++;
                return Task.CompletedTask;
            },
            NullLogger<GraphCurationService>.Instance);

        var renameTask = service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "ALPHA_RENAMED" },
            AllowRename: true,
            AllowMerge: false));
        await graph.BlockedOnUpsert.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var relationTask = service.CreateRelationAsync(new GraphRelationCreateRequest(
            "ALPHA",
            "BETA",
            new Dictionary<string, object> { ["description"] = "blocked while endpoint is renamed" }));

        await Task.Delay(100);
        relationTask.IsCompleted.Should().BeFalse();

        graph.ResumeUpsert.SetResult();
        var renameResult = await renameTask.WaitAsync(TimeSpan.FromSeconds(3));
        var relationResult = await relationTask.WaitAsync(TimeSpan.FromSeconds(3));

        renameResult.Succeeded.Should().BeTrue();
        relationResult.Succeeded.Should().BeFalse();
        relationResult.Status.Should().Be("validation_error");
        graph.GetSeededNode("ALPHA").Should().BeNull();
        graph.GetSeededNode("ALPHA_RENAMED").Should().NotBeNull();
        graph.GetSeededEdge("ALPHA", "BETA").Should().BeNull();
        vectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA")).Should().BeNull();
        bumps.Should().Be(1);
    }

    [Fact]
    public async Task EditEntityAsync_WhenBothRelationEndpointsRenameConcurrently_RewritesOnlyFinalRelation()
    {
        var graph = new PausingGraphStore("ALPHA2");
        graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
        graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
        graph.SeedEdge("ALPHA", "BETA", new()
        {
            ["description"] = "related",
            ["keywords"] = "uses",
            ["source_id"] = "chunk-r",
            ["weight"] = 1.0
        });
        var vectorStore = new InMemoryVectorStore();
        var textChunks = new InMemoryKvStore();
        var fullEntities = new InMemoryKvStore();
        var fullRelations = new InMemoryKvStore();
        var entityChunks = new InMemoryKvStore();
        var relationChunks = new InMemoryKvStore();
        textChunks.Seed("chunk-r", new() { ["full_doc_id"] = "doc-1" });
        fullRelations.Seed("doc-1", new()
        {
            ["relation_pairs"] = new List<string[]> { new[] { "ALPHA", "BETA" } },
            ["count"] = 1
        });
        relationChunks.Seed("ALPHA<SEP>BETA", new()
        {
            ["chunk_ids"] = new List<string> { "chunk-r" },
            ["count"] = 1
        });
        vectorStore.Seed("relationships", new VectorDocument
        {
            Id = GraphCurationVectorIds.Relation("ALPHA", "BETA"),
            Content = "ALPHA\nBETA\nuses\nrelated",
            Vector = [1.0f],
            Metadata = new Dictionary<string, object>()
        });
        var bumps = 0;
        var service = new GraphCurationService(
            graph,
            vectorStore,
            new FakeEmbeddingService(),
            textChunks,
            fullEntities,
            fullRelations,
            entityChunks,
            relationChunks,
            () =>
            {
                bumps++;
                return Task.CompletedTask;
            },
            NullLogger<GraphCurationService>.Instance);

        var alphaRenameTask = service.EditEntityAsync(new GraphEntityEditRequest(
            "ALPHA",
            new Dictionary<string, object> { ["entity_name"] = "ALPHA2" },
            AllowRename: true,
            AllowMerge: false));
        await graph.BlockedOnUpsert.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var betaRenameTask = service.EditEntityAsync(new GraphEntityEditRequest(
            "BETA",
            new Dictionary<string, object> { ["entity_name"] = "BETA2" },
            AllowRename: true,
            AllowMerge: false));

        await Task.Delay(100);
        betaRenameTask.IsCompleted.Should().BeFalse();

        graph.ResumeUpsert.SetResult();
        var alphaResult = await alphaRenameTask.WaitAsync(TimeSpan.FromSeconds(3));
        var betaResult = await betaRenameTask.WaitAsync(TimeSpan.FromSeconds(3));

        alphaResult.Succeeded.Should().BeTrue();
        betaResult.Succeeded.Should().BeTrue();
        graph.GetSeededNode("ALPHA").Should().BeNull();
        graph.GetSeededNode("BETA").Should().BeNull();
        graph.GetSeededNode("ALPHA2").Should().NotBeNull();
        graph.GetSeededNode("BETA2").Should().NotBeNull();
        graph.GetSeededEdge("ALPHA", "BETA").Should().BeNull();
        graph.GetSeededEdge("ALPHA", "BETA2").Should().BeNull();
        graph.GetSeededEdge("ALPHA2", "BETA").Should().BeNull();
        graph.GetSeededEdge("ALPHA2", "BETA2")!.Properties["description"].Should().Be("related");
        relationChunks.Items.Should().NotContainKey("ALPHA<SEP>BETA");
        relationChunks.Items.Should().NotContainKey("ALPHA<SEP>BETA2");
        relationChunks.Items.Should().NotContainKey("ALPHA2<SEP>BETA");
        relationChunks.Items["ALPHA2<SEP>BETA2"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-r" });
        ReadRelationPairs(fullRelations.Items["doc-1"], "relation_pairs")
            .Should().BeEquivalentTo(new[] { new[] { "ALPHA2", "BETA2" } });
        vectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA")).Should().BeNull();
        vectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA2")).Should().BeNull();
        vectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA2", "BETA")).Should().BeNull();
        vectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA2", "BETA2")).Should().NotBeNull();
        bumps.Should().Be(2);
    }

    private sealed class GraphCurationFixture
    {
        public static readonly float[] Embedding = FakeEmbeddingService.Embedding;

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

    private sealed class PausingGraphStore(string pausedUpsertNodeId) : IGraphStore
    {
        private readonly InMemoryGraphStore inner = new();

        public TaskCompletionSource BlockedOnUpsert { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ResumeUpsert { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SeedNode(string nodeId, Dictionary<string, object> properties) =>
            inner.SeedNode(nodeId, properties);

        public void SeedEdge(string sourceId, string targetId, Dictionary<string, object> properties) =>
            inner.SeedEdge(sourceId, targetId, properties);

        public GraphNode? GetSeededNode(string nodeId) =>
            inner.GetSeededNode(nodeId);

        public GraphEdge? GetSeededEdge(string sourceId, string targetId) =>
            inner.GetSeededEdge(sourceId, targetId);

        public Task<bool> HasNodeAsync(string nodeId, CancellationToken cancellationToken = default) =>
            inner.HasNodeAsync(nodeId, cancellationToken);

        public Task<bool> HasEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            CancellationToken cancellationToken = default) =>
            inner.HasEdgeAsync(sourceNodeId, targetNodeId, cancellationToken);

        public Task<int> GetNodeDegreeAsync(string nodeId, CancellationToken cancellationToken = default) =>
            inner.GetNodeDegreeAsync(nodeId, cancellationToken);

        public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default) =>
            inner.GetNodeAsync(nodeId, cancellationToken);

        public Task<GraphEdge?> GetEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            CancellationToken cancellationToken = default) =>
            inner.GetEdgeAsync(sourceNodeId, targetNodeId, cancellationToken);

        public Task<List<(string SourceId, string TargetId)>> GetNodeEdgesAsync(
            string sourceNodeId,
            CancellationToken cancellationToken = default) =>
            inner.GetNodeEdgesAsync(sourceNodeId, cancellationToken);

        public async Task UpsertNodeAsync(
            string nodeId,
            Dictionary<string, object> nodeData,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(nodeId, pausedUpsertNodeId, StringComparison.Ordinal))
            {
                BlockedOnUpsert.TrySetResult();
                await ResumeUpsert.Task.WaitAsync(cancellationToken);
            }

            await inner.UpsertNodeAsync(nodeId, nodeData, cancellationToken);
        }

        public Task UpsertEdgeAsync(
            string sourceNodeId,
            string targetNodeId,
            Dictionary<string, object> edgeData,
            CancellationToken cancellationToken = default) =>
            inner.UpsertEdgeAsync(sourceNodeId, targetNodeId, edgeData, cancellationToken);

        public Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default) =>
            inner.DeleteNodeAsync(nodeId, cancellationToken);

        public Task RemoveEdgesAsync(
            List<(string SourceId, string TargetId)> edges,
            CancellationToken cancellationToken = default) =>
            inner.RemoveEdgesAsync(edges, cancellationToken);

        public Task<KnowledgeGraph> GetKnowledgeGraphAsync(
            string nodeLabel,
            int maxDepth = 3,
            int maxNodes = 1000,
            CancellationToken cancellationToken = default) =>
            inner.GetKnowledgeGraphAsync(nodeLabel, maxDepth, maxNodes, cancellationToken);

        public Task<List<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) =>
            inner.GetAllLabelsAsync(cancellationToken);

        public Task<List<string>> GetPopularLabelsAsync(
            int limit = 300,
            CancellationToken cancellationToken = default) =>
            inner.GetPopularLabelsAsync(limit, cancellationToken);

        public Task<Dictionary<string, GraphNode>> GetNodesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default) =>
            inner.GetNodesBatchAsync(nodeIds, cancellationToken);

        public Task<Dictionary<string, int>> GetNodeDegreesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default) =>
            inner.GetNodeDegreesBatchAsync(nodeIds, cancellationToken);

        public Task<Dictionary<string, List<(string SourceId, string TargetId)>>> GetNodesEdgesBatchAsync(
            List<string> nodeIds,
            CancellationToken cancellationToken = default) =>
            inner.GetNodesEdgesBatchAsync(nodeIds, cancellationToken);

        public Task<Dictionary<(string SourceId, string TargetId), GraphEdge>> GetEdgesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default) =>
            inner.GetEdgesBatchAsync(edgePairs, cancellationToken);

        public Task<Dictionary<(string SourceId, string TargetId), int>> GetEdgeDegreesBatchAsync(
            List<(string SourceId, string TargetId)> edgePairs,
            CancellationToken cancellationToken = default) =>
            inner.GetEdgeDegreesBatchAsync(edgePairs, cancellationToken);
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
