using FluentAssertions;
using LightRAGNet.Services.GraphCuration;

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
}
