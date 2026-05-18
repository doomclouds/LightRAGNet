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
}
