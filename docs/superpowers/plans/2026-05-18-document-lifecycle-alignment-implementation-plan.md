# Document Lifecycle Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a testable document lifecycle core that aligns LightRAGNet with Python LightRAG's indexing status, chunk snapshot, deletion contract, and workspace isolation semantics.

**Architecture:** Introduce `Services/DocumentLifecycle` in the core `LightRAGNet` project. Lifecycle rules live in `DocumentLifecycleService`; persistence is abstracted behind `IDocumentStatusStore`; production uses a keyed `IKVStore` adapter named `doc_status`; tests use an in-memory fake. `LightRAG.InsertAsync` remains the orchestrator but delegates status transitions and duplicate checks to the lifecycle service.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NSubstitute, existing `IKVStore`, existing `LightRAGOptions.Workspace`, existing fake tokenizer and test doubles.

---

## File Structure

Create:

- `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleStatus.cs`: lifecycle enum and wire-format helpers.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentChunkSnapshot.cs`: chunk snapshot captured after chunking.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentStatusRecord.cs`: Python `doc_status`-style record.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentIngestionResult.cs`: result from prepare-ingestion.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionPlan.cs`: deletion plan contract.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionResult.cs`: deletion operation contract.
- `src/LightRAGNet/Services/DocumentLifecycle/IDocumentStatusStore.cs`: workspace-scoped store abstraction.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`: lifecycle state machine.
- `src/LightRAGNet/Services/DocumentLifecycle/KvDocumentStatusStore.cs`: production adapter over keyed `IKVStore`.
- `tests/LightRAGNet.Tests/TestDoubles/InMemoryDocumentStatusStore.cs`: fast fake for unit tests.
- `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`: lifecycle TDD tests.

Modify:

- `src/LightRAGNet.Storage/KVContracts.cs`: add `DocStatus`.
- `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`: register lifecycle service and status store.
- `src/LightRAGNet/LightRAG.cs`: inject lifecycle service and use it in `InsertAsync`.
- `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`: no behavior change expected; run as regression guard.

---

### Task 1: Add Lifecycle Types And Store Contract

**Files:**

- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleStatus.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentChunkSnapshot.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentStatusRecord.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentIngestionResult.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionPlan.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionResult.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/IDocumentStatusStore.cs`
- Test: `dotnet build .\LightRAGNet.slnx`

- [ ] **Step 1: Create the lifecycle status enum**

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleStatus.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public enum DocumentLifecycleStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    Deleting,
    Deleted,
    DeletionFailed
}

public static class DocumentLifecycleStatusExtensions
{
    public static string ToWireValue(this DocumentLifecycleStatus status)
    {
        return status switch
        {
            DocumentLifecycleStatus.Pending => "pending",
            DocumentLifecycleStatus.Processing => "processing",
            DocumentLifecycleStatus.Processed => "processed",
            DocumentLifecycleStatus.Failed => "failed",
            DocumentLifecycleStatus.Deleting => "deleting",
            DocumentLifecycleStatus.Deleted => "deleted",
            DocumentLifecycleStatus.DeletionFailed => "deletion_failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown lifecycle status.")
        };
    }

    public static DocumentLifecycleStatus FromWireValue(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "pending" => DocumentLifecycleStatus.Pending,
            "processing" => DocumentLifecycleStatus.Processing,
            "processed" => DocumentLifecycleStatus.Processed,
            "failed" => DocumentLifecycleStatus.Failed,
            "deleting" => DocumentLifecycleStatus.Deleting,
            "deleted" => DocumentLifecycleStatus.Deleted,
            "deletion_failed" => DocumentLifecycleStatus.DeletionFailed,
            _ => DocumentLifecycleStatus.Pending
        };
    }
}
```

- [ ] **Step 2: Create value models**

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentChunkSnapshot.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentChunkSnapshot(
    string ChunkId,
    int Tokens,
    int ChunkOrderIndex,
    string FilePath);
```

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentStatusRecord.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentStatusRecord
{
    public required string DocId { get; init; }
    public required string Workspace { get; init; }
    public DocumentLifecycleStatus Status { get; set; }
    public string ContentSummary { get; set; } = string.Empty;
    public int ContentLength { get; set; }
    public int ChunksCount { get; set; }
    public List<string> ChunksList { get; set; } = [];
    public List<DocumentChunkSnapshot> ChunkSnapshots { get; set; } = [];
    public string FilePath { get; set; } = "unknown_source";
    public string TrackId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentIngestionResult.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentIngestionResult(
    string DocId,
    string Workspace,
    bool IsDuplicate,
    DocumentStatusRecord StatusRecord);
```

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionPlan.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentDeletionPlan
{
    public required string DocId { get; init; }
    public required string Workspace { get; init; }
    public bool Found { get; init; }
    public IReadOnlyList<string> ChunkIds { get; init; } = [];
    public IReadOnlyList<DocumentChunkSnapshot> ChunkSnapshots { get; init; } = [];
    public bool DeleteFullDocument { get; init; }
    public bool DeleteTextChunks { get; init; }
    public bool DeleteChunkVectors { get; init; }
    public bool DeleteDocumentGraphMetadata { get; init; }
    public bool DeleteLlmCache { get; init; }
}
```

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentDeletionResult.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentDeletionResult(
    string DocId,
    string Workspace,
    bool Found,
    bool Succeeded,
    string Stage,
    string Message);
```

