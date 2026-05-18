# Document Deletion Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Every production-code step must be preceded by a failing test and a recorded RED result.

**Goal:** Implement retryable, background-task-based single-document deletion that aligns LightRAGNet with Python LightRAG `adelete_by_doc_id`.

**Architecture:** Deletion is split into a core RAG deletion service, a public `LightRAG.DeleteDocumentAsync` entry point, queue/server orchestration, and Blazor/API UX changes. Core storage deletion is tested with in-memory KV/vector/graph stores before touching API/UI. Server deletion removes the Markdown row and uploaded file only after RAG storage deletion succeeds.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, ASP.NET Core, Blazor Server, MudBlazor, JSON KV stores, Qdrant, Neo4j.

---

## Required Worktree

All implementation work must happen in:

```text
C:\WorkSpace\RiderProjects\LightRAGNet\.worktrees\document-deletion-parity
```

The worktree already exists on branch:

```text
feature/document-deletion-parity
```

Before starting any task:

```powershell
git status --short --branch
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentLifecycleServiceTests
```

Expected baseline:

```text
## feature/document-deletion-parity
... existing lifecycle tests pass
```

If baseline tests fail, stop and diagnose before writing new code.

## Spec-to-Plan Traceability

This section is a hard gate. If a spec row cannot be mapped to a task, this plan is incomplete.

| Spec requirement | Plan coverage |
| --- | --- |
| Allow deletion of indexed documents | Task 6 API behavior, Task 8 UI behavior |
| Not-indexed documents delete synchronously | Task 6 API tests and implementation |
| Indexed/deletion-failed documents enqueue deletion task | Task 1 task contracts, Task 5 processor, Task 6 API |
| Block delete while insertion is pending/processing | Task 1 queue guard, Task 6 API conflict test |
| Keep visible row on deletion failure | Task 5 handler behavior, Task 6 API, Task 8 UI |
| Remove row/file only after RAG deletion succeeds | Task 5 status path, Task 7 cleanup tests |
| Collect chunk ids from `doc_status` | Task 4 `LightRAG.DeleteDocumentAsync`, Task 3 deletion context |
| Delete `full_docs`, `text_chunks`, chunk vectors | Task 3 core deletion execution |
| Read document-level entity/relation indexes | Task 3 context loading |
| Prune graph node/edge `source_id` | Task 2 parser, Task 3 impact analysis and execution |
| Delete entities/relations with no remaining sources | Task 3 owned graph deletion tests |
| Update/rebuild retained entities/relations | Task 3 shared graph update tests |
| Update/delete `entity_chunks` and `relation_chunks` | Task 3 tracking tests |
| Optional LLM cache deletion, disabled by default | Task 3 cache tests, Task 4 public API option |
| Record failed deletion stages and allow retry | Task 1 lifecycle metadata, Task 3 failure tests, Task 6 retry |
| Use background task/status path | Task 1 queue contracts, Task 5 processor, Task 8 SignalR/UI |
| API returns `204`, `202`, `409`, `404` as designed | Task 6 server tests |
| `clear-all` remains bulk reset and includes `doc_status` | Task 9 cleanup boundary |
| Normal tests do not require Docker | Tasks 2-9 use fakes; Task 10 optional integration only |

Before final implementation review, run this grep and manually verify every term still has a task:

```powershell
rg -n "indexed|DeletionFailed|doc_status|source_id|entity_chunks|relation_chunks|llm|clear-all|202|409|Qdrant|Neo4j" docs/superpowers/specs/2026-05-18-document-deletion-parity-design.md docs/superpowers/plans/2026-05-18-document-deletion-parity-implementation-plan.md
```

## Shared Contracts

These names are fixed for all tasks. Do not invent alternate names unless a reviewer approves a concrete reason.

### New Task Operation Type

```csharp
namespace LightRAGNet.Models;

public enum RagTaskOperationType
{
    IndexDocument,
    DeleteDocument
}
```

Add to `RagTask`:

```csharp
public RagTaskOperationType OperationType { get; set; } = RagTaskOperationType.IndexDocument;
public bool DeleteLlmCache { get; set; }
public string? DeleteFilePath { get; set; }
```

### New Delete Result DTOs

Create `src/LightRAGNet.Share/Models/MarkdownDocumentDeleteResult.cs`:

```csharp
namespace LightRAGNet.Share.Models;

public class MarkdownDocumentDeleteResult
{
    public bool Accepted { get; set; }
    public bool DeletedImmediately { get; set; }
    public string? TaskId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
```

Create `src/LightRAGNet.Web/MarkdownDocumentDeleteClientResult.cs`:

```csharp
namespace LightRAGNet.Web;

public sealed class MarkdownDocumentDeleteClientResult
{
    public bool Succeeded { get; init; }
    public bool DeletedImmediately { get; init; }
    public bool Accepted { get; init; }
    public bool Conflict { get; init; }
    public string? TaskId { get; init; }
    public string? ErrorMessage { get; init; }
}
```

### New Deletion Stage Names

Create `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionStage.cs`:

```csharp
namespace LightRAGNet.Services.DocumentDeletion;

public static class DocumentDeletionStage
{
    public const string PrepareDeletion = "prepare_deletion";
    public const string CollectChunks = "collect_chunks";
    public const string CollectLlmCache = "collect_llm_cache";
    public const string AnalyzeGraphReferences = "analyze_graph_references";
    public const string DeleteChunkVectors = "delete_chunk_vectors";
    public const string DeleteTextChunks = "delete_text_chunks";
    public const string DeleteGraphRelations = "delete_graph_relations";
    public const string DeleteGraphEntities = "delete_graph_entities";
    public const string UpdateGraphReferences = "update_graph_references";
    public const string DeleteRelationVectors = "delete_relation_vectors";
    public const string DeleteEntityVectors = "delete_entity_vectors";
    public const string UpdateRelationVectors = "update_relation_vectors";
    public const string UpdateEntityVectors = "update_entity_vectors";
    public const string DeleteRelationTracking = "delete_relation_tracking";
    public const string DeleteEntityTracking = "delete_entity_tracking";
    public const string DeleteLlmCache = "delete_llm_cache";
    public const string DeleteDocumentMetadata = "delete_document_metadata";
    public const string DeleteDocStatus = "delete_doc_status";
    public const string DeleteMarkdownRecord = "delete_markdown_record";
    public const string DeleteUploadedFile = "delete_uploaded_file";
}
```

`DocumentDeletionStage` is the shared stage vocabulary for both core RAG deletion and server cleanup. The core `DocumentDeletionService` only executes core stages through `DeleteDocStatus`; server-only stages such as Markdown row/file deletion stay in server code and reuse `DeleteMarkdownRecord` / `DeleteUploadedFile`.

---

## Task 1: Task and Lifecycle Contracts

**Spec coverage:** background task model, deletion failed visibility, retry metadata, same-document concurrency guard.

**Files:**

- Modify: `src/LightRAGNet/Models/RagTask.cs`
- Modify: `src/LightRAGNet/Services/TaskQueue/IRagTaskQueueService.cs`
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskQueueService.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Modify: `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`
- Test: `tests/LightRAGNet.Server.Tests/MarkdownDocumentsControllerTests.cs`
- Test support: `tests/LightRAGNet.Server.Tests/LightRagServerFactory.cs`

- [ ] **Step 1: Add failing queue contract tests**

Append these tests to `RagTaskQueueServiceTests.cs`. Adjust helper names only if the file already has equivalent helpers.

