using FluentAssertions;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryKvStoreTests
{
    [Fact]
    public async Task InMemoryKvStore_FilterKeysAsync_ReturnsMissingKeysLikeProduction()
    {
        var store = new InMemoryKvStore();
        store.Seed("existing", new Dictionary<string, object>());

        var missingKeys = await store.FilterKeysAsync(["existing", "missing"]);

        missingKeys.Should().Equal("missing");
    }

    [Fact]
    public async Task InMemoryKvStore_GetByIdAsync_ReturnsDeepClone()
    {
        var store = new InMemoryKvStore();
        store.Seed("chunk-a", new Dictionary<string, object>
        {
            ["object_list"] = new List<object>
            {
                "source-a",
                new Dictionary<string, object>
                {
                    ["nested"] = new List<object> { "nested-a" }
                }
            },
            ["string_list"] = new List<string> { "file-a" }
        });

        var firstRead = await store.GetByIdAsync("chunk-a");
        ((List<object>)firstRead!["object_list"]).Add("source-b");
        ((List<object>)((Dictionary<string, object>)((List<object>)firstRead["object_list"])[1])["nested"]).Add("nested-b");
        ((List<string>)firstRead["string_list"]).Add("file-b");

        var secondRead = await store.GetByIdAsync("chunk-a");

        secondRead.Should().NotBeNull();
        ((List<object>)secondRead!["object_list"]).Should().HaveCount(2);
        ((List<object>)((Dictionary<string, object>)((List<object>)secondRead["object_list"])[1])["nested"])
            .Should()
            .Equal("nested-a");
        ((List<string>)secondRead["string_list"]).Should().Equal("file-a");
    }

    [Fact]
    public async Task InMemoryKvStore_Items_ReturnsSnapshot()
    {
        var store = new InMemoryKvStore();
        store.Seed("chunk-a", new Dictionary<string, object>
        {
            ["object_list"] = new List<object> { "source-a" }
        });

        var snapshot = store.Items;
        ((List<object>)snapshot["chunk-a"]["object_list"]).Add("source-b");
        snapshot.Remove("chunk-a");

        var stored = await store.GetByIdAsync("chunk-a");

        stored.Should().NotBeNull();
        ((List<object>)stored!["object_list"]).Should().Equal("source-a");
    }

    [Fact]
    public async Task InMemoryKvStore_SnapshotAsync_ReturnsSafeInspectableDescriptors()
    {
        var store = new InMemoryKvStore();
        store.Seed("Mix:query:abcdef0123456789", new Dictionary<string, object>
        {
            ["return"] = "secret return provider payload",
            ["cache_type"] = "query",
            ["original_prompt"] = "secret prompt api_key authorization",
            ["queryparam"] = new Dictionary<string, object?>
            {
                ["workspace_query_revision"] = 3,
                ["raw_query"] = "do not expose"
            },
            ["create_time"] = 1234
        });

        var snapshot = await store.SnapshotAsync();

        snapshot.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                Key = "Mix:query:abcdef0123456789",
                CacheType = "query",
                WorkspaceQueryRevision = 3L,
                HasChunkId = false,
                CreatedAt = 1234L
            });
    }
}