- [ ] **Step 3: Create the store interface**

Create `src/LightRAGNet/Services/DocumentLifecycle/IDocumentStatusStore.cs`:

```csharp
namespace LightRAGNet.Services.DocumentLifecycle;

public interface IDocumentStatusStore
{
    Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        DocumentStatusRecord record,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentStatusRecord>> GetByStatusAsync(
        string workspace,
        DocumentLifecycleStatus status,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Build to verify the new types compile**

Run:

```powershell
dotnet build .\LightRAGNet.slnx
```

Expected: build succeeds with existing warnings only.

- [ ] **Step 5: Commit Task 1**

```powershell
git add src/LightRAGNet/Services/DocumentLifecycle
git commit -m "feat: add document lifecycle contracts"
```

---

### Task 2: Implement Lifecycle Service With Failing Tests First

**Files:**

- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryDocumentStatusStore.cs`
- Create: `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`

- [ ] **Step 1: Add the in-memory fake store**

Create `tests/LightRAGNet.Tests/TestDoubles/InMemoryDocumentStatusStore.cs`:

```csharp
using LightRAGNet.Services.DocumentLifecycle;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class InMemoryDocumentStatusStore : IDocumentStatusStore
{
    private readonly Dictionary<(string Workspace, string DocId), DocumentStatusRecord> records = [];

    public Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        records.TryGetValue((workspace, docId), out var record);
        return Task.FromResult(Clone(record));
    }

    public Task UpsertAsync(DocumentStatusRecord record, CancellationToken cancellationToken = default)
    {
        records[(record.Workspace, record.DocId)] = Clone(record)!;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string workspace, string docId, CancellationToken cancellationToken = default)
    {
        records.Remove((workspace, docId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentStatusRecord>> GetByStatusAsync(
        string workspace,
        DocumentLifecycleStatus status,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DocumentStatusRecord> result = records.Values
            .Where(record => record.Workspace == workspace && record.Status == status)
            .Select(record => Clone(record)!)
            .ToList();

        return Task.FromResult(result);
    }

    private static DocumentStatusRecord? Clone(DocumentStatusRecord? record)
    {
        if (record is null)
        {
            return null;
        }

        return new DocumentStatusRecord
        {
            DocId = record.DocId,
            Workspace = record.Workspace,
            Status = record.Status,
            ContentSummary = record.ContentSummary,
            ContentLength = record.ContentLength,
            ChunksCount = record.ChunksCount,
            ChunksList = [.. record.ChunksList],
            ChunkSnapshots = [.. record.ChunkSnapshots],
            FilePath = record.FilePath,
            TrackId = record.TrackId,
            ErrorMessage = record.ErrorMessage,
            Metadata = new Dictionary<string, object>(record.Metadata),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }
}
```

- [ ] **Step 2: Write failing lifecycle tests**

