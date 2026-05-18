using FluentAssertions;
using LightRAGNet.Services.DocumentDeletion;

namespace LightRAGNet.Tests.DocumentDeletion;

public sealed class GraphSourceReferenceParserTests
{
    [Fact]
    public void Split_RemovesEmptyValuesAndPreservesOrder()
    {
        var sourceIds = GraphSourceReferenceParser.Split("chunk-a<SEP><SEP>chunk-b<SEP>chunk-a<SEP>  <SEP>chunk-c");

        sourceIds.Should().Equal("chunk-a", "chunk-b", "chunk-c");
    }

    [Fact]
    public void Prune_RemovesDeletedSourcesAndPreservesRemainingOrder()
    {
        var remaining = GraphSourceReferenceParser.Prune(
            "chunk-a<SEP>chunk-b<SEP>chunk-c<SEP>chunk-b",
            new HashSet<string> { "chunk-a", "chunk-c" });

        remaining.Should().Equal("chunk-b");
    }

    [Fact]
    public void Join_UsesPythonGraphFieldSeparator()
    {
        var sourceId = GraphSourceReferenceParser.Join(["chunk-a", "", "chunk-b", "  ", "chunk-c"]);

        sourceId.Should().Be("chunk-a<SEP>chunk-b<SEP>chunk-c");
    }

    [Fact]
    public void MakeRelationKey_SortsEndpoints()
    {
        var relationKey = GraphSourceReferenceParser.MakeRelationKey("zeta", "alpha");

        relationKey.Should().Be("alpha<SEP>zeta");
    }
}
