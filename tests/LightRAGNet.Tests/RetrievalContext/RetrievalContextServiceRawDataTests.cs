using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Storage;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextServiceRawDataTests
{
    [Fact]
    public async Task BuildQueryContextAsync_WhenKgResultsExist_IncludesStructuredRawData()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore.QueryAsync(
                "entities",
                "alpha",
                Arg.Any<int>(),
                Arg.Any<float[]?>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new SearchResult
                {
                    Id = "entity-alpha",
                    Content = "Alpha entity",
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = "Alpha",
                        ["entity_type"] = "Concept",
                        ["description"] = "Alpha description",
                        ["source_id"] = "chunk-a",
                        ["file_path"] = "docs/a.md"
                    }
                }
            ]);

        var graphStore = Substitute.For<IGraphStore>();
        graphStore.GetNodesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, GraphNode>
            {
                ["Alpha"] = new()
                {
                    Id = "Alpha",
                    Properties = new Dictionary<string, object>
                    {
                        ["entity_type"] = "Concept",
                        ["description"] = "Alpha description",
                        ["source_id"] = "chunk-a",
                        ["file_path"] = "docs/a.md"
                    }
                }
            });
        graphStore.GetNodeDegreesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["Alpha"] = 1 });
        graphStore.GetNodesEdgesBatchAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, List<(string SourceId, string TargetId)>>
            {
                ["Alpha"] = [("Alpha", "Beta")]
            });
        graphStore.GetEdgesBatchAsync(Arg.Any<List<(string SourceId, string TargetId)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(string SourceId, string TargetId), GraphEdge>
            {
                [("Alpha", "Beta")] = new()
                {
                    SourceId = "Alpha",
                    TargetId = "Beta",
                    Properties = new Dictionary<string, object>
                    {
                        ["keywords"] = "depends on",
                        ["description"] = "Alpha depends on Beta",
                        ["weight"] = 2.5d,
                        ["source_id"] = "chunk-b"
                    }
                }
            });
        graphStore.GetEdgeDegreesBatchAsync(Arg.Any<List<(string SourceId, string TargetId)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(string SourceId, string TargetId), int>
            {
                [("Alpha", "Beta")] = 3
            });

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f]);

        var textChunks = new InMemoryKvStore();
        await textChunks.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["chunk-a"] = new()
            {
                ["content"] = "chunk content",
                ["file_path"] = "docs/a.md"
            },
            ["chunk-b"] = new()
            {
                ["content"] = "relationship chunk content",
                ["file_path"] = "docs/b.md"
            }
        });

        var service = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            textChunks,
            Options.Create(new LightRAGOptions { KgChunkPickMethod = "WEIGHT" }),
            NullLoggerFactory.Instance);

        var result = await service.BuildQueryContextAsync(
            "alpha",
            new KeywordsResult
            {
                HighLevelKeywords = ["overview"],
                LowLevelKeywords = ["alpha"]
            },
            new QueryParam { Mode = QueryMode.Local, EnableRerank = false },
            CancellationToken.None);

        result.Should().NotBeNull();

        var data = result!.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        data.Should().ContainKeys("entities", "relationships", "chunks", "references");

        var entities = data["entities"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        entities.Should().ContainSingle().Which.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["entity_name"] = "Alpha",
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["rank"] = 1,
            ["source_id"] = "chunk-a",
            ["file_path"] = "docs/a.md"
        });

        var relationships = data["relationships"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        relationships.Should().ContainSingle().Which.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["src_id"] = "Alpha",
            ["tgt_id"] = "Beta",
            ["keywords"] = "depends on",
            ["description"] = "Alpha depends on Beta",
            ["rank"] = 3,
            ["weight"] = 2.5d,
            ["source_id"] = "chunk-b"
        });

        var chunks = data["chunks"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        chunks.Should().BeEquivalentTo([
            new Dictionary<string, object>
            {
                ["chunk_id"] = "chunk-a",
                ["content"] = "chunk content",
                ["file_path"] = "docs/a.md",
                ["reference_id"] = "1"
            },
            new Dictionary<string, object>
            {
                ["chunk_id"] = "chunk-b",
                ["content"] = "relationship chunk content",
                ["file_path"] = "docs/b.md",
                ["reference_id"] = "2"
            }
        ], options => options.WithStrictOrdering());

        var references = data["references"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        references.Should().BeEquivalentTo([
            new Dictionary<string, object>
            {
                ["reference_id"] = "1",
                ["file_path"] = "docs/a.md"
            },
            new Dictionary<string, object>
            {
                ["reference_id"] = "2",
                ["file_path"] = "docs/b.md"
            }
        ], options => options.WithStrictOrdering());

        var metadata = result.RawData["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;
        metadata["query_mode"].Should().Be("Local");
        metadata["high_level_keywords"].Should().BeEquivalentTo(new[] { "overview" });
        metadata["low_level_keywords"].Should().BeEquivalentTo(new[] { "alpha" });

        var keywords = metadata["keywords"].Should().BeOfType<Dictionary<string, object>>().Subject;
        keywords["high_level"].Should().BeEquivalentTo(new[] { "overview" });
        keywords["low_level"].Should().BeEquivalentTo(new[] { "alpha" });

        var processingInfo = metadata["processing_info"].Should().BeOfType<Dictionary<string, object>>().Subject;
        processingInfo["total_entities_found"].Should().Be(1);
        processingInfo["total_relations_found"].Should().Be(1);
        processingInfo["final_chunks_count"].Should().Be(2);
    }
}