```csharp
[Fact]
public async Task EnqueueDeletionTaskAsync_WhenIndexTaskPendingForDocument_ReturnsNullAndDoesNotCreateTask()
{
    var (service, _, _) = CreateService();
    await service.EnqueueTaskAsync(42, "alpha beta", "alpha.md");

    var taskId = await service.EnqueueDeletionTaskAsync(
        42,
        "doc-alpha",
        "alpha.md",
        deleteLlmCache: false);

    taskId.Should().BeNull();
    var tasks = await service.GetAllTasksAsync();
    tasks.Should().ContainSingle();
    tasks[0].OperationType.Should().Be(RagTaskOperationType.IndexDocument);
}

[Fact]
public async Task EnqueueDeletionTaskAsync_WhenNoActiveTask_CreatesDeleteTask()
{
    var (service, _, _) = CreateService();

    var taskId = await service.EnqueueDeletionTaskAsync(
        42,
        "doc-alpha",
        "alpha.md",
        deleteLlmCache: true);

    taskId.Should().NotBeNullOrWhiteSpace();
    var task = await service.GetTaskAsync(taskId!);
    task.Should().NotBeNull();
    task!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
    task.RagDocumentId.Should().Be("doc-alpha");
    task.DeleteLlmCache.Should().BeTrue();
    task.DeleteFilePath.Should().Be("alpha.md");
    task.Status.Should().Be(RagTaskStatus.Pending);
}

[Fact]
public async Task EnqueueDeletionTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNull()
{
    var (service, _, _) = CreateService();
    await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

    var duplicate = await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

    duplicate.Should().BeNull();
    var tasks = await service.GetAllTasksAsync();
    tasks.Should().ContainSingle(t => t.OperationType == RagTaskOperationType.DeleteDocument);
}

[Fact]
public async Task EnqueueTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNullAndDoesNotCreateIndexTask()
{
    var (service, _, _) = CreateService();
    await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

    var indexTaskId = await service.EnqueueTaskAsync(42, "alpha beta", "alpha.md");

    indexTaskId.Should().BeNull();
    var tasks = await service.GetAllTasksAsync();
    tasks.Should().ContainSingle(t => t.OperationType == RagTaskOperationType.DeleteDocument);
}

[Fact]
public async Task EnqueueDeletionTaskAsync_PublishesDeleteOperationMetadata()
{
    var (service, _, mediator) = CreateService();

    await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: true);

    await mediator.Received(1).Publish(
        Arg.Is<RagTaskStatusChangedEvent>(evt =>
            evt.Task.OperationType == RagTaskOperationType.DeleteDocument &&
            evt.Task.DeleteLlmCache &&
            evt.Task.DeleteFilePath == "alpha.md"),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task GetNextTaskAsync_WhenDeleteTaskPending_ReturnsDeleteTaskMetadata()
{
    var (service, _, _) = CreateService();
    await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: true);

    var next = await service.GetNextTaskAsync();

    next.Should().NotBeNull();
    next!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
    next.RagDocumentId.Should().Be("doc-alpha");
    next.DeleteLlmCache.Should().BeTrue();
}
```

- [ ] **Step 2: Verify RED for queue tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RagTaskQueueServiceTests.Enque"
```

Expected RED:

```text
error CS1061: 'IRagTaskQueueService' does not contain a definition for 'EnqueueDeletionTaskAsync'
error CS0103: The name 'RagTaskOperationType' does not exist
```

- [ ] **Step 3: Add failing lifecycle tests**

Append these to `DocumentLifecycleServiceTests.cs`:

```csharp
[Fact]
public async Task MarkDeletionStartedAsync_WhenProcessed_MarksDeletingAndClearsPreviousFailure()
{
    var store = new InMemoryDocumentStatusStore();
    var service = CreateService(store);
    await PrepareProcessedDocumentAsync(service);
    await service.MarkDeletionFailedAsync("workspace-a", "doc-1", "delete_chunk_vectors", "qdrant failed");

    await service.MarkDeletionStartedAsync("workspace-a", "doc-1");

    var stored = await store.GetAsync("workspace-a", "doc-1");
    stored.Should().NotBeNull();
    stored!.Status.Should().Be(DocumentLifecycleStatus.Deleting);
    stored.ErrorMessage.Should().BeEmpty();
    stored.Metadata.Should().NotContainKey("deletion_failed");
    stored.Metadata.Should().NotContainKey("deletion_failure_stage");
}

[Fact]
public async Task MarkDeletionSucceededAsync_WhenDeleting_DeletesStatusRecord()
{
    var store = new InMemoryDocumentStatusStore();
    var service = CreateService(store);
    await PrepareProcessedDocumentAsync(service);
    await service.MarkDeletionStartedAsync("workspace-a", "doc-1");

    await service.MarkDeletionSucceededAsync("workspace-a", "doc-1");

    var stored = await store.GetAsync("workspace-a", "doc-1");
    stored.Should().BeNull();
}

[Fact]
public async Task MarkDeletionFailedAsync_WithCacheIds_PreservesRetryMetadata()
{
    var store = new InMemoryDocumentStatusStore();
    var service = CreateService(store);
    await PrepareProcessedDocumentAsync(service);

    await service.MarkDeletionFailedAsync(
        "workspace-a",
        "doc-1",
        "delete_llm_cache",
        "cache failed",
        ["cache-a", "cache-b"]);

    var stored = await store.GetAsync("workspace-a", "doc-1");
    stored.Should().NotBeNull();
    stored!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
    stored.Metadata["deletion_failure_stage"].Should().Be("delete_llm_cache");
    stored.Metadata["deletion_llm_cache_ids"].Should().BeEquivalentTo(new[] { "cache-a", "cache-b" });
}
```

- [ ] **Step 4: Verify RED for lifecycle tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentLifecycleServiceTests.MarkDeletion"
```

Expected RED:

```text
error CS1061: 'DocumentLifecycleService' does not contain a definition for 'MarkDeletionStartedAsync'
error CS1061: 'DocumentLifecycleService' does not contain a definition for 'MarkDeletionSucceededAsync'
```

- [ ] **Step 5: Implement minimal contracts**

Implement `RagTaskOperationType` in `RagTask.cs` and add the properties from Shared Contracts.

Add this to `IRagTaskQueueService`:

```csharp
Task<string?> EnqueueTaskAsync(
    int documentId,
    string content,
    string filePath,
    CancellationToken cancellationToken = default);

Task<string?> EnqueueDeletionTaskAsync(
    int documentId,
    string ragDocumentId,
    string filePath,
    bool deleteLlmCache,
    CancellationToken cancellationToken = default);
```

Change `RagTaskQueueService.EnqueueTaskAsync` to return `string?`. Before creating a new index task, reject active same-document tasks:

```csharp
var hasActiveTask = _tasks.Values.Any(t =>
    t.DocumentId == documentId &&
    (t.Status == RagTaskStatus.Pending || t.Status == RagTaskStatus.Processing));

if (hasActiveTask)
{
    logger.LogWarning("Cannot enqueue indexing for document {DocumentId}; active task exists.", documentId);
    return null;
}
```

Update `MarkdownDocumentsController.AddToRagSystem` so a null task id returns `409 Conflict` and does not mark the document as `Pending`.

Add a server regression test named `AddToRagSystem_WhenQueueRejectsTask_ReturnsConflictAndDoesNotMarkPending`:

```csharp
[Fact]
public async Task AddToRagSystem_WhenQueueRejectsTask_ReturnsConflictAndDoesNotMarkPending()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 10,
        FileName = "blocked.md",
        Content = "content",
        RagStatus = null
    });
    await SeedDeleteTaskAsync(factory, documentId: 10);
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/10/add-to-rag", null);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(10);
    document!.RagStatus.Should().BeNull();
}
```

If the existing server test factory cannot seed active queue state, extend `LightRagServerFactory` with the smallest test hook needed to configure or access the in-memory queue. Do not introduce production-only test hooks.

In `RagTaskQueueService.EnqueueDeletionTaskAsync`:

