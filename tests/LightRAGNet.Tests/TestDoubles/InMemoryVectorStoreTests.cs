using FluentAssertions;
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryVectorStoreTests
{
    [Fact]
    public async Task InMemoryVectorStore_GetByIdAsync_ReturnsDeepClone()
    {
        var store = new InMemoryVectorStore();
        store.Seed("entities", new VectorDocument
        {
            Id = "entity-a",
            Vector = [1.0f, 2.0f],
            Metadata = new Dictionary<string, object>
            {
                ["source_ids"] = new List<object>
                {
                    "chunk-a",
                    new Dictionary<string, object>
                    {
                        ["nested"] = new List<object> { "nested-a" }
                    }
                },
                ["file_paths"] = new List<string> { "file-a.md" }
            },
            Content = "entity content"
        });

        var firstRead = await store.GetByIdAsync("entities", "entity-a");
        firstRead!.Vector[0] = 99.0f;
        ((List<object>)firstRead.Metadata["source_ids"]).Add("chunk-b");
        ((List<object>)((Dictionary<string, object>)((List<object>)firstRead.Metadata["source_ids"])[1])["nested"])
            .Add("nested-b");
        ((List<string>)firstRead.Metadata["file_paths"]).Add("file-b.md");

        var secondRead = await store.GetByIdAsync("entities", "entity-a");

        secondRead!.Vector.Should().Equal(1.0f, 2.0f);
        ((List<object>)secondRead.Metadata["source_ids"]).Should().HaveCount(2);
        ((List<object>)((Dictionary<string, object>)((List<object>)secondRead.Metadata["source_ids"])[1])["nested"])
            .Should()
            .Equal("nested-a");
        ((List<string>)secondRead.Metadata["file_paths"]).Should().Equal("file-a.md");
    }

    [Fact]
    public async Task InMemoryVectorStore_Collections_ReturnsSnapshot()
    {
        var store = new InMemoryVectorStore();
        store.Seed("entities", new VectorDocument
        {
            Id = "entity-a",
            Vector = [1.0f, 2.0f],
            Metadata = new Dictionary<string, object>
            {
                ["source_ids"] = new List<object> { "chunk-a" }
            }
        });

        var collections = store.Collections;
        collections["entities"]["entity-a"].Vector[0] = 99.0f;
        ((List<object>)collections["entities"]["entity-a"].Metadata["source_ids"]).Add("chunk-b");
        collections["entities"].Remove("entity-a");

        var stored = await store.GetByIdAsync("entities", "entity-a");

        stored.Should().NotBeNull();
        stored!.Vector.Should().Equal(1.0f, 2.0f);
        ((List<object>)stored.Metadata["source_ids"]).Should().Equal("chunk-a");
    }
}