Create `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class DocumentLifecycleServiceTests
{
    [Fact]
    public async Task CreatePending_NewDocument_WritesPendingStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);

        var result = await service.PrepareIngestionAsync(
            "alpha beta gamma",
            docId: "doc-1",
            filePath: "alpha.md",
            trackId: "track-1");

        result.IsDuplicate.Should().BeFalse();
        result.DocId.Should().Be("doc-1");
        result.Workspace.Should().Be("_");

        var saved = await store.GetAsync("_", "doc-1");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(DocumentLifecycleStatus.Pending);
        saved.FilePath.Should().Be("alpha.md");
        saved.TrackId.Should().Be("track-1");
        saved.ContentLength.Should().Be("alpha beta gamma".Length);
        saved.ContentSummary.Should().Be("alpha beta gamma");
        saved.ChunksList.Should().BeEmpty();
    }

    [Fact]
    public async Task StartProcessing_PendingDocument_MarksProcessing()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");

        await service.StartProcessingAsync("_", "doc-1");

        var saved = await store.GetAsync("_", "doc-1");
        saved!.Status.Should().Be(DocumentLifecycleStatus.Processing);
    }

    [Fact]
    public async Task RecordChunks_AfterChunking_PreservesChunkSnapshot()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");

        await service.RecordChunksAsync("_", "doc-1", [
            new Chunk { Id = "chunk-1", Tokens = 3, ChunkOrderIndex = 0, FilePath = "file.md", Content = "aaa", FullDocId = "doc-1" },
            new Chunk { Id = "chunk-2", Tokens = 2, ChunkOrderIndex = 1, FilePath = "file.md", Content = "bbb", FullDocId = "doc-1" }
        ]);

        var saved = await store.GetAsync("_", "doc-1");
        saved!.ChunksCount.Should().Be(2);
        saved.ChunksList.Should().Equal("chunk-1", "chunk-2");
        saved.ChunkSnapshots.Select(snapshot => snapshot.ChunkId).Should().Equal("chunk-1", "chunk-2");
    }

    [Fact]
    public async Task FailAfterChunking_ProcessingDocument_PreservesChunksAndError()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");
        await service.RecordChunksAsync("_", "doc-1", [
            new Chunk { Id = "chunk-1", Tokens = 3, ChunkOrderIndex = 0, FilePath = "file.md", Content = "aaa", FullDocId = "doc-1" }
        ]);

        await service.MarkFailedAsync("_", "doc-1", "extract_entities", "extract failed");

        var saved = await store.GetAsync("_", "doc-1");
        saved!.Status.Should().Be(DocumentLifecycleStatus.Failed);
        saved.ErrorMessage.Should().Be("extract failed");
        saved.ChunksList.Should().Equal("chunk-1");
        saved.Metadata["failure_stage"].Should().Be("extract_entities");
    }

    [Fact]
    public async Task FailBeforeChunking_ExistingFailedDocument_PreservesPreviousChunkSnapshot()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await store.UpsertAsync(new DocumentStatusRecord
        {
            DocId = "doc-1",
            Workspace = "_",
            Status = DocumentLifecycleStatus.Failed,
            ContentSummary = "old",
            ContentLength = 3,
            ChunksCount = 2,
            ChunksList = ["old-1", "old-2"],
            ChunkSnapshots =
            [
                new DocumentChunkSnapshot("old-1", 1, 0, "old.md"),
                new DocumentChunkSnapshot("old-2", 1, 1, "old.md")
            ],
            FilePath = "old.md",
            TrackId = "track-old",
            ErrorMessage = "old failure",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await service.MarkFailedAsync("_", "doc-1", "chunking", "new chunking failed");

        var saved = await store.GetAsync("_", "doc-1");
        saved!.ChunksList.Should().Equal("old-1", "old-2");
        saved.ChunksCount.Should().Be(2);
        saved.ErrorMessage.Should().Be("new chunking failed");
        saved.Metadata["failure_stage"].Should().Be("chunking");
    }

    [Fact]
    public async Task MarkProcessed_ProcessingDocument_WritesProcessedStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");

        await service.MarkProcessedAsync("_", "doc-1");

        var saved = await store.GetAsync("_", "doc-1");
        saved!.Status.Should().Be(DocumentLifecycleStatus.Processed);
        saved.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateDocument_SameWorkspace_ReturnsExistingStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");

        var duplicate = await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-2");

        duplicate.IsDuplicate.Should().BeTrue();
        duplicate.StatusRecord.TrackId.Should().Be("track-1");
    }

    [Fact]
    public async Task DuplicateDocument_DifferentWorkspace_AllowsSeparateStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var serviceA = CreateService(store, workspace: "a");
        var serviceB = CreateService(store, workspace: "b");

        await serviceA.PrepareIngestionAsync("content", "doc-1", "a.md", "track-a");
        var second = await serviceB.PrepareIngestionAsync("content", "doc-1", "b.md", "track-b");

        second.IsDuplicate.Should().BeFalse();
        (await store.GetAsync("a", "doc-1"))!.FilePath.Should().Be("a.md");
        (await store.GetAsync("b", "doc-1"))!.FilePath.Should().Be("b.md");
    }

    [Fact]
    public async Task CreateDeletionPlan_ProcessedDocument_IncludesKnownChunkIds()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");
        await service.RecordChunksAsync("_", "doc-1", [
            new Chunk { Id = "chunk-1", Tokens = 3, ChunkOrderIndex = 0, FilePath = "file.md", Content = "aaa", FullDocId = "doc-1" }
        ]);
        await service.MarkProcessedAsync("_", "doc-1");

        var plan = await service.CreateDeletionPlanAsync("_", "doc-1", deleteLlmCache: true);

        plan.Found.Should().BeTrue();
        plan.ChunkIds.Should().Equal("chunk-1");
        plan.DeleteFullDocument.Should().BeTrue();
        plan.DeleteTextChunks.Should().BeTrue();
        plan.DeleteChunkVectors.Should().BeTrue();
        plan.DeleteDocumentGraphMetadata.Should().BeTrue();
        plan.DeleteLlmCache.Should().BeTrue();
    }

    [Fact]
    public async Task MarkDeletionFailed_WhenStageFails_RecordsRetryMetadata()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");
        await service.MarkProcessedAsync("_", "doc-1");

        var result = await service.MarkDeletionFailedAsync("_", "doc-1", "delete_chunk_vectors", "qdrant failed");

        result.Succeeded.Should().BeFalse();
        result.Stage.Should().Be("delete_chunk_vectors");
        var saved = await store.GetAsync("_", "doc-1");
        saved!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        saved.Metadata["deletion_failed"].Should().Be(true);
        saved.Metadata["deletion_failure_stage"].Should().Be("delete_chunk_vectors");
    }

    [Fact]
    public async Task CreateDeletionPlan_AfterDeletionFailure_UsesPreservedChunkSnapshot()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
        await service.StartProcessingAsync("_", "doc-1");
        await service.RecordChunksAsync("_", "doc-1", [
            new Chunk { Id = "chunk-1", Tokens = 3, ChunkOrderIndex = 0, FilePath = "file.md", Content = "aaa", FullDocId = "doc-1" }
        ]);
        await service.MarkDeletionFailedAsync("_", "doc-1", "delete_text_chunks", "disk failed");

        var plan = await service.CreateDeletionPlanAsync("_", "doc-1");

        plan.Found.Should().BeTrue();
        plan.ChunkIds.Should().Equal("chunk-1");
    }

    [Fact]
    public async Task CreateDeletionPlan_UnknownDocument_ReturnsNotFound()
    {
        var service = CreateService(new InMemoryDocumentStatusStore());

        var plan = await service.CreateDeletionPlanAsync("_", "missing");

        plan.Found.Should().BeFalse();
        plan.ChunkIds.Should().BeEmpty();
    }

    private static DocumentLifecycleService CreateService(
        IDocumentStatusStore store,
        string workspace = "_")
    {
        return new DocumentLifecycleService(
            store,
            Options.Create(new LightRAGOptions { Workspace = workspace }),
            NullLogger<DocumentLifecycleService>.Instance);
    }
}
```

