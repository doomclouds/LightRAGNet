using FluentAssertions;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryGraphStoreTests
{
    [Fact]
    public async Task InMemoryGraphStore_GetNodeAsync_ReturnsDeepClone()
    {
        var store = new InMemoryGraphStore();
        store.SeedNode("entity-a", new Dictionary<string, object>
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
        });

        var firstRead = await store.GetNodeAsync("entity-a");
        ((List<object>)firstRead!.Properties["source_ids"]).Add("chunk-b");
        ((List<object>)((Dictionary<string, object>)((List<object>)firstRead.Properties["source_ids"])[1])["nested"])
            .Add("nested-b");
        ((List<string>)firstRead.Properties["file_paths"]).Add("file-b.md");

        var secondRead = await store.GetNodeAsync("entity-a");

        ((List<object>)secondRead!.Properties["source_ids"]).Should().HaveCount(2);
        ((List<object>)((Dictionary<string, object>)((List<object>)secondRead.Properties["source_ids"])[1])["nested"])
            .Should()
            .Equal("nested-a");
        ((List<string>)secondRead.Properties["file_paths"]).Should().Equal("file-a.md");
    }
}