```csharp
public async Task<string?> EnqueueDeletionTaskAsync(
    int documentId,
    string ragDocumentId,
    string filePath,
    bool deleteLlmCache,
    CancellationToken cancellationToken = default)
{
    await EnsureTasksLoadedAsync(cancellationToken);
    await _lock.WaitAsync(cancellationToken);
    RagTask task;
    try
    {
        var hasActiveTask = _tasks.Values.Any(t =>
            t.DocumentId == documentId &&
            (t.Status == RagTaskStatus.Pending || t.Status == RagTaskStatus.Processing));

        if (hasActiveTask)
        {
            logger.LogWarning("Cannot enqueue deletion for document {DocumentId}; active task exists.", documentId);
            return null;
        }

        var taskId = HashUtils.ComputeMd5Hash(
            $"delete_{documentId}_{ragDocumentId}_{DateTime.UtcNow:O}",
            "task-");

        task = new RagTask
        {
            TaskId = taskId,
            DocumentId = documentId,
            RagDocumentId = ragDocumentId,
            FilePath = filePath,
            DeleteFilePath = filePath,
            DeleteLlmCache = deleteLlmCache,
            OperationType = RagTaskOperationType.DeleteDocument,
            Status = RagTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _tasks.TryAdd(taskId, task);
        await stateStore.SaveTaskStateAsync(task, cancellationToken);
    }
    finally
    {
        _lock.Release();
    }

    await PublishStatusChangedAsync(task, cancellationToken);
    return task.TaskId;
}
```

In `DocumentLifecycleService`, add:

```csharp
public async Task MarkDeletionStartedAsync(
    string workspace,
    string docId,
    CancellationToken cancellationToken = default)
{
    var normalizedWorkspace = NormalizeWorkspace(workspace);
    var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
    if (record is null)
    {
        LogMissingStatusMutation(normalizedWorkspace, docId, nameof(MarkDeletionStartedAsync));
        return;
    }

    record.Status = DocumentLifecycleStatus.Deleting;
    record.ErrorMessage = string.Empty;
    record.Metadata.Remove("deletion_failed");
    record.Metadata.Remove("deletion_failure_stage");
    Touch(record);
    await _statusStore.UpsertAsync(record, cancellationToken);
}

public async Task MarkDeletionSucceededAsync(
    string workspace,
    string docId,
    CancellationToken cancellationToken = default)
{
    var normalizedWorkspace = NormalizeWorkspace(workspace);
    await _statusStore.DeleteAsync(normalizedWorkspace, docId, cancellationToken);
}
```

Extend `MarkDeletionFailedAsync` with optional cache ids:

```csharp
public async Task<DocumentDeletionResult> MarkDeletionFailedAsync(
    string workspace,
    string docId,
    string stage,
    string errorMessage,
    IReadOnlyCollection<string>? llmCacheIds,
    CancellationToken cancellationToken = default)
```

Keep the old overload by delegating to the new one with `llmCacheIds: null`.

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RagTaskQueueServiceTests.Enque|FullyQualifiedName~DocumentLifecycleServiceTests.MarkDeletion"
```

Expected GREEN:

```text
Passed
```

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet/Models/RagTask.cs src/LightRAGNet/Services/TaskQueue src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests
git commit -m "feat: add deletion task contracts"
```

---

## Task 2: Graph Source Parser and In-Memory Storage Doubles

**Spec coverage:** parse/prune `source_id`, update/delete tracking records, normal tests without Docker.

**Files:**

- Create: `src/LightRAGNet/Services/DocumentDeletion/GraphSourceReferenceParser.cs`
- Modify: `src/LightRAGNet/Services/KnowledgeGraphMerge/RelationBuilder.cs`
- Modify: `src/LightRAGNet/Services/KnowledgeGraphMerge/StorageUpdateStage.cs`
- Test: `tests/LightRAGNet.Tests/DocumentDeletion/GraphSourceReferenceParserTests.cs`
- Test: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/RelationBuilderTests.cs`
- Test: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/StorageUpdateStageTests.cs`
- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryKvStore.cs`
- Test: `tests/LightRAGNet.Tests/TestDoubles/InMemoryKvStoreTests.cs`
- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
- Test: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStoreTests.cs`
- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryGraphStore.cs`
- Test: `tests/LightRAGNet.Tests/TestDoubles/InMemoryGraphStoreTests.cs`

- [ ] **Step 1: Write failing parser tests**

Create `GraphSourceReferenceParserTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentDeletion;

namespace LightRAGNet.Tests.DocumentDeletion;

public sealed class GraphSourceReferenceParserTests
{
    [Fact]
    public void Split_RemovesEmptyValuesAndPreservesOrder()
    {
        var result = GraphSourceReferenceParser.Split("chunk-a<SEP>chunk-b<SEP><SEP>chunk-a");

        result.Should().Equal("chunk-a", "chunk-b");
    }

    [Fact]
    public void Prune_RemovesDeletedSourcesAndPreservesRemainingOrder()
    {
        var result = GraphSourceReferenceParser.Prune(
            "chunk-a<SEP>chunk-b<SEP>chunk-c",
            new HashSet<string> { "chunk-b" });

        result.Should().Equal("chunk-a", "chunk-c");
    }

    [Fact]
    public void Join_UsesPythonGraphFieldSeparator()
    {
        var result = GraphSourceReferenceParser.Join(["chunk-a", "chunk-b"]);

        result.Should().Be("chunk-a<SEP>chunk-b");
    }