- [ ] **Step 3: Run the new tests and verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentLifecycleServiceTests
```

Expected: build or test failure because `DocumentLifecycleService` does not exist.

- [ ] **Step 4: Implement the lifecycle service**

Create `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`:

```csharp
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentLifecycleService(
    IDocumentStatusStore statusStore,
    IOptions<LightRAGOptions> options,
    ILogger<DocumentLifecycleService> logger)
{
    private readonly LightRAGOptions options = options.Value;

    public async Task<DocumentIngestionResult> PrepareIngestionAsync(
        string content,
        string? docId = null,
        string? filePath = null,
        string? trackId = null,
        CancellationToken cancellationToken = default)
    {
        var workspace = NormalizeWorkspace(options.Workspace);
        var resolvedDocId = string.IsNullOrWhiteSpace(docId)
            ? HashUtils.ComputeMd5Hash(content, "doc-")
            : docId;

        var existing = await statusStore.GetAsync(workspace, resolvedDocId, cancellationToken);
        if (existing is not null)
        {
            return new DocumentIngestionResult(resolvedDocId, workspace, true, existing);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new DocumentStatusRecord
        {
            DocId = resolvedDocId,
            Workspace = workspace,
            Status = DocumentLifecycleStatus.Pending,
            ContentSummary = CreateSummary(content),
            ContentLength = content.Length,
            FilePath = NormalizeFilePath(filePath),
            TrackId = string.IsNullOrWhiteSpace(trackId) ? $"track-{resolvedDocId}" : trackId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await statusStore.UpsertAsync(record, cancellationToken);
        return new DocumentIngestionResult(resolvedDocId, workspace, false, record);
    }

    public Task StartProcessingAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(workspace, docId, record =>
        {
            record.Status = DocumentLifecycleStatus.Processing;
            record.ErrorMessage = string.Empty;
            record.Metadata.Remove("failure_stage");
        }, cancellationToken);
    }

    public Task RecordChunksAsync(
        string workspace,
        string docId,
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(workspace, docId, record =>
        {
            record.ChunksList = chunks.Select(chunk => chunk.Id).ToList();
            record.ChunksCount = chunks.Count;
            record.ChunkSnapshots = chunks
                .Select(chunk => new DocumentChunkSnapshot(
                    chunk.Id,
                    chunk.Tokens,
                    chunk.ChunkOrderIndex,
                    NormalizeFilePath(chunk.FilePath)))
                .ToList();
        }, cancellationToken);
    }

    public Task MarkProcessedAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(workspace, docId, record =>
        {
            record.Status = DocumentLifecycleStatus.Processed;
            record.ErrorMessage = string.Empty;
            record.Metadata.Remove("failure_stage");
        }, cancellationToken);
    }

    public Task MarkFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(workspace, docId, record =>
        {
            record.Status = DocumentLifecycleStatus.Failed;
            record.ErrorMessage = errorMessage;
            record.Metadata["failure_stage"] = stage;
        }, cancellationToken);
    }

    public async Task<DocumentDeletionPlan> CreateDeletionPlanAsync(
        string workspace,
        string docId,
        bool deleteLlmCache = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionPlan
            {
                DocId = docId,
                Workspace = normalizedWorkspace,
                Found = false
            };
        }

        return new DocumentDeletionPlan
        {
            DocId = docId,
            Workspace = normalizedWorkspace,
            Found = true,
            ChunkIds = [.. record.ChunksList],
            ChunkSnapshots = [.. record.ChunkSnapshots],
            DeleteFullDocument = true,
            DeleteTextChunks = record.ChunksList.Count > 0,
            DeleteChunkVectors = record.ChunksList.Count > 0,
            DeleteDocumentGraphMetadata = true,
            DeleteLlmCache = deleteLlmCache
        };
    }

    public async Task<DocumentDeletionResult> MarkDeletionFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionResult(docId, normalizedWorkspace, false, false, stage, "Document status not found.");
        }

        record.Status = DocumentLifecycleStatus.DeletionFailed;
        record.ErrorMessage = errorMessage;
        record.Metadata["deletion_failed"] = true;
        record.Metadata["deletion_failure_stage"] = stage;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await statusStore.UpsertAsync(record, cancellationToken);

        return new DocumentDeletionResult(docId, normalizedWorkspace, true, false, stage, errorMessage);
    }

    private async Task MutateAsync(
        string workspace,
        string docId,
        Action<DocumentStatusRecord> mutation,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            logger.LogWarning("Document lifecycle status not found: {Workspace}/{DocId}", normalizedWorkspace, docId);
            return;
        }

        mutation(record);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await statusStore.UpsertAsync(record, cancellationToken);
    }

    private static string NormalizeWorkspace(string? workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }

    private static string NormalizeFilePath(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath) ? "unknown_source" : filePath;
    }

    private static string CreateSummary(string content)
    {
        var trimmed = content.Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..120];
    }
}
```

- [ ] **Step 5: Run lifecycle tests and verify they pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentLifecycleServiceTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests
git commit -m "test: cover document lifecycle state machine"
```

