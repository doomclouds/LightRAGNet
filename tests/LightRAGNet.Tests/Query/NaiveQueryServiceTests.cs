using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;
using LightRAGNet.Tests.TestDoubles;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class NaiveQueryServiceTests
{
    [Fact]
    public async Task BuildContextAsync_WhenChunksExist_QueriesChunksCollectionAndBuildsRawData()
    {
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha beta content",
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/a.md"
            }
        });
        var service = CreateService(vectorStore);

        var result = await service.BuildContextAsync(
            "alpha",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                ChunkTopK = 3,
                TopK = 40,
                EnableRerank = false
            },
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Context.Should().Contain("alpha beta content");
        result.Context.Should().Contain("[1] docs/a.md");
        vectorStore.QueryCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Query == "alpha" &&
            call.TopK == 3);

        var data = result.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        data["entities"].Should().BeEquivalentTo(Array.Empty<object>());
        data["relationships"].Should().BeEquivalentTo(Array.Empty<object>());
        data["chunks"].Should().BeAssignableTo<List<Dictionary<string, object>>>();
        data["references"].Should().BeAssignableTo<List<Dictionary<string, object>>>();

        var chunks = (List<Dictionary<string, object>>)data["chunks"];
        chunks.Should().ContainSingle(chunk =>
            chunk["chunk_id"].Equals("chunk-a") &&
            chunk["content"].Equals("alpha beta content") &&
            chunk["file_path"].Equals("docs/a.md") &&
            chunk["reference_id"].Equals("1"));

        var references = (List<Dictionary<string, object>>)data["references"];
        references.Should().ContainSingle(reference =>
            reference["reference_id"].Equals("1") &&
            reference["file_path"].Equals("docs/a.md"));

        var metadata = result.RawData["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;
        metadata["query_mode"].Should().Be("Naive");
        metadata["keywords"].Should().BeOfType<Dictionary<string, object>>();
        metadata["processing_info"].Should().BeOfType<Dictionary<string, object>>();

        var processingInfo = (Dictionary<string, object>)metadata["processing_info"];
        processingInfo["total_chunks_found"].Should().Be(1);
        processingInfo["final_chunks_count"].Should().Be(1);
    }

    [Fact]
    public async Task BuildContextAsync_WhenNoChunks_ReturnsNull()
    {
        var service = CreateService(new InMemoryVectorStore());

        var result = await service.BuildContextAsync(
            "missing",
            new QueryParam { Mode = QueryMode.Naive },
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildContextAsync_WhenRerankEnabled_OrdersChunksByRerankScore()
    {
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "first content",
            Metadata = new Dictionary<string, object> { ["file_path"] = "docs/a.md" }
        });
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-b",
            Content = "second content",
            Metadata = new Dictionary<string, object> { ["file_path"] = "docs/b.md" }
        });
        var rerankService = Substitute.For<IRerankService>();
        rerankService
            .RerankAsync("alpha", Arg.Any<List<string>>(), 2, Arg.Any<CancellationToken>())
            .Returns([
                new RerankResult { Index = 0, RelevanceScore = 0.1f },
                new RerankResult { Index = 1, RelevanceScore = 0.9f },
                new RerankResult { Index = 99, RelevanceScore = 1.0f }
            ]);
        var service = CreateService(vectorStore, rerankService);

        var result = await service.BuildContextAsync(
            "alpha",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                ChunkTopK = 2,
                EnableRerank = true
            },
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Context.IndexOf("second content", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.Context.IndexOf("first content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildContextAsync_WhenPromptOverheadConsumesBudget_ReturnsNull()
    {
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha beta content",
            Metadata = new Dictionary<string, object> { ["file_path"] = "docs/a.md" }
        });
        var service = CreateService(vectorStore);

        var lowBudget = await service.BuildContextAsync(
            "alpha",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                MaxTotalTokens = 5,
                EnableRerank = false
            },
            CancellationToken.None);

        var normalBudget = await service.BuildContextAsync(
            "alpha",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                MaxTotalTokens = 1000,
                EnableRerank = false
            },
            CancellationToken.None);

        lowBudget.Should().BeNull();
        normalBudget.Should().NotBeNull();
        normalBudget!.Context.Should().Contain("alpha beta content");
    }

    private static NaiveQueryService CreateService(
        IVectorStore vectorStore,
        IRerankService? rerankService = null)
    {
        return new NaiveQueryService(
            vectorStore,
            rerankService ?? Substitute.For<IRerankService>(),
            new FakeTokenizer());
    }
}
