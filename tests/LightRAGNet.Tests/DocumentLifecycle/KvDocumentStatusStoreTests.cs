using FluentAssertions;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class KvDocumentStatusStoreTests
{
    private const string LegacyWorkspace = "workspace-a";
    private const string LegacyDocId = "doc-legacy";
    private const string LegacyKey = $"{LegacyWorkspace}:{LegacyDocId}";
    private const string CurrentKey = $"w11:{LegacyWorkspace}d10:{LegacyDocId}";

    [Fact]
    public async Task Roundtrip_WithJsonKvStore_PersistsStatusRecordFields()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        try
        {
            var record = new DocumentStatusRecord
            {
                DocId = "doc-1",
                Workspace = "workspace-a",
                Status = DocumentLifecycleStatus.Processed,
                ContentSummary = "summary",
                ContentLength = 42,
                ChunksCount = 2,
                ChunksList = ["chunk-1", "chunk-2"],
                ChunkSnapshots =
                [
                    new DocumentChunkSnapshot("chunk-1", 10, 0, "doc.md"),
                    new DocumentChunkSnapshot("chunk-2", 8, 1, "doc.md")
                ],
                FilePath = "doc.md",
                TrackId = "track-1",
                ErrorMessage = string.Empty,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = "unit-test",
                    ["validated"] = true
                },
                CreatedAt = new DateTimeOffset(2026, 5, 18, 1, 2, 3, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 5, 18, 4, 5, 6, TimeSpan.Zero)
            };

            var writeKvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            var writeStatusStore = new KvDocumentStatusStore(writeKvStore);

            await writeStatusStore.UpsertAsync(record);

            var readKvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            var readStatusStore = new KvDocumentStatusStore(readKvStore);

            var loaded = await readStatusStore.GetAsync("workspace-a", "doc-1");

            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(DocumentLifecycleStatus.Processed);
            loaded.Workspace.Should().Be("workspace-a");
            loaded.FilePath.Should().Be("doc.md");
            loaded.Metadata.Should().Contain("source", "unit-test");
            loaded.Metadata.Should().Contain("validated", true);
            loaded.ChunksList.Should().Equal("chunk-1", "chunk-2");
            loaded.ChunkSnapshots.Should().Equal(
                new DocumentChunkSnapshot("chunk-1", 10, 0, "doc.md"),
                new DocumentChunkSnapshot("chunk-2", 8, 1, "doc.md"));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task GetAsync_WithLegacyColonDelimitedKey_ReadsMigratesAndDeletesLegacyKey()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        try
        {
            var kvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            await kvStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [LegacyKey] = CreateLegacyRecordDictionary()
            });
            await kvStore.IndexDoneCallbackAsync();
            var statusStore = new KvDocumentStatusStore(kvStore);

            var loaded = await statusStore.GetAsync(LegacyWorkspace, LegacyDocId);

            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(DocumentLifecycleStatus.Processed);
            loaded.Workspace.Should().Be(LegacyWorkspace);
            loaded.DocId.Should().Be(LegacyDocId);
            loaded.FilePath.Should().Be("legacy.md");
            (await kvStore.GetByIdAsync(CurrentKey)).Should().NotBeNull();
            (await kvStore.GetByIdAsync(LegacyKey)).Should().BeNull();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task GetAsync_WithLegacyKeyCollision_DoesNotMigrateMismatchedPayload()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        const string collidingLegacyKey = "a:b:c";
        const string ownerCurrentKey = "w3:a:bd1:c";
        const string wrongLookupCurrentKey = "w1:ad3:b:c";

        try
        {
            var kvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            await kvStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [collidingLegacyKey] = CreateRecordDictionary("a:b", "c", "owner.md")
            });
            await kvStore.IndexDoneCallbackAsync();
            var statusStore = new KvDocumentStatusStore(kvStore);

            var wrongLookup = await statusStore.GetAsync("a", "b:c");

            wrongLookup.Should().BeNull();
            (await kvStore.GetByIdAsync(wrongLookupCurrentKey)).Should().BeNull();
            (await kvStore.GetByIdAsync(collidingLegacyKey)).Should().NotBeNull();

            var ownerLookup = await statusStore.GetAsync("a:b", "c");

            ownerLookup.Should().NotBeNull();
            ownerLookup!.Workspace.Should().Be("a:b");
            ownerLookup.DocId.Should().Be("c");
            ownerLookup.FilePath.Should().Be("owner.md");
            (await kvStore.GetByIdAsync(ownerCurrentKey)).Should().NotBeNull();
            (await kvStore.GetByIdAsync(collidingLegacyKey)).Should().BeNull();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesCurrentAndLegacyKeys()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        try
        {
            var kvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            await kvStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [CurrentKey] = CreateLegacyRecordDictionary(),
                [LegacyKey] = CreateLegacyRecordDictionary()
            });
            await kvStore.IndexDoneCallbackAsync();
            var statusStore = new KvDocumentStatusStore(kvStore);

            await statusStore.DeleteAsync(LegacyWorkspace, LegacyDocId);

            (await kvStore.GetByIdAsync(CurrentKey)).Should().BeNull();
            (await kvStore.GetByIdAsync(LegacyKey)).Should().BeNull();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_WithLegacyKeyCollision_DoesNotDeleteMismatchedPayload()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        const string collidingLegacyKey = "a:b:c";

        try
        {
            var kvStore = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
            await kvStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [collidingLegacyKey] = CreateRecordDictionary("a:b", "c", "owner.md")
            });
            await kvStore.IndexDoneCallbackAsync();
            var statusStore = new KvDocumentStatusStore(kvStore);

            await statusStore.DeleteAsync("a", "b:c");

            (await kvStore.GetByIdAsync(collidingLegacyKey)).Should().NotBeNull();

            await statusStore.DeleteAsync("a:b", "c");

            (await kvStore.GetByIdAsync(collidingLegacyKey)).Should().BeNull();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task Upsert_WithColonDelimitedWorkspaceAndDocId_DoesNotCollide()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "LightRAGNet.Tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        try
        {
            var statusStore = new KvDocumentStatusStore(
                new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance));
            var workspaceWithColon = new DocumentStatusRecord
            {
                DocId = "c",
                Workspace = "a:b",
                Status = DocumentLifecycleStatus.Pending,
                ContentSummary = "workspace contains colon",
                ContentLength = 24,
                FilePath = "workspace-colon.md",
                TrackId = "track-workspace",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var docIdWithColon = new DocumentStatusRecord
            {
                DocId = "b:c",
                Workspace = "a",
                Status = DocumentLifecycleStatus.Failed,
                ContentSummary = "doc id contains colon",
                ContentLength = 21,
                FilePath = "docid-colon.md",
                TrackId = "track-docid",
                ErrorMessage = "separate failure",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await statusStore.UpsertAsync(workspaceWithColon);
            await statusStore.UpsertAsync(docIdWithColon);

            var first = await statusStore.GetAsync("a:b", "c");
            var second = await statusStore.GetAsync("a", "b:c");

            first.Should().NotBeNull();
            first!.Workspace.Should().Be("a:b");
            first.DocId.Should().Be("c");
            first.FilePath.Should().Be("workspace-colon.md");
            first.TrackId.Should().Be("track-workspace");
            second.Should().NotBeNull();
            second!.Workspace.Should().Be("a");
            second.DocId.Should().Be("b:c");
            second.FilePath.Should().Be("docid-colon.md");
            second.TrackId.Should().Be("track-docid");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static Dictionary<string, object> CreateLegacyRecordDictionary()
    {
        return CreateRecordDictionary(LegacyWorkspace, LegacyDocId, "legacy.md");
    }

    private static Dictionary<string, object> CreateRecordDictionary(
        string workspace,
        string docId,
        string filePath)
    {
        return new Dictionary<string, object>
        {
            ["doc_id"] = docId,
            ["workspace"] = workspace,
            ["status"] = "processed",
            ["content_summary"] = "legacy summary",
            ["content_length"] = 14,
            ["chunks_count"] = 1,
            ["chunks_list"] = new List<string> { "chunk-legacy" },
            ["chunk_snapshots"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["chunk_id"] = "chunk-legacy",
                    ["tokens"] = 3,
                    ["chunk_order_index"] = 0,
                    ["file_path"] = filePath
                }
            },
            ["file_path"] = filePath,
            ["track_id"] = "track-legacy",
            ["error_msg"] = string.Empty,
            ["metadata"] = new Dictionary<string, object>
            {
                ["source"] = "legacy"
            },
            ["created_at"] = new DateTimeOffset(2026, 5, 17, 1, 2, 3, TimeSpan.Zero).ToString("O"),
            ["updated_at"] = new DateTimeOffset(2026, 5, 17, 4, 5, 6, TimeSpan.Zero).ToString("O")
        };
    }
}