---

### Task 3: Add Production KV Adapter And Dependency Injection

**Files:**

- Modify: `src/LightRAGNet.Storage/KVContracts.cs`
- Create: `src/LightRAGNet/Services/DocumentLifecycle/KvDocumentStatusStore.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`

- [ ] **Step 1: Add failing adapter tests**

Append these tests to `DocumentLifecycleServiceTests.cs`:

```csharp
[Fact]
public void DocumentLifecycleStatus_ToWireValue_UsesPythonStyleValues()
{
    DocumentLifecycleStatus.Pending.ToWireValue().Should().Be("pending");
    DocumentLifecycleStatus.Processing.ToWireValue().Should().Be("processing");
    DocumentLifecycleStatus.Processed.ToWireValue().Should().Be("processed");
    DocumentLifecycleStatus.Failed.ToWireValue().Should().Be("failed");
    DocumentLifecycleStatus.DeletionFailed.ToWireValue().Should().Be("deletion_failed");
}

[Fact]
public void DocumentLifecycleStatus_FromWireValue_ParsesPythonStyleValues()
{
    DocumentLifecycleStatusExtensions.FromWireValue("pending").Should().Be(DocumentLifecycleStatus.Pending);
    DocumentLifecycleStatusExtensions.FromWireValue("processing").Should().Be(DocumentLifecycleStatus.Processing);
    DocumentLifecycleStatusExtensions.FromWireValue("processed").Should().Be(DocumentLifecycleStatus.Processed);
    DocumentLifecycleStatusExtensions.FromWireValue("failed").Should().Be(DocumentLifecycleStatus.Failed);
    DocumentLifecycleStatusExtensions.FromWireValue("deletion_failed").Should().Be(DocumentLifecycleStatus.DeletionFailed);
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentLifecycleStatus"
```

Expected: PASS if Task 1 was implemented exactly.

- [ ] **Step 2: Add `DocStatus` to KV contracts**

Modify `src/LightRAGNet.Storage/KVContracts.cs`:

```csharp
namespace LightRAGNet.Storage;

public static class KVContracts
{
    public const string TextChunks = "text_chunks";

    public const string FullDocs = "full_docs";

    public const string FullEntities = "full_entities";

    public const string FullRelations = "full_relations";

    public const string EntityChunks = "entity_chunks";

    public const string RelationChunks = "relation_chunks";

    public const string LLMCache = "llm_cache";

    public const string DocStatus = "doc_status";

    public static IEnumerable<string> GetKVStoreNames()
    {
        yield return TextChunks;
        yield return FullDocs;
        yield return FullEntities;
        yield return FullRelations;
        yield return EntityChunks;
        yield return RelationChunks;
        yield return LLMCache;
        yield return DocStatus;
    }
}
```

- [ ] **Step 3: Implement the KV adapter**

