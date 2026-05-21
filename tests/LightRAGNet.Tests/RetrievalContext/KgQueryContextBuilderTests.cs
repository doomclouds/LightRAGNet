using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class KgQueryContextBuilderTests
{
    [Fact]
    public void Build_EmitsStructuredJsonSectionsAndReferenceIds()
    {
        var builder = new KgQueryContextBuilder(new FakeTokenizer());
        var searchResult = new KGSearchResult
        {
            Entities =
            [
                new EntityData
                {
                    Name = "ALPHA",
                    Type = "concept",
                    Description = "Alpha description",
                    Rank = 2,
                    SourceId = "chunk-a",
                    FilePath = "docs/a.md"
                }
            ],
            Relations =
            [
                new RelationData
                {
                    SourceId = "ALPHA",
                    TargetId = "BETA",
                    Keywords = "depends on",
                    Description = "Alpha depends on Beta",
                    Rank = 3,
                    Weight = 2.5d,
                    RSourceId = "chunk-b"
                }
            ],
            Chunks =
            [
                new ChunkData
                {
                    ChunkId = "chunk-a",
                    Content = "Alpha chunk content",
                    FilePath = "docs/a.md"
                },
                new ChunkData
                {
                    ChunkId = "chunk-b",
                    Content = "Beta chunk content",
                    FilePath = "docs/b.md"
                }
            ]
        };

        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxTotalTokens = 30000,
                MaxEntityTokens = 1000,
                MaxRelationTokens = 1000
            },
            query: "alpha");

        result.Context.Should().Contain("Knowledge Graph Data (Entity):");
        result.Context.Should().Contain("""{"entity":"ALPHA","type":"concept","description":"Alpha description"}""");
        result.Context.Should().Contain("Knowledge Graph Data (Relationship):");
        result.Context.Should().Contain("""{"entity1":"ALPHA","entity2":"BETA","keywords":"depends on","description":"Alpha depends on Beta"}""");
        result.Context.Should().Contain("Document Chunks (Each entry has a reference_id refer to the `Reference Document List`):");
        result.Context.Should().Contain("""{"reference_id":"1","content":"Alpha chunk content"}""");
        result.Context.Should().Contain("""{"reference_id":"2","content":"Beta chunk content"}""");
        result.Context.Should().Contain("[1] docs/a.md");
        result.Context.Should().Contain("[2] docs/b.md");
        result.Chunks.Select(chunk => chunk.ReferenceId).Should().Equal("1", "2");
        result.References.Select(reference => reference.ReferenceId).Should().Equal("1", "2");
    }

    [Fact]
    public void Build_LimitsEntitiesUsingJsonContextShape()
    {
        var builder = new KgQueryContextBuilder(new WhitespaceTokenizer());
        var searchResult = new KGSearchResult
        {
            Entities =
            [
                new EntityData { Name = "ALPHA", Type = "concept", Description = "short" },
                new EntityData { Name = "BETA", Type = "concept", Description = "second" }
            ]
        };

        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxEntityTokens = 7,
                MaxRelationTokens = 1000,
                MaxTotalTokens = 30000
            },
            query: "alpha");

        result.Entities.Should().ContainSingle(entity => entity.Name == "ALPHA");
        result.Context.Should().Contain("""{"entity":"ALPHA","type":"concept","description":"short"}""");
        result.Context.Should().NotContain("BETA");
    }

    [Fact]
    public void Build_WhenEntitySectionFitsBudgetExactly_DoesNotApplySyntheticJsonLineCost()
    {
        var builder = new KgQueryContextBuilder(new WhitespaceTokenizer());
        var searchResult = new KGSearchResult
        {
            Entities =
            [
                new EntityData { Name = "ALPHA", Type = "concept", Description = "short" },
                new EntityData { Name = "BETA", Type = "concept", Description = "second" }
            ]
        };

        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxEntityTokens = 8,
                MaxRelationTokens = 1000,
                MaxTotalTokens = 30000
            },
            query: "alpha");

        result.Entities.Select(entity => entity.Name).Should().Equal("ALPHA", "BETA");
        result.Context.Should().Contain("""{"entity":"ALPHA","type":"concept","description":"short"}""");
        result.Context.Should().Contain("""{"entity":"BETA","type":"concept","description":"second"}""");
    }

    [Fact]
    public void Build_LimitsRelationshipsUsingJsonContextShape()
    {
        var builder = new KgQueryContextBuilder(new WhitespaceTokenizer());
        var searchResult = new KGSearchResult
        {
            Relations =
            [
                new RelationData
                {
                    SourceId = "ALPHA",
                    TargetId = "BETA",
                    Keywords = "depends",
                    Description = "short"
                },
                new RelationData
                {
                    SourceId = "BETA",
                    TargetId = "GAMMA",
                    Keywords = "blocks",
                    Description = "second"
                }
            ]
        };

        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxEntityTokens = 1000,
                MaxRelationTokens = 7,
                MaxTotalTokens = 30000
            },
            query: "alpha");

        result.Relations.Should().ContainSingle(relation => relation.SourceId == "ALPHA");
        result.Context.Should().Contain("""{"entity1":"ALPHA","entity2":"BETA","keywords":"depends","description":"short"}""");
        result.Context.Should().NotContain("GAMMA");
    }

    [Fact]
    public void Build_WhenChunkBudgetCannotFitReferenceList_DropsChunks()
    {
        var builder = new KgQueryContextBuilder(new FakeTokenizer());
        var searchResult = new KGSearchResult
        {
            Chunks =
            [
                new ChunkData
                {
                    ChunkId = "chunk-a",
                    Content = "alpha",
                    FilePath = "docs/a.md"
                }
            ]
        };

        // Budget is intentionally above the payload-only cost but below the final chunk section plus reference list cost.
        var result = builder.Build(
            searchResult,
            new QueryParam
            {
                MaxEntityTokens = 1000,
                MaxRelationTokens = 1000,
                MaxTotalTokens = 205
            },
            query: "alpha");

        result.Chunks.Should().BeEmpty();
        result.References.Should().BeEmpty();
        result.Context.Should().NotContain("Document Chunks");
    }

    private sealed class WhitespaceTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var tokenCount = text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Length;

            return Enumerable.Range(1, tokenCount).ToList();
        }

        public string Decode(List<int> tokens)
        {
            return string.Join(" ", tokens.Select(token => $"t{token}"));
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }
    }
}
