using FluentAssertions;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class KvDocumentStatusStoreTests
{
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
}