    [Fact]
    public void MakeRelationKey_SortsEndpoints()
    {
        GraphSourceReferenceParser.MakeRelationKey("zeta", "alpha")
            .Should().Be("alpha<SEP>zeta");
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~GraphSourceReferenceParserTests
```

Expected RED:

```text
error CS0246: The type or namespace name 'DocumentDeletion' does not exist
```

- [ ] **Step 3: Implement parser**

Create `GraphSourceReferenceParser.cs`:

```csharp
namespace LightRAGNet.Services.DocumentDeletion;

public static class GraphSourceReferenceParser
{
    public const string GraphFieldSep = "<SEP>";

    public static IReadOnlyList<string> Split(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var part in sourceId.Split(GraphFieldSep, StringSplitOptions.None))
        {
            var value = part.Trim();
            if (value.Length == 0 || !seen.Add(value))
            {
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    public static IReadOnlyList<string> Prune(string? sourceId, ISet<string> deletedChunkIds)
    {
        return Split(sourceId)
            .Where(id => !deletedChunkIds.Contains(id))
            .ToList();
    }

    public static string Join(IEnumerable<string> sourceIds)
    {
        return string.Join(GraphFieldSep, sourceIds.Where(id => !string.IsNullOrWhiteSpace(id)));
    }

    public static string MakeRelationKey(string sourceId, string targetId)
    {
        var sorted = new[] { sourceId, targetId }
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return Join(sorted);
    }
}
```

- [ ] **Step 4: Add in-memory stores**

Create `InMemoryKvStore.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class InMemoryKvStore : IKVStore
{
    private readonly Dictionary<string, Dictionary<string, object>> items = new(StringComparer.Ordinal);

    public Dictionary<string, Dictionary<string, object>> Items => items.ToDictionary(
        pair => pair.Key,
        pair => Clone(pair.Value),
        StringComparer.Ordinal);
    public List<IReadOnlyList<string>> DeleteCalls { get; } = [];
    public List<IReadOnlyDictionary<string, Dictionary<string, object>>> UpsertCalls { get; } = [];
    public string? ThrowOnDeleteKey { get; set; }
    public string? ThrowOnUpsertKey { get; set; }

    public Task<Dictionary<string, object>?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(items.TryGetValue(id, out var value) ? Clone(value) : null);
    }

    public Task<List<Dictionary<string, object>>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ids.Where(items.ContainsKey).Select(id => Clone(items[id])).ToList());
    }

    public Task<HashSet<string>> FilterKeysAsync(HashSet<string> keys, CancellationToken cancellationToken = default)
    {
        // Match JsonKVStore: return keys that are missing from storage.
        return Task.FromResult(keys.Where(key => !items.ContainsKey(key)).ToHashSet(StringComparer.Ordinal));
    }

    public Task UpsertAsync(Dictionary<string, Dictionary<string, object>> data, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsertKey is not null && data.ContainsKey(ThrowOnUpsertKey))
        {
            throw new InvalidOperationException($"upsert failed: {ThrowOnUpsertKey}");
        }

        UpsertCalls.Add(data);
        foreach (var (key, value) in data)
        {
            items[key] = Clone(value);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idsList = ids.ToList();
        DeleteCalls.Add(idsList);
        foreach (var id in idsList)
        {
            if (ThrowOnDeleteKey == id)
            {
                throw new InvalidOperationException($"delete failed: {id}");
            }

            items.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) => Task.FromResult(items.Count == 0);
    public Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DropAsync(CancellationToken cancellationToken = default)
    {
        items.Clear();
        return Task.CompletedTask;
    }

    public void Seed(string id, Dictionary<string, object> value) => items[id] = Clone(value);

    private static Dictionary<string, object> Clone(Dictionary<string, object> value)
    {
        return value.ToDictionary(kvp => kvp.Key, kvp => CloneValue(kvp.Value), StringComparer.Ordinal);
    }

    private static object CloneValue(object value)
    {
        return value switch
        {
            Dictionary<string, object> dictionary => Clone(dictionary),
            List<object> list => list.Select(CloneValue).ToList(),
            List<string> list => list.ToList(),
            _ => value
        };
    }
}
```

Create vector/graph doubles with the same pattern:

```csharp
// InMemoryVectorStore: dictionary keyed by collection then id.
// Collections should expose a deep-cloned snapshot, not the internal dictionary.
// Implement QueryAsync by returning [] because deletion tests do not query.
// Record DeleteCalls as List<(string Collection, IReadOnlyList<string> Ids)>.
// Record UpsertCalls as List<(string Collection, IReadOnlyList<VectorDocument> Documents)>.
// Expose Seed(collection, VectorDocument) and Get(collection, id).

// InMemoryGraphStore: dictionaries for nodes and edges.
// Edge key should be GraphSourceReferenceParser.MakeRelationKey(source, target).
// Implement GetNodeAsync, GetEdgeAsync, GetNodesBatchAsync, GetEdgesBatchAsync,
// UpsertNodeAsync, UpsertEdgeAsync, DeleteNodeAsync, RemoveEdgesAsync.
// Expose DeletedNodes and DeletedEdges for assertions.
```

Also update `RelationBuilder` so relation chunk keys are generated with
`GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId)`. Writer code,
deletion code, and graph test doubles must share the same ordinal relation-key
helper; do not leave a culture-sensitive `OrderBy(x => x)` key path in the
writer.

Also update `StorageUpdateStage` so `full_relations.relation_pairs` are generated
from the same `GraphSourceReferenceParser.MakeRelationKey(sourceId, targetId)`
normalization. `InMemoryKvStore.Items` and `InMemoryVectorStore.Collections`
must expose clone/snapshot views so tests cannot mutate double internals without
going through store methods.

Do not add test-only methods to production types.

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~GraphSourceReferenceParserTests|FullyQualifiedName~RelationBuilderTests|FullyQualifiedName~StorageUpdateStage|FullyQualifiedName~InMemoryKvStore|FullyQualifiedName~InMemoryVectorStore|FullyQualifiedName~InMemoryGraphStore"
```

Expected GREEN:

```text
Passed
```

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet/Services/DocumentDeletion src/LightRAGNet/Services/KnowledgeGraphMerge tests/LightRAGNet.Tests/DocumentDeletion tests/LightRAGNet.Tests/KnowledgeGraphMerge tests/LightRAGNet.Tests/TestDoubles docs/superpowers/plans/2026-05-18-document-deletion-parity-implementation-plan.md
git commit -m "test: add deletion storage doubles"
```

---

## Task 3: Core Deletion Impact and Execution

**Spec coverage:** KV/vector/graph deletion, shared graph pruning, owned entity/relation deletion, tracking update/delete, optional LLM cache deletion, failure stage recording.

**Files:**

- Create: `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionRequest.cs`
- Create: `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionImpact.cs`
- Create: `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionStage.cs`
- Create: `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionService.cs`
- Create: `tests/LightRAGNet.Tests/DocumentDeletion/DocumentDeletionServiceTests.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Write failing owned graph deletion test**

Create `DocumentDeletionServiceTests.cs` with this first test:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentDeletion;

public sealed class DocumentDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_WhenEntityAndRelationOnlyUseDeletedChunks_RemovesGraphVectorsAndTracking()
    {
        var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(
            chunkIds: ["chunk-a", "chunk-b"]);
        fixture.FullEntities.Seed("doc-1", new()
        {
            ["entity_names"] = new List<object> { "ALPHA", "BETA" },
            ["count"] = 2
        });
        fixture.FullRelations.Seed("doc-1", new()
        {
            ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } },
            ["count"] = 1
        });
        fixture.EntityChunks.Seed("ALPHA", new() { ["chunk_ids"] = new List<object> { "chunk-a" }, ["count"] = 1 });
        fixture.EntityChunks.Seed("BETA", new() { ["chunk_ids"] = new List<object> { "chunk-b" }, ["count"] = 1 });
        fixture.RelationChunks.Seed("ALPHA<SEP>BETA", new() { ["chunk_ids"] = new List<object> { "chunk-a" }, ["count"] = 1 });
        fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["source_id"] = "chunk-a", ["description"] = "alpha desc" });
        fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["source_id"] = "chunk-b", ["description"] = "beta desc" });
        fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["source_id"] = "chunk-a", ["description"] = "rel desc", ["keywords"] = "rel" });

        var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest(
            Workspace: "workspace-a",
            DocId: "doc-1",
            ChunkIds: ["chunk-a", "chunk-b"],
            DeleteLlmCache: false));

        result.Succeeded.Should().BeTrue();
        fixture.Graph.DeletedNodes.Should().BeEquivalentTo("ALPHA", "BETA");
        fixture.Graph.DeletedEdges.Should().Contain(("ALPHA", "BETA"));
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "chunks" && call.Ids.SequenceEqual(["chunk-a", "chunk-b"]));
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "entities" && call.Ids.Contains("ALPHA") && call.Ids.Contains("BETA"));
        fixture.VectorStore.DeleteCalls.Should().Contain(call => call.Collection == "relationships" && call.Ids.Contains("ALPHA<SEP>BETA"));
        fixture.EntityChunks.Items.Should().NotContainKey("ALPHA");
        fixture.RelationChunks.Items.Should().NotContainKey("ALPHA<SEP>BETA");
    }
}
```

Add a private fixture at the bottom of the test file. It should wire all in-memory stores and `DocumentLifecycleService`. Keep it local to this test file.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletionServiceTests
```

Expected RED:

```text
error CS0246: The type or namespace name 'DocumentDeletionService' could not be found
error CS0246: The type or namespace name 'DocumentDeletionRequest' could not be found
```

- [ ] **Step 3: Implement request, impact, and service constructor**

Create:

```csharp
namespace LightRAGNet.Services.DocumentDeletion;

public sealed record DocumentDeletionRequest(
    string Workspace,
    string DocId,
    IReadOnlyList<string> ChunkIds,
    bool DeleteLlmCache);

public sealed class DocumentDeletionImpact
{
    public List<string> ChunkIdsToDelete { get; } = [];
    public List<string> EntityIdsToDelete { get; } = [];
    public List<EntityReferenceUpdate> EntityUpdates { get; } = [];
    public List<RelationReferenceDelete> RelationsToDelete { get; } = [];
    public List<RelationReferenceUpdate> RelationUpdates { get; } = [];
    public List<string> LlmCacheIdsToDelete { get; } = [];
}

public sealed record EntityReferenceUpdate(
    string EntityName,
    IReadOnlyList<string> RemainingChunkIds,
    Dictionary<string, object> UpdatedProperties,
    VectorDocument VectorDocument);

public sealed record RelationReferenceDelete(
    string SourceId,
    string TargetId,
    string RelationKey);

public sealed record RelationReferenceUpdate(
    string SourceId,
    string TargetId,
    string RelationKey,
    IReadOnlyList<string> RemainingChunkIds,
    Dictionary<string, object> UpdatedProperties,
    VectorDocument VectorDocument);
```