Create `src/LightRAGNet/Services/DocumentLifecycle/KvDocumentStatusStore.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class KvDocumentStatusStore(
    [FromKeyedServices(KVContracts.DocStatus)] IKVStore store) : IDocumentStatusStore
{
    public async Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var data = await store.GetByIdAsync(MakeKey(workspace, docId), cancellationToken);
        return data is null ? null : FromDictionary(data);
    }

    public Task UpsertAsync(DocumentStatusRecord record, CancellationToken cancellationToken = default)
    {
        return store.UpsertAsync(
            new Dictionary<string, Dictionary<string, object>>
            {
                [MakeKey(record.Workspace, record.DocId)] = ToDictionary(record)
            },
            cancellationToken);
    }

    public Task DeleteAsync(string workspace, string docId, CancellationToken cancellationToken = default)
    {
        return store.DeleteAsync([MakeKey(workspace, docId)], cancellationToken);
    }

    public Task<IReadOnlyList<DocumentStatusRecord>> GetByStatusAsync(
        string workspace,
        DocumentLifecycleStatus status,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DocumentStatusRecord>>([]);
    }

    private static string MakeKey(string workspace, string docId)
    {
        return $"{NormalizeWorkspace(workspace)}:{docId}";
    }

    private static string NormalizeWorkspace(string workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }

    private static Dictionary<string, object> ToDictionary(DocumentStatusRecord record)
    {
        return new Dictionary<string, object>
        {
            ["doc_id"] = record.DocId,
            ["workspace"] = record.Workspace,
            ["status"] = record.Status.ToWireValue(),
            ["content_summary"] = record.ContentSummary,
            ["content_length"] = record.ContentLength,
            ["chunks_count"] = record.ChunksCount,
            ["chunks_list"] = record.ChunksList,
            ["chunk_snapshots"] = record.ChunkSnapshots.Select(snapshot => new Dictionary<string, object>
            {
                ["chunk_id"] = snapshot.ChunkId,
                ["tokens"] = snapshot.Tokens,
                ["chunk_order_index"] = snapshot.ChunkOrderIndex,
                ["file_path"] = snapshot.FilePath
            }).ToList(),
            ["file_path"] = record.FilePath,
            ["track_id"] = record.TrackId,
            ["error_msg"] = record.ErrorMessage,
            ["metadata"] = record.Metadata,
            ["created_at"] = record.CreatedAt.ToString("O"),
            ["updated_at"] = record.UpdatedAt.ToString("O")
        };
    }

    private static DocumentStatusRecord FromDictionary(Dictionary<string, object> data)
    {
        return new DocumentStatusRecord
        {
            DocId = GetString(data, "doc_id"),
            Workspace = GetString(data, "workspace", "_"),
            Status = DocumentLifecycleStatusExtensions.FromWireValue(GetString(data, "status")),
            ContentSummary = GetString(data, "content_summary"),
            ContentLength = GetInt(data, "content_length"),
            ChunksCount = GetInt(data, "chunks_count"),
            ChunksList = GetStringList(data, "chunks_list"),
            ChunkSnapshots = GetChunkSnapshots(data, "chunk_snapshots"),
            FilePath = GetString(data, "file_path", "unknown_source"),
            TrackId = GetString(data, "track_id"),
            ErrorMessage = GetString(data, "error_msg"),
            Metadata = GetObjectDictionary(data, "metadata"),
            CreatedAt = GetDateTimeOffset(data, "created_at"),
            UpdatedAt = GetDateTimeOffset(data, "updated_at")
        };
    }

    private static string GetString(Dictionary<string, object> data, string key, string defaultValue = "")
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? defaultValue,
            _ => value.ToString() ?? defaultValue
        };
    }

    private static int GetInt(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Number => json.GetInt32(),
            int number => number,
            long number => (int)number,
            _ => Convert.ToInt32(value)
        };
    }

    private static DateTimeOffset GetDateTimeOffset(Dictionary<string, object> data, string key)
    {
        var value = GetString(data, key);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }

    private static List<string> GetStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            return json.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToList();
        }

        if (value is IEnumerable<object> objects)
        {
            return objects.Select(item => item.ToString() ?? string.Empty).Where(item => item.Length > 0).ToList();
        }

        return [];
    }

    private static List<DocumentChunkSnapshot> GetChunkSnapshots(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            return json.EnumerateArray()
                .Select(item => new DocumentChunkSnapshot(
                    item.GetProperty("chunk_id").GetString() ?? string.Empty,
                    item.GetProperty("tokens").GetInt32(),
                    item.GetProperty("chunk_order_index").GetInt32(),
                    item.GetProperty("file_path").GetString() ?? "unknown_source"))
                .ToList();
        }

        return [];
    }

    private static Dictionary<string, object> GetObjectDictionary(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            return json.EnumerateObject().ToDictionary(property => property.Name, property => (object)ReadJsonValue(property.Value));
        }

        return value as Dictionary<string, object> ?? [];
    }

    private static object ReadJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.ToString()
        };
    }
}
```

- [ ] **Step 4: Register lifecycle services**

Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`:

Add this using:

```csharp
using LightRAGNet.Services.DocumentLifecycle;
```

Add this after KV store registration:

```csharp
services.AddSingleton<IDocumentStatusStore, KvDocumentStatusStore>();
services.AddSingleton<DocumentLifecycleService>();
```

- [ ] **Step 5: Build the solution**

Run:

```powershell
dotnet build .\LightRAGNet.slnx
```

Expected: build succeeds with existing warnings only.

- [ ] **Step 6: Commit Task 3**

```powershell
git add src/LightRAGNet.Storage/KVContracts.cs src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs
git commit -m "feat: persist document lifecycle status"
```

---

### Task 4: Integrate Lifecycle Into `LightRAG.InsertAsync`

**Files:**

- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`

- [ ] **Step 1: Write a characterization test for duplicate short-circuit**

