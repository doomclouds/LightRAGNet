using FluentAssertions;
using LightRAGNet.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.Storage;

public sealed class JsonKVStoreConcurrencyTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsSnapshot_NotInternalDictionary()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "original"
            }
        });

        var snapshot = await store.GetByIdAsync("item-1");
        snapshot.Should().NotBeNull();
        snapshot!["name"] = "mutated";

        var reloaded = await store.GetByIdAsync("item-1");

        reloaded.Should().NotBeNull();
        reloaded!["name"].Should().Be("original");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNestedSnapshot_NotInternalList()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["tags"] = new List<object> { "alpha", "beta" }
            }
        });

        var snapshot = await store.GetByIdAsync("item-1");
        snapshot.Should().NotBeNull();
        var tags = snapshot!["tags"].Should().BeAssignableTo<List<object>>().Subject;
        tags.Add("mutated");

        var reloaded = await store.GetByIdAsync("item-1");
        reloaded.Should().NotBeNull();
        var reloadedTags = reloaded!["tags"]
            .Should().BeAssignableTo<List<object>>().Subject;

        reloadedTags.Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNestedSnapshot_NotInternalStringList()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        var originalTags = new List<string> { "alpha", "beta" };
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["tags"] = originalTags
            }
        });

        var snapshot = await store.GetByIdAsync("item-1");
        snapshot.Should().NotBeNull();
        var tags = snapshot!["tags"].Should().BeOfType<List<string>>().Subject;
        tags.Should().NotBeSameAs(originalTags);
        tags.Add("mutated");

        var reloaded = await store.GetByIdAsync("item-1");
        reloaded.Should().NotBeNull();
        var reloadedTags = reloaded!["tags"].Should().BeOfType<List<string>>().Subject;

        reloadedTags.Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNestedSnapshot_NotInternalStringArray()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        var originalTags = new[] { "alpha", "beta" };
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["tags"] = originalTags
            }
        });

        var snapshot = await store.GetByIdAsync("item-1");
        snapshot.Should().NotBeNull();
        var tags = snapshot!["tags"].Should().BeOfType<string[]>().Subject;
        tags.Should().NotBeSameAs(originalTags);
        tags[0] = "mutated";

        var reloaded = await store.GetByIdAsync("item-1");
        reloaded.Should().NotBeNull();
        var reloadedTags = reloaded!["tags"].Should().BeOfType<string[]>().Subject;

        reloadedTags.Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNestedSnapshot_NotInternalRelationPairs()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        var originalPairs = new List<string[]>
        {
            new[] { "source", "target" },
            new[] { "left", "right" }
        };
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["pairs"] = originalPairs
            }
        });

        var snapshot = await store.GetByIdAsync("item-1");
        snapshot.Should().NotBeNull();
        var pairs = snapshot!["pairs"].Should().BeOfType<List<string[]>>().Subject;
        pairs.Should().NotBeSameAs(originalPairs);
        pairs[0].Should().NotBeSameAs(originalPairs[0]);
        pairs[0][0] = "mutated";

        var reloaded = await store.GetByIdAsync("item-1");
        reloaded.Should().NotBeNull();
        var reloadedPairs = reloaded!["pairs"].Should().BeOfType<List<string[]>>().Subject;

        reloadedPairs[0].Should().Equal("source", "target");
        reloadedPairs[1].Should().Equal("left", "right");
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsSnapshots_NotInternalDictionaries()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "first"
            },
            ["item-2"] = new(StringComparer.Ordinal)
            {
                ["name"] = "second"
            }
        });

        var snapshots = await store.GetByIdsAsync(["item-1", "item-2"]);
        snapshots.Should().HaveCount(2);
        snapshots[0]["name"] = "mutated";

        var reloaded = await store.GetByIdAsync("item-1");

        reloaded.Should().NotBeNull();
        reloaded!["name"].Should().Be("first");
    }

    [Fact]
    public async Task ConcurrentReadWriteDelete_DoesNotThrow()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        var exceptions = new List<Exception>();
        var exceptionsLock = new object();

        var workers = Enumerable.Range(0, 30)
            .Select(worker => Task.Run(async () =>
            {
                try
                {
                    for (var iteration = 0; iteration < 100; iteration++)
                    {
                        var key = $"item-{iteration % 20}";
                        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
                        {
                            [key] = new(StringComparer.Ordinal)
                            {
                                ["worker"] = worker,
                                ["iteration"] = iteration,
                                ["tags"] = new List<object> { worker, iteration }
                            }
                        });

                        _ = await store.GetByIdAsync(key);
                        _ = await store.GetByIdsAsync([key, $"item-{(iteration + 1) % 20}"]);
                        _ = await store.FilterKeysAsync([$"item-{iteration % 20}", $"missing-{worker}"]);
                        _ = await store.IsEmptyAsync();

                        if (iteration % 3 == 0)
                        {
                            await store.DeleteAsync([$"item-{(iteration + worker) % 20}"]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptionsLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(workers);

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_WhenCallerMutatesInputAfterUpsert_DoesNotMutateStore()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        var tags = new List<string> { "alpha", "beta" };
        var record = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = "original",
            ["tags"] = tags
        };
        var input = new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = record
        };

        await store.UpsertAsync(input);
        record["name"] = "mutated";
        tags.Add("mutated");

        var reloaded = await store.GetByIdAsync("item-1");

        reloaded.Should().NotBeNull();
        reloaded!["name"].Should().Be("original");
        reloaded["tags"].Should().BeOfType<List<string>>().Subject
            .Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task SnapshotAsync_ReturnsSafeInspectableDescriptorsOnly()
    {
        using var tempDirectory = new TempDirectory();
        var store = CreateStore(tempDirectory);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["Mix:query:abcdef0123456789"] = new(StringComparer.Ordinal)
            {
                ["return"] = "secret return provider payload",
                ["cache_type"] = "query",
                ["original_prompt"] = "secret prompt api_key authorization",
                ["queryparam"] = new Dictionary<string, object?>
                {
                    ["workspace"] = "workspace-a",
                    ["workspace_query_revision"] = 3,
                    ["raw_query"] = "do not expose"
                },
                ["create_time"] = 1234
            }
        });

        var snapshot = await store.SnapshotAsync();

        snapshot.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                Key = "Mix:query:abcdef0123456789",
                CacheType = "query",
                Workspace = "workspace-a",
                WorkspaceQueryRevision = 3L,
                HasChunkId = false,
                CreatedAt = 1234L
            });
    }

    [Fact]
    public async Task IndexDoneCallbackAsync_PersistsThroughAtomicWriter()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = tempDirectory.GetPath("store.json");
        var store = CreateStore(filePath);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "persisted"
            }
        });

        await store.IndexDoneCallbackAsync();

        File.Exists(filePath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(filePath);
        json.Should().Contain("persisted");
        Directory.EnumerateFiles(tempDirectory.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDoneCallbackAsync_PersistsChineseTextWithoutUnicodeEscapes()
    {
        using var tempDirectory = new TempDirectory();
        var filePath = tempDirectory.GetPath("store.json");
        var store = CreateStore(filePath);
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "线性修正业务说明.md",
                ["keywords"] = new List<string> { "采集流程", "100字" }
            }
        });

        await store.IndexDoneCallbackAsync();

        var json = await File.ReadAllTextAsync(filePath);
        json.Should().Contain("线性修正业务说明.md");
        json.Should().Contain("采集流程");
        json.Should().Contain("100字");
        json.Should().NotContain("\\u7EBF");
        json.Should().NotContain("\\u91C7");
    }

    [Fact]
    public async Task IndexDoneCallbackAsync_WhenPersistenceFails_ThrowsToCaller()
    {
        using var tempDirectory = new TempDirectory();
        var blockedDirectoryPath = tempDirectory.GetPath("blocked");
        await File.WriteAllTextAsync(blockedDirectoryPath, "not a directory");
        var store = CreateStore(Path.Combine(blockedDirectoryPath, "store.json"));
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "cannot-save"
            }
        });

        Func<Task> act = async () => await store.IndexDoneCallbackAsync();

        await act.Should().ThrowAsync<Exception>()
            .Where(ex => typeof(IOException).IsAssignableFrom(ex.GetType())
                         || typeof(UnauthorizedAccessException).IsAssignableFrom(ex.GetType()));
    }

    [Fact]
    public async Task DropAsync_WhenPersistenceFails_RestoresInMemoryData()
    {
        using var tempDirectory = new TempDirectory();
        var blockedDirectoryPath = tempDirectory.GetPath("blocked");
        var store = CreateStore(Path.Combine(blockedDirectoryPath, "store.json"));
        await store.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            ["item-1"] = new(StringComparer.Ordinal)
            {
                ["name"] = "survives-drop-failure",
                ["tags"] = new List<string> { "alpha", "beta" }
            }
        });
        await File.WriteAllTextAsync(blockedDirectoryPath, "not a directory");

        Func<Task> act = async () => await store.DropAsync();

        await act.Should().ThrowAsync<Exception>()
            .Where(ex => typeof(IOException).IsAssignableFrom(ex.GetType())
                         || typeof(UnauthorizedAccessException).IsAssignableFrom(ex.GetType()));
        var reloaded = await store.GetByIdAsync("item-1");
        reloaded.Should().NotBeNull();
        reloaded!["name"].Should().Be("survives-drop-failure");
        reloaded["tags"].Should().BeOfType<List<string>>().Subject
            .Should().Equal("alpha", "beta");
    }

    private static JsonKVStore CreateStore(TempDirectory tempDirectory)
    {
        return CreateStore(tempDirectory.GetPath("store.json"));
    }

    private static JsonKVStore CreateStore(string filePath)
    {
        return new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"lightragnet-json-kv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string GetPath(string fileName)
        {
            return System.IO.Path.Combine(Path, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