`DocumentDeletionService` constructor dependencies:

```csharp
public DocumentDeletionService(
    IVectorStore vectorStore,
    IGraphStore graphStore,
    IEmbeddingService embeddingService,
    [FromKeyedServices(KVContracts.TextChunks)] IKVStore textChunksStore,
    [FromKeyedServices(KVContracts.FullDocs)] IKVStore fullDocsStore,
    [FromKeyedServices(KVContracts.FullEntities)] IKVStore fullEntitiesStore,
    [FromKeyedServices(KVContracts.FullRelations)] IKVStore fullRelationsStore,
    [FromKeyedServices(KVContracts.EntityChunks)] IKVStore entityChunksStore,
    [FromKeyedServices(KVContracts.RelationChunks)] IKVStore relationChunksStore,
    [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
    DocumentLifecycleService lifecycleService,
    ILogger<DocumentDeletionService> logger)
```

- [ ] **Step 4: Implement minimal owned deletion**

Implement `DeleteAsync` for the first test:

1. Mark deletion started.
2. Collect optional LLM cache ids from `text_chunks[chunkId].llm_cache_list`.
3. Load `full_entities[docId]` and `full_relations[docId]`.
4. Read all referenced `entity_chunks`, `relation_chunks`, graph nodes, and graph edges needed to decide delete vs update.
5. Compute a `DocumentDeletionImpact` before any destructive storage call.
6. Delete chunk vectors from `chunks`.
7. Delete text chunks and persist the KV mutation with `IndexDoneCallbackAsync`.
8. Delete graph relations/entities whose tracking chunks are all deleted.
9. Update retained graph references, vectors, and tracking from the precomputed impact, persisting each KV tracking mutation as it is applied.
10. Delete full docs, full entities, and full relations, persisting each KV mutation as it is applied.
11. Mark deletion succeeded.

Use helper conversion methods inside the service:

```csharp
private static IReadOnlyList<string> ReadStringList(Dictionary<string, object>? data, string key)
```

It must support `List<object>`, `List<string>`, `IEnumerable<object>`, scalar strings, and `JsonElement` arrays/strings from reloaded `JsonKVStore` files. Relation pair parsing must also support nested `JsonElement` arrays.

- [ ] **Step 5: Verify GREEN for first deletion test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DeleteAsync_WhenEntityAndRelationOnlyUseDeletedChunks
```

Expected GREEN:

```text
Passed
```

- [ ] **Step 6: Add shared graph pruning test**

Add:

```csharp
[Fact]
public async Task DeleteAsync_WhenEntityAndRelationHaveSharedChunks_PrunesSourceIdsAndKeepsGraph()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    fixture.FullEntities.Seed("doc-1", new() { ["entity_names"] = new List<object> { "ALPHA" }, ["count"] = 1 });
    fixture.FullRelations.Seed("doc-1", new() { ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } }, ["count"] = 1 });
    fixture.EntityChunks.Seed("ALPHA", new() { ["chunk_ids"] = new List<object> { "chunk-a", "chunk-z" }, ["count"] = 2 });
    fixture.RelationChunks.Seed("ALPHA<SEP>BETA", new() { ["chunk_ids"] = new List<object> { "chunk-a", "chunk-z" }, ["count"] = 2 });
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["source_id"] = "chunk-a<SEP>chunk-z", ["description"] = "alpha desc" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["source_id"] = "chunk-z", ["description"] = "beta desc" });
    fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["source_id"] = "chunk-a<SEP>chunk-z", ["description"] = "rel desc", ["keywords"] = "rel" });

    var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

    result.Succeeded.Should().BeTrue();
    fixture.Graph.DeletedNodes.Should().BeEmpty();
    fixture.Graph.GetSeededNode("ALPHA")!.Properties["source_id"].Should().Be("chunk-z");
    fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["source_id"].Should().Be("chunk-z");
    fixture.EntityChunks.Items["ALPHA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-z" });
    fixture.RelationChunks.Items["ALPHA<SEP>BETA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-z" });
    fixture.VectorStore.UpsertCalls.Should().Contain(call => call.Collection == "entities");
    fixture.VectorStore.UpsertCalls.Should().Contain(call => call.Collection == "relationships");
}
```

- [ ] **Step 7: Verify RED, then implement shared update**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DeleteAsync_WhenEntityAndRelationHaveSharedChunks
```

Expected RED before implementation:

```text
Expected source_id to be "chunk-z" ...
```

Implement retained update:

- For retained entity, update graph node with existing properties plus pruned `source_id`.
- Generate new embeddings before upserting retained entity/relation vectors:

```csharp
var content = $"{entityName}\n{description}";
var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
```

- Upsert entity vector:

```csharp
new VectorDocument
{
    Id = entityName,
    Content = $"{entityName}\n{description}",
    Vector = embedding,
    Metadata = new Dictionary<string, object>
    {
        ["id"] = entityName,
        ["entity_name"] = entityName,
        ["source_id"] = GraphSourceReferenceParser.Join(remainingSources)
    }
}
```

The test fixture must inject a fake `IEmbeddingService` returning a deterministic non-empty vector such as `[0.1f, 0.2f]`. Do not use empty vectors in production code.

- [ ] **Step 8: Add LLM cache tests**

Add:

```csharp
[Fact]
public async Task DeleteAsync_WhenDeleteLlmCacheFalse_DoesNotDeleteCacheIds()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object> { "cache-a" } });

    await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

    fixture.LlmCache.DeleteCalls.Should().BeEmpty();
}

[Fact]
public async Task DeleteAsync_WhenDeleteLlmCacheTrue_DeletesChunkCacheIds()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    fixture.TextChunks.Seed("chunk-a", new() { ["llm_cache_list"] = new List<object> { "cache-a", "cache-b" } });
    fixture.LlmCache.Seed("cache-a", new() { ["return_value"] = "a" });
    fixture.LlmCache.Seed("cache-b", new() { ["return_value"] = "b" });

    await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: true));

    fixture.LlmCache.DeleteCalls.SelectMany(call => call).Should().BeEquivalentTo("cache-a", "cache-b");
}
```

Implement `CollectLlmCacheIds` before deleting text chunks.

- [ ] **Step 9: Add failure and retry tests**

Add:

