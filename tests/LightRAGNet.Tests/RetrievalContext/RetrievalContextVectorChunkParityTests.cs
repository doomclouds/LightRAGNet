using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextVectorChunkParityTests
{
    private const string Sep = "<SEP>";

    [Fact]
    public async Task BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid",
            ["file_path"] = "docs/alpha.md"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Ids.Order().SequenceEqual(new[] { "chunk-far", "chunk-mid", "chunk-near" }.Order()));
    }

    private static TestHarness CreateHarness(LightRAGOptions? options = null)
    {
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.0f]);

        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        var service = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            textChunks,
            Options.Create(options ?? new LightRAGOptions
            {
                KgChunkPickMethod = "VECTOR",
                RelatedChunkNumber = 4
            }),
            NullLoggerFactory.Instance);

        return new TestHarness(service, vectorStore, graphStore, textChunks);
    }

    private static async Task SeedTextChunksAsync(
        InMemoryKvStore textChunks,
        IEnumerable<(string Id, string Content, string FilePath)> chunks)
    {
        await textChunks.UpsertAsync(chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => new Dictionary<string, object>
            {
                ["content"] = chunk.Content,
                ["file_path"] = chunk.FilePath
            }));
    }

    private static void SeedChunkVectors(
        InMemoryVectorStore vectorStore,
        IEnumerable<(string Id, float[] Vector)> chunks)
    {
        foreach (var (id, vector) in chunks)
        {
            vectorStore.Seed("chunks", new VectorDocument
            {
                Id = id,
                Vector = vector,
                Content = $"{id} content",
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["file_path"] = $"docs/{id}.md"
                }
            });
        }
    }

    private static List<string> GetChunkIds(QueryContextResult result)
    {
        var data = result.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var chunks = data["chunks"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        return chunks.Select(chunk => chunk["chunk_id"].ToString()!).ToList();
    }

    private sealed record TestHarness(
        RetrievalContextService Service,
        InMemoryVectorStore VectorStore,
        InMemoryGraphStore GraphStore,
        InMemoryKvStore TextChunks);
}