Create `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs` with a minimal first test:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class LightRAGLifecycleIntegrationTests
{
    [Fact]
    public async Task PrepareIngestion_DuplicateDocument_DoesNotCreateSecondPendingRecord()
    {
        var store = new InMemoryDocumentStatusStore();
        var lifecycle = new DocumentLifecycleService(
            store,
            Options.Create(new LightRAGOptions { Workspace = "_" }),
            NullLogger<DocumentLifecycleService>.Instance);

        var first = await lifecycle.PrepareIngestionAsync("same content", "doc-same", "a.md", "track-a");
        var second = await lifecycle.PrepareIngestionAsync("same content", "doc-same", "b.md", "track-b");

        first.IsDuplicate.Should().BeFalse();
        second.IsDuplicate.Should().BeTrue();
        second.StatusRecord.FilePath.Should().Be("a.md");
    }
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGLifecycleIntegrationTests
```

Expected: PASS. This test locks the duplicate contract before touching `LightRAG.cs`.

- [ ] **Step 2: Inject lifecycle service into `LightRAG`**

Modify the primary constructor in `src/LightRAGNet/LightRAG.cs`:

```csharp
using LightRAGNet.Services.DocumentLifecycle;
```

Add a constructor parameter before `ILogger<LightRAG> logger`:

```csharp
DocumentLifecycleService documentLifecycleService,
```

- [ ] **Step 3: Use lifecycle service at the start of `InsertAsync`**

Replace the existing document id and duplicate block:

```csharp
// 1. Generate document ID
docId ??= HashUtils.ComputeMd5Hash(content, "doc-");
filePath ??= "unknown_source";

// 2. Check if document already exists
var existingDoc = await fullDocsStore.GetByIdAsync(docId, cancellationToken);
if (existingDoc != null)
{
    logger.LogWarning("Document {DocId} already exists", docId);
    PostTaskState(new TaskState
    {
        Stage = TaskStage.Completed,
        Current = 1,
        Total = 1,
        Description = "Document already exists, skipping insertion",
        DocId = docId
    });
    return docId;
}
```

with:

```csharp
var ingestion = await documentLifecycleService.PrepareIngestionAsync(
    content,
    docId,
    filePath,
    cancellationToken: cancellationToken);

docId = ingestion.DocId;
filePath = ingestion.StatusRecord.FilePath;

if (ingestion.IsDuplicate)
{
    logger.LogWarning("Document {DocId} already exists in workspace {Workspace}", docId, ingestion.Workspace);
    PostTaskState(new TaskState
    {
        Stage = TaskStage.Completed,
        Current = 1,
        Total = 1,
        Description = "Document already exists, skipping insertion",
        DocId = docId
    });
    return docId;
}
```

- [ ] **Step 4: Record processing and chunks**

Immediately before the existing document chunking state post, add:

```csharp
await documentLifecycleService.StartProcessingAsync(ingestion.Workspace, docId, cancellationToken);
```

Immediately after `var chunks = documentProcessingService.ChunkDocument(...)`, add:

```csharp
await documentLifecycleService.RecordChunksAsync(ingestion.Workspace, docId, chunks, cancellationToken);
```

- [ ] **Step 5: Mark successful insertion as processed**

Immediately before the final `return docId;`, after the completed task state post, add:

```csharp
await documentLifecycleService.MarkProcessedAsync(ingestion.Workspace, docId, cancellationToken);
```

- [ ] **Step 6: Add failure recording around processing**

Wrap the existing post-chunk pipeline steps in a `try/catch`. Keep state initialization and `PrepareIngestionAsync` outside the `try`. Use stage names from this plan:

```csharp
try
{
    // Existing chunking, chunk processing, stores, vector upsert, graph merge, full doc store, persist, and completed state logic stays here.
}
catch (Exception ex)
{
    var stage = ex is InvalidOperationException ? "chunking" : "insert_pipeline";
    await documentLifecycleService.MarkFailedAsync(
        ingestion.Workspace,
        docId,
        stage,
        ex.Message,
        cancellationToken);
    throw;
}
```

If a worker can determine a more specific stage without broad rewrites, use one of these exact strings: `chunking`, `process_chunks`, `store_text_chunks`, `store_chunk_vectors`, `merge_graph`, `store_full_document`, `persist`.

- [ ] **Step 7: Run core tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit Task 4**

```powershell
git add src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs
git commit -m "feat: connect insert flow to document lifecycle"
```

---

### Task 5: Add Workspace And Deletion Contract Regression Coverage

**Files:**

- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`
- Modify: `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`

- [ ] **Step 1: Add a test for explicit default workspace behavior**

Append:

```csharp
[Fact]
public async Task PrepareIngestion_WhenWorkspaceIsBlank_UsesDefaultWorkspace()
{
    var store = new InMemoryDocumentStatusStore();
    var service = CreateService(store, workspace: "");

    var result = await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");

    result.Workspace.Should().Be("_");
    (await store.GetAsync("_", "doc-1")).Should().NotBeNull();
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~PrepareIngestion_WhenWorkspaceIsBlank
```

Expected: PASS.

- [ ] **Step 2: Add a test for deletion plan on `DeletionFailed` records**

Append:

```csharp
[Fact]
public async Task CreateDeletionPlan_DeletionFailedDocument_RemainsRetryable()
{
    var store = new InMemoryDocumentStatusStore();
    var service = CreateService(store);
    await service.PrepareIngestionAsync("content", "doc-1", "file.md", "track-1");
    await service.StartProcessingAsync("_", "doc-1");
    await service.RecordChunksAsync("_", "doc-1", [
        new Chunk { Id = "chunk-1", Tokens = 3, ChunkOrderIndex = 0, FilePath = "file.md", Content = "aaa", FullDocId = "doc-1" }
    ]);
    await service.MarkDeletionFailedAsync("_", "doc-1", "delete_text_chunks", "disk failed");

    var plan = await service.CreateDeletionPlanAsync("_", "doc-1");

    plan.Found.Should().BeTrue();
    plan.ChunkIds.Should().Equal("chunk-1");
    plan.DeleteTextChunks.Should().BeTrue();
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~CreateDeletionPlan_DeletionFailedDocument_RemainsRetryable
```

Expected: PASS. The test name is intentionally aligned with the lifecycle status spelling.

- [ ] **Step 3: Run full unit tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Commit Task 5**

```powershell
git add src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests/DocumentLifecycle
git commit -m "test: cover lifecycle workspace and deletion contracts"
```

---

### Task 6: Add Thin Server Smoke Coverage Or Document Why It Is Deferred

**Files:**

- Inspect: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Inspect: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
- Modify or create: `tests/LightRAGNet.Server.Tests/*`

- [ ] **Step 1: Inspect current server test host**

Run:

```powershell
Get-Content -Encoding utf8 .\tests\LightRAGNet.Server.Tests\ServerHostSmokeTests.cs
```

Expected: identify how the current smoke test starts the host.

- [ ] **Step 2: If the server host already supports replacement services, add this smoke test**

Add to an existing server test file or create `tests/LightRAGNet.Server.Tests/DocumentLifecycleApiSmokeTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentLifecycleApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public DocumentLifecycleApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task OpenApiOrRoot_WhenServerStarts_ReturnsReachableResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.InternalServerError);
    }
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: If the server host requires real external services, do not force a brittle API test**

If Step 2 fails because the host requires Qdrant, Neo4j, or real provider credentials before requests can be made, delete the new test file and append this note to `docs/superpowers/plans/2026-05-18-document-lifecycle-alignment-implementation-plan.md` under this task:

```markdown
Server smoke coverage for lifecycle status is deferred because the current host construction still requires external Qdrant/Neo4j/provider configuration before request handling. Keep lifecycle behavior in core tests until the server test host has service replacement hooks.
```

- [ ] **Step 4: Commit Task 6**

If a test was added:

```powershell
git add tests/LightRAGNet.Server.Tests
git commit -m "test: add server lifecycle smoke coverage"
```

If a note was added:

```powershell
git add docs/superpowers/plans/2026-05-18-document-lifecycle-alignment-implementation-plan.md
git commit -m "docs: note server lifecycle smoke boundary"
```

---

### Task 7: Final Verification And Asset Gate

**Files:**

- Verify all changed code and tests.
- Update only files directly needed to fix verification failures.

- [ ] **Step 1: Restore**

Run:

```powershell
dotnet restore .\LightRAGNet.slnx
```

Expected: restore succeeds. Existing NU1903 warning for `System.Security.Cryptography.Xml 9.0.0` may remain.

- [ ] **Step 2: Build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx
```

Expected: build succeeds. Existing warnings may remain; new errors must be fixed before continuing.

- [ ] **Step 3: Test**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
```

Expected: all tests pass.

- [ ] **Step 4: Run asset completion gate**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.2\skills\compound-development-asset\scripts\check_completion_gate.py . --json
```

Expected: `status` is `pass`.

- [ ] **Step 5: Commit verification fixes when verification changed files**

When `git status --short` shows files changed during verification, inspect `git diff`, stage only the direct fix files, and commit:

```powershell
git status --short
git diff
git add src/LightRAGNet tests/LightRAGNet.Tests tests/LightRAGNet.Server.Tests
git commit -m "fix: stabilize document lifecycle verification"
```

When `git status --short` is clean, skip this step without creating a commit.

---

## Self-Review Notes

Spec coverage:

- Lifecycle model and state machine are covered by Tasks 1 and 2.
- `IKVStore` status persistence is covered by Task 3.
- `LightRAG.InsertAsync` integration and duplicate behavior are covered by Task 4.
- Workspace isolation and deletion retry contracts are covered by Task 5.
- Thin server coverage is handled by Task 6 with an explicit fallback when host dependencies block a useful smoke test.
- Verification and asset gate are covered by Task 7.

Implementation boundaries:

- The plan does not implement full graph rebuild, vector deletion, Neo4j pruning, or LLM cache cleanup.
- The plan keeps real storage integration minimal by using `doc_status` through the existing keyed KV system.
- The plan uses TDD for each behavior before production code changes where the behavior is new.