```csharp
[Fact]
public async Task DeleteAsync_WhenUsingJsonKvStore_PersistsSuccessfulDeletion()
{
    // Seed JsonKVStore files, delete with DeleteLlmCache=true, then reload
    // JsonKVStore instances from the same files and assert text chunk, full doc,
    // and cache records stay deleted.
}

[Fact]
public async Task DeleteAsync_WhenJsonKvStoreReloadsArrays_ParsesJsonElementsForGraphImpact()
{
    // Seed and persist full_entities/full_relations/entity_chunks/relation_chunks,
    // reload JsonKVStore files, then delete and assert graph/vector/tracking
    // owned entity/relation deletion still happens.
}

[Fact]
public async Task DeleteAsync_WhenCancellationIsRequested_PropagatesAndDoesNotMarkDeletionFailed()
{
    // Use a canceled token and assert OperationCanceledException propagates
    // without recording DeletionFailed.
}

[Fact]
public async Task DeleteAsync_WhenDeletedEntityIsEndpointOfRetainedRelation_RetainsAndUpdatesEntity()
{
    // Entity tracking lists only deleted chunks, but a retained relation still
    // references the entity. Assert the entity is updated, not deleted.
}

[Fact]
public async Task DeleteAsync_WhenImpactAnalysisFails_DoesNotRunDestructiveDeletes()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    fixture.FullRelations.Seed("doc-1", new()
    {
        ["relation_pairs"] = new List<object> { new List<object> { "ALPHA", "BETA" } },
        ["count"] = 1
    });
    fixture.RelationChunks.ThrowOnGetKey = "ALPHA<SEP>BETA";

    var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

    result.Succeeded.Should().BeFalse();
    result.Stage.Should().Be(DocumentDeletionStage.AnalyzeGraphReferences);
    fixture.VectorStore.DeleteCalls.Should().NotContain(call => call.Collection == "chunks");
    fixture.TextChunks.DeleteCalls.Should().BeEmpty();
    fixture.FullDocs.DeleteCalls.Should().BeEmpty();
}

[Fact]
public async Task DeleteAsync_WhenVectorDeleteFails_RecordsFailureStage()
{
    var fixture = await DocumentDeletionFixture.CreateProcessedDocumentAsync(chunkIds: ["chunk-a"]);
    fixture.VectorStore.ThrowOnDeleteCollection = "chunks";

    var result = await fixture.Service.DeleteAsync(new DocumentDeletionRequest("workspace-a", "doc-1", ["chunk-a"], DeleteLlmCache: false));

    result.Succeeded.Should().BeFalse();
    result.Stage.Should().Be(DocumentDeletionStage.DeleteChunkVectors);
    var status = await fixture.StatusStore.GetAsync("workspace-a", "doc-1");
    status!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
    status.Metadata["deletion_failure_stage"].Should().Be(DocumentDeletionStage.DeleteChunkVectors);
}
```

Implement try/catch in `DeleteAsync`:

```csharp
try
{
    currentStage = DocumentDeletionStage.DeleteChunkVectors;
    await vectorStore.DeleteAsync("chunks", request.ChunkIds, cancellationToken);
    ...
}
catch (Exception ex)
{
    return await lifecycleService.MarkDeletionFailedAsync(
        request.Workspace,
        request.DocId,
        currentStage,
        ex.Message,
        collectedCacheIds,
        cancellationToken);
}
```

- [ ] **Step 10: Verify full core deletion tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletionServiceTests
```

Expected GREEN:

```text
Passed
```

- [ ] **Step 11: Register service and commit**

In `ServiceCollectionExtensions.AddLightRAG`, add:

```csharp
services.AddSingleton<DocumentDeletionService>();
```

Commit:

```powershell
git add src/LightRAGNet/Services/DocumentDeletion src/LightRAGNet.Hosting tests/LightRAGNet.Tests/DocumentDeletion tests/LightRAGNet.Tests/TestDoubles
git commit -m "feat: implement core document deletion"
```

---

## Task 4: Public `LightRAG.DeleteDocumentAsync`

**Spec coverage:** collect chunks from `doc_status`, public RAG deletion entry point, progress events, retryable failure.

**Files:**

- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: `src/LightRAGNet/Models/TaskState.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`

- [ ] **Step 1: Write failing integration test**

Add:

```csharp
[Fact]
public async Task DeleteDocumentAsync_ProcessedDocument_UsesLifecycleChunkIdsAndDeletesStorage()
{
    var statusStore = new InMemoryDocumentStatusStore();
    var lifecycleService = CreateLifecycleService(statusStore);
    await lifecycleService.PrepareIngestionAsync("alpha beta gamma", docId: "doc-delete", filePath: "delete.md");
    await lifecycleService.RecordChunksAsync("workspace-a", "doc-delete", [
        new Chunk { Id = "chunk-a", Content = "alpha", FullDocId = "doc-delete", FilePath = "delete.md", Tokens = 1, ChunkOrderIndex = 0 }
    ]);
    await lifecycleService.MarkProcessedAsync("workspace-a", "doc-delete");
    var textChunks = new InMemoryKvStore();
    textChunks.Seed("chunk-a", new() { ["content"] = "alpha" });
    var fullDocs = new InMemoryKvStore();
    fullDocs.Seed("doc-delete", new() { ["content"] = "alpha beta gamma" });
    var vectorStore = new InMemoryVectorStore();
    var rag = CreateLightRag(
        lifecycleService,
        textChunksStore: textChunks,
        fullDocsStore: fullDocs,
        vectorStore: vectorStore);

    var result = await rag.DeleteDocumentAsync("doc-delete");

    result.Succeeded.Should().BeTrue();
    textChunks.Items.Should().NotContainKey("chunk-a");
    fullDocs.Items.Should().NotContainKey("doc-delete");
    var status = await statusStore.GetAsync("workspace-a", "doc-delete");
    status.Should().BeNull();
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DeleteDocumentAsync_ProcessedDocument
```

Expected RED:

```text
error CS1061: 'LightRAG' does not contain a definition for 'DeleteDocumentAsync'
```

- [ ] **Step 3: Implement `DeleteDocumentAsync`**

Add `DocumentDeletionService documentDeletionService` to `LightRAG` constructor.

Add method:

```csharp
public async Task<DocumentDeletionResult> DeleteDocumentAsync(
    string docId,
    bool deleteLlmCache = false,
    CancellationToken cancellationToken = default)
{
    var plan = await documentLifecycleService.CreateDeletionPlanAsync(
        "_",
        docId,
        deleteLlmCache,
        cancellationToken);

    if (!plan.Found)
    {
        return new DocumentDeletionResult(docId, "_", Found: false, Succeeded: false, null, "Document not found");
    }

    PostTaskState(new TaskState
    {
        Stage = TaskStage.DeletingDocument,
        Current = 0,
        Total = 0,
        Description = "Deleting document",
        DocId = docId
    });

    return await documentDeletionService.DeleteAsync(
        new DocumentDeletionRequest(plan.Workspace, docId, plan.ChunkIds, deleteLlmCache),
        cancellationToken);
}
```

Important: replace `"_"` with the actual configured workspace if `DocumentLifecycleService` exposes it or if `CreateDeletionPlanAsync` is called by server with workspace. If not available, add `GetDefaultWorkspace()` to lifecycle service and test it.

Add `TaskStage` values:

```csharp
DeletingDocument,
DeletingGraph,
DeletingVectors,
DeletingMetadata
```

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGLifecycleIntegrationTests
```

Expected GREEN:

```text
Passed
```

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/LightRAG.cs src/LightRAGNet/Models tests/LightRAGNet.Tests/DocumentLifecycle
git commit -m "feat: expose document deletion entry point"
```

---

## Task 5: Background Processor Delete Path

**Spec coverage:** deletion task goes through queue/status path; row removed only after success; failure leaves `DeletionFailed`.

**Files:**

- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`
- Modify: `src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs`
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`

- [ ] **Step 1: Add task status mapping test**

In server tests, create a test that constructs a delete task status event and asserts DB row state:

```csharp
[Fact]
public async Task DeleteTaskFailure_KeepsMarkdownRowAndMarksDeletionFailed()
{
    using var factory = new LightRagServerFactory();
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.MarkdownDocuments.Add(new MarkdownDocument
    {
        Id = 77,
        FileName = "delete.md",
        Content = "content",
        IsInRagSystem = true,
        RagDocumentId = "doc-delete",
        RagStatus = "Deleting"
    });
    await context.SaveChangesAsync();
    var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

    await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
    {
        DocumentId = 77,
        RagDocumentId = "doc-delete",
        OperationType = RagTaskOperationType.DeleteDocument,
        Status = RagTaskStatus.Failed,
        ErrorMessage = "delete failed"
    }), CancellationToken.None);

    var document = await context.MarkdownDocuments.FindAsync(77);
    document.Should().NotBeNull();
    document!.RagStatus.Should().Be("DeletionFailed");
    document.RagErrorMessage.Should().Be("delete failed");
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DeleteTaskFailure
```

Expected RED:

```text
Expected document.RagStatus to be "DeletionFailed", but found "Failed"
```

- [ ] **Step 3: Implement handler mapping**

In `RagTaskStatusChangedHandler.UpdateDatabaseStatusAsync`:

```csharp
if (task.OperationType == RagTaskOperationType.DeleteDocument)
{
    document.RagStatus = task.Status switch
    {
        RagTaskStatus.Pending or RagTaskStatus.Processing => "Deleting",
        RagTaskStatus.Failed => "DeletionFailed",
        RagTaskStatus.Completed => "Deleted",
        _ => task.Status.ToString()
    };
    document.RagErrorMessage = task.ErrorMessage;
    await context.SaveChangesAsync(cancellationToken);
    return;
}
```

Do not remove the row in this handler yet; row/file deletion belongs to processor success path or controller callback to avoid deleting before RAG deletion completes.

- [ ] **Step 4: Update processor**

In `RagTaskProcessorService.ProcessTaskAsync`, branch:

```csharp
if (task.OperationType == RagTaskOperationType.DeleteDocument)
{
    await ProcessDeleteTaskAsync(task, lightRAG, cancellationToken);
    return;
}
```

`ProcessDeleteTaskAsync`:

```csharp
private async Task ProcessDeleteTaskAsync(RagTask task, LightRAG lightRAG, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(task.RagDocumentId))
    {
        throw new InvalidOperationException("Delete task requires RagDocumentId.");
    }

    var result = await lightRAG.DeleteDocumentAsync(
        task.RagDocumentId,
        task.DeleteLlmCache,
        cancellationToken);

    if (!result.Succeeded)
    {
        throw new InvalidOperationException(result.ErrorMessage ?? "Document deletion failed.");
    }

    task.Status = RagTaskStatus.Completed;
    task.CompletedAt = DateTime.UtcNow;
    task.CurrentStage = TaskStage.Completed;
    await taskQueue.UpdateTaskStatusAsync(task.TaskId, RagTaskStatus.Completed, cancellationToken: cancellationToken);
}
```

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DeleteTaskFailure
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagTaskQueueServiceTests
```

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet/Services/TaskQueue src/LightRAGNet.Server/Handlers tests
git commit -m "feat: process deletion task status"
```

---

## Task 6: API Delete Semantics

**Spec coverage:** `DELETE /api/MarkdownDocuments/{id}` returns `204`, `202`, `409`, `404`; indexed delete enqueues task; deletion failed can retry.

**Files:**

- Create: `src/LightRAGNet.Share/Models/MarkdownDocumentDeleteResult.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`

- [ ] **Step 1: Add API tests**

Create `DocumentDeletionApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentDeletionApiTests
{
    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnly_ReturnsNoContent()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument { Id = 1, FileName = "local.md", Content = "content" });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Indexed_ReturnsAcceptedAndMarksDeleting()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 2,
            FileName = "indexed.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-indexed",
            RagStatus = "Completed"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/2?deleteLlmCache=true");
        var result = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result.Should().NotBeNull();
        result!.Accepted.Should().BeTrue();
        result.TaskId.Should().NotBeNullOrWhiteSpace();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await context.MarkdownDocuments.FindAsync(2);
        doc!.RagStatus.Should().Be("Deleting");
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Processing_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 3,
            FileName = "processing.md",
            Content = "content",
            RagStatus = "Processing"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/3");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Deleting_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 4,
            FileName = "deleting.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-deleting",
            RagStatus = "Deleting"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/4");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_DeletionFailed_ReturnsAcceptedAndClearsError()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 5,
            FileName = "retry.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-retry",
            RagStatus = "DeletionFailed",
            RagErrorMessage = "previous failure"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/5");
        var result = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result!.Accepted.Should().BeTrue();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await context.MarkdownDocuments.FindAsync(5);
        doc!.RagStatus.Should().Be("Deleting");
        doc.RagErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Missing_ReturnsNotFound()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DocumentDeletionApiTests
```

Expected RED:

```text
Indexed delete returns BadRequest because current API blocks IsInRagSystem.
Processing delete returns BadRequest or wrong status instead of Conflict.
```

- [ ] **Step 3: Implement API behavior**

In `DeleteMarkdownDocument`:

```csharp
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(typeof(MarkdownDocumentDeleteResult), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public async Task<IActionResult> DeleteMarkdownDocument(
    int id,
    [FromQuery] bool deleteLlmCache = false,
    CancellationToken cancellationToken = default)
```

Behavior:

```csharp
if (document.RagStatus is "Pending" or "Processing" or "Deleting")
{
    return Conflict(new { error = "Document has active RAG task" });
}

if (!document.IsInRagSystem && document.RagStatus != "DeletionFailed")
{
    await DeleteUploadedFileAsync(document);
    context.MarkdownDocuments.Remove(document);
    await context.SaveChangesAsync(cancellationToken);
    return NoContent();
}

if (string.IsNullOrWhiteSpace(document.RagDocumentId))
{
    return Conflict(new { error = "Document is missing RagDocumentId" });
}

var taskId = await taskQueueService.EnqueueDeletionTaskAsync(
    document.Id,
    document.RagDocumentId,
    document.FileUrl ?? document.FileName,
    deleteLlmCache,
    cancellationToken);

if (taskId is null)
{
    return Conflict(new { error = "Document has active RAG task" });
}

document.RagStatus = "Deleting";
document.RagErrorMessage = null;
await context.SaveChangesAsync(cancellationToken);

return Accepted(new MarkdownDocumentDeleteResult
{
    Accepted = true,
    TaskId = taskId,
    Status = "Deleting",
    Message = "Document deletion has been queued."
});
```

Extract uploaded file deletion into a private method so the processor can reuse it later.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DocumentDeletionApiTests
```

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet.Share src/LightRAGNet.Server tests/LightRAGNet.Server.Tests
git commit -m "feat: allow indexed document deletion api"
```

---

## Task 7: Delete Row and Uploaded File After Successful Delete

**Spec coverage:** success removes Markdown row and uploaded file only after RAG deletion succeeds.

**Files:**

- Create: `src/LightRAGNet.Server/Services/MarkdownDocumentDeletionService.cs`
- Modify: `src/LightRAGNet.Server/Program.cs` or DI registration location
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs` only if server-specific dependency can be injected safely; otherwise use handler-based cleanup.
- Test: `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`

- [ ] **Step 1: Add failing success cleanup test**

Add:

```csharp
[Fact]
public async Task DeleteTaskCompleted_RemovesMarkdownRow()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 4,
        FileName = "indexed.md",
        Content = "content",
        IsInRagSystem = true,
        RagDocumentId = "doc-indexed",
        RagStatus = "Deleting"
    });
    using var scope = factory.Services.CreateScope();
    var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

    await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
    {
        DocumentId = 4,
        RagDocumentId = "doc-indexed",
        OperationType = RagTaskOperationType.DeleteDocument,
        Status = RagTaskStatus.Completed
    }), CancellationToken.None);

    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var doc = await context.MarkdownDocuments.FindAsync(4);
    doc.Should().BeNull();
}
```

- [ ] **Step 2: Verify RED**

Expected RED:

```text
Expected doc to be null, but found MarkdownDocument.
```

- [ ] **Step 3: Implement cleanup in handler or service**

Preferred implementation: in `RagTaskStatusChangedHandler.UpdateDatabaseStatusAsync`, for delete completion:

```csharp
if (task.OperationType == RagTaskOperationType.DeleteDocument &&
    task.Status == RagTaskStatus.Completed)
{
    await DeleteUploadedFileIfPresentAsync(document);
    context.MarkdownDocuments.Remove(document);
    await context.SaveChangesAsync(cancellationToken);
    return;
}
```

Keep file deletion best-effort: log warning and still remove DB row if file is already gone.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DeleteTaskCompleted
```

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet.Server tests/LightRAGNet.Server.Tests
git commit -m "feat: remove document row after deletion"
```

---

## Task 8: Blazor API Client and Document List UI

**Spec coverage:** indexed delete button visible, deletion confirmation differs by state, `202` keeps row as deleting, failure allows retry.

**Files:**

- Modify: `src/LightRAGNet.Web/ApiClient.cs`
- Create: `src/LightRAGNet.Web/MarkdownDocumentDeleteClientResult.cs`
- Modify: `src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor`

- [ ] **Step 1: Change API client result**

No production code before a test if an API-client test harness exists. If no web test project exists, use build verification and keep the change minimal.

Replace `DeleteMarkdownDocumentAsync` bool result with:

```csharp
public async Task<MarkdownDocumentDeleteClientResult> DeleteMarkdownDocumentAsync(
    int id,
    bool deleteLlmCache = false,
    CancellationToken cancellationToken = default)
{
    var url = $"api/MarkdownDocuments/{id}?deleteLlmCache={deleteLlmCache.ToString().ToLowerInvariant()}";
    var response = await httpClient.DeleteAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
    {
        return new MarkdownDocumentDeleteClientResult { Succeeded = true, DeletedImmediately = true };
    }

    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
    {
        var body = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>(cancellationToken: cancellationToken);
        return new MarkdownDocumentDeleteClientResult
        {
            Succeeded = true,
            Accepted = true,
            TaskId = body?.TaskId
        };
    }

    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        return new MarkdownDocumentDeleteClientResult
        {
            Succeeded = false,
            Conflict = true,
            ErrorMessage = await response.Content.ReadAsStringAsync(cancellationToken)
        };
    }

    return new MarkdownDocumentDeleteClientResult
    {
        Succeeded = false,
        ErrorMessage = await response.Content.ReadAsStringAsync(cancellationToken)
    };
}
```

- [ ] **Step 2: Update UI conditions**

Replace delete button hiding condition:

```razor
@if (CanDeleteDocument(context))
{
    <MudTooltip Text="Delete">
        <MudIconButton Color="Color.Error" Icon="@Icons.Material.Filled.Delete"
                       OnClick="@(() => DeleteDocument(context))"
                       Disabled="@IsDocumentBusy(context)"
                       Size="Size.Small" />
    </MudTooltip>
}
```

Add helpers:

```csharp
private static bool IsDocumentBusy(MarkdownDocumentDto document)
{
    return document.RagStatus is "Pending" or "Processing" or "Deleting";
}

private static bool CanDeleteDocument(MarkdownDocumentDto document)
{
    return !IsDocumentBusy(document);
}
```

Change method signature:

```csharp
private async Task DeleteDocument(MarkdownDocumentDto document)
```

Confirmation message:

```csharp
var message = document.IsInRagSystem || document.RagStatus == "DeletionFailed"
    ? "This will remove the document from RAG storage and then delete the local document record."
    : "Are you sure you want to delete this document?";
```

Handle result:

```csharp
var deleteResult = await ApiClient.DeleteMarkdownDocumentAsync(document.Id);
if (deleteResult.DeletedImmediately)
{
    RemoveDocumentFromCurrentPage(document.Id);
    Snackbar.Add("Document deleted successfully", Severity.Success);
}
else if (deleteResult.Accepted)
{
    document.RagStatus = "Deleting";
    document.RagErrorMessage = null;
    Snackbar.Add("Document deletion queued", Severity.Info);
    StateHasChanged();
}
else if (deleteResult.Conflict)
{
    Snackbar.Add("Document has an active RAG task", Severity.Warning);
}
else
{
    Snackbar.Add($"Failed to delete document: {deleteResult.ErrorMessage}", Severity.Error);
}
```

- [ ] **Step 3: Verify build**

Run:

```powershell
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj
```

Expected GREEN:

```text
Build succeeded.
```

- [ ] **Step 4: Commit**

```powershell
git add src/LightRAGNet.Web src/LightRAGNet.Share
git commit -m "feat: update document deletion ui"
```

---

## Task 9: Clear-All Regression and Full Suite

**Spec coverage:** `clear-all` remains bulk reset, includes `doc_status`, stops tasks first, pushes refresh.

**Files:**

- Modify if needed: `src/LightRAGNet.Storage/KVContracts.cs`
- Modify if needed: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`

- [ ] **Step 1: Add regression test**

Add:

```csharp
[Fact]
public void KVContracts_GetKVStoreNames_IncludesDocStatus()
{
    KVContracts.GetKVStoreNames().Should().Contain(KVContracts.DocStatus);
}
```

If this test does not compile because `DocStatus` is missing, add the constant:

```csharp
public const string DocStatus = "doc_status";
```

and include it in `GetKVStoreNames()`.

- [ ] **Step 2: Verify clear-all behavior**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ClearAll|FullyQualifiedName~KVContracts"
```

Expected GREEN after implementation.

- [ ] **Step 3: Full verification**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
```

Expected:

```text
Passed
Build succeeded.
```

- [ ] **Step 4: Commit**

```powershell
git add src tests
git commit -m "test: cover deletion cleanup boundaries"
```

---

## Task 10: Optional Real Storage Integration

**Spec coverage:** optional Neo4j/Qdrant confidence without slowing normal tests.

Only do this task if Tasks 1-9 are green.

**Files:**

- Create: `tests/LightRAGNet.Tests/Storage/DocumentDeletionStorageIntegrationTests.cs`

- [ ] **Step 1: Add skip gate**

Use this skip pattern:

```csharp
private static bool RunStorageIntegration =>
    Environment.GetEnvironmentVariable("LIGHTRAGNET_RUN_STORAGE_INTEGRATION") == "1";
```

Each test should skip when false.

- [ ] **Step 2: Add Qdrant delete/upsert round-trip**

Test should upsert one vector into `chunks`, delete it, then assert `GetByIdAsync` returns null or no record according to current adapter behavior.

- [ ] **Step 3: Add Neo4j source pruning round-trip**

Test should upsert node/edge with `source_id = "chunk-a<SEP>chunk-b"`, update with pruned `source_id = "chunk-b"`, then assert graph read returns `chunk-b`.

- [ ] **Step 4: Verify optional tests**

Run only when Docker services are available:

```powershell
$env:LIGHTRAGNET_RUN_STORAGE_INTEGRATION='1'
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletionStorageIntegrationTests
```

- [ ] **Step 5: Commit if added**

```powershell
git add tests/LightRAGNet.Tests/Storage
git commit -m "test: add optional deletion storage integration"
```

---

## Final Review Gate

Before claiming implementation complete:

```powershell
dotnet test .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
rg -n "indexed|DeletionFailed|doc_status|source_id|entity_chunks|relation_chunks|llm|clear-all|202|409|Qdrant|Neo4j" docs/superpowers/specs/2026-05-18-document-deletion-parity-design.md docs/superpowers/plans/2026-05-18-document-deletion-parity-implementation-plan.md
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "document deletion parity" --json
```

Manual self-review checklist:

- Every spec requirement in the traceability table has at least one implemented task.
- Every new public method has a failing test recorded before implementation.
- API behavior matches `204`, `202`, `409`, `404` semantics.
- Deletion failure leaves document row visible and retryable.
- Successful indexed deletion removes Markdown row only after RAG storage deletion succeeds.
- Core tests do not require Docker.
- Optional storage integration tests are gated behind `LIGHTRAGNET_RUN_STORAGE_INTEGRATION=1`.
- No unrelated formatting churn.
- No secrets or local credentials committed.

After implementation, run asset compounding:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "document deletion parity" --json
```

If it reports missing archive coverage, write/update the requirement archive before final handoff.
