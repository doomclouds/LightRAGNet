# Document Deletion Parity Implementation Plan

- Date: `2026-05-18`
- Spec: `docs/superpowers/specs/2026-05-18-document-deletion-parity-design.md`
- Branch/worktree target: `.worktrees/document-deletion-parity`
- Method: TDD first, small commits, subagent-friendly slices

## Goal

Implement single-document deletion parity with Python LightRAG `adelete_by_doc_id`: indexed documents can be deleted through API/UI, deletion runs as a background task, graph/vector/KV references are pruned or removed, optional LLM cache cleanup is retryable, and failure leaves a visible retryable state.

## File Map

Core contracts and storage:

- `src/LightRAGNet.Core/Interfaces/IGraphStore.cs`: add focused update helpers if current `UpsertNodeAsync`, `UpsertEdgeAsync`, `DeleteNodeAsync`, and `RemoveEdgesAsync` are insufficient.
- `src/LightRAGNet.Storage/Neo4jGraphStore.cs`: implement graph helper methods using workspace label and `entity_id`.
- `src/LightRAGNet.Storage/QdrantVectorStore.cs`: verify delete/upsert behavior for `chunks`, `entities`, and `relationships`; fix only if tests expose mismatch.

Deletion core:

- `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionService.cs`: orchestrates destructive RAG storage deletion.
- `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionRequest.cs`: request contract.
- `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionContext.cs`: loaded storage state.
- `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionImpact.cs`: computed delete/update sets.
- `src/LightRAGNet/Services/DocumentDeletion/DocumentDeletionStage.cs`: canonical stage names.
- `src/LightRAGNet/Services/DocumentDeletion/GraphSourceReferenceParser.cs`: parse, prune, and join `source_id` values.
- `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`: add deletion started/succeeded helpers and persisted retry metadata.
- `src/LightRAGNet/LightRAG.cs`: add public `DeleteDocumentAsync`.
- `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`: register deletion service.

Task queue and server:

- `src/LightRAGNet/Models/RagTask.cs`: add operation type and delete cache flag.
- `src/LightRAGNet/Models/TaskState.cs`: add deletion stages.
- `src/LightRAGNet/Services/TaskQueue/IRagTaskQueueService.cs`: add enqueue deletion method and same-document concurrency guard.
- `src/LightRAGNet/Services/TaskQueue/RagTaskQueueService.cs`: persist operation type and guard index/delete conflicts.
- `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`: dispatch index vs delete work.
- `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`: change delete endpoint behavior.
- `src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs`: map deletion task status to document state.

Web:

- `src/LightRAGNet.Share/Models/MarkdownDocumentDeleteResult.cs`: response DTO for `202 Accepted`.
- `src/LightRAGNet.Web/ApiClient.cs`: return delete result instead of bool.
- `src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor`: show delete for indexed docs and handle `202`.

Tests:

- `tests/LightRAGNet.Tests/DocumentDeletion/GraphSourceReferenceParserTests.cs`
- `tests/LightRAGNet.Tests/DocumentDeletion/DocumentDeletionServiceTests.cs`
- `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`
- `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`
- `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`
- `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`
- `tests/LightRAGNet.Tests/TestDoubles/InMemoryKvStore.cs`
- `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
- `tests/LightRAGNet.Tests/TestDoubles/InMemoryGraphStore.cs`

## Execution Setup

Run from repo root:

```powershell
git status --short --branch
git worktree add .worktrees/document-deletion-parity -b feature/document-deletion-parity
```

Expected: clean status except existing intentional ahead commits on `main`; new worktree at `.worktrees/document-deletion-parity`.

All implementation commands below run in:

```powershell
cd C:\WorkSpace\RiderProjects\LightRAGNet\.worktrees\document-deletion-parity
```

## Task 1: Add Deletion Operation Contracts

Red tests:

- In `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`, add:
  - `EnqueueDeletionTaskAsync_WhenIndexTaskPendingForDocument_ReturnsConflict`
  - `EnqueueDeletionTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsConflict`
  - `RagTaskStateStore_OperationTypeMissing_DefaultsToIndexDocument`
- In `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`, add:
  - `MarkDeletionStarted_WhenProcessed_MarksDeletingAndClearsPreviousFailure`
  - `MarkDeletionSucceeded_WhenDeleting_DeletesStatusRecord`
  - `MarkDeletionFailed_WithCacheIds_PreservesRetryMetadata`

Implementation:

- Add `RagTaskOperationType` enum with `IndexDocument` and `DeleteDocument`.
- Add `OperationType`, `DeleteLlmCache`, and `DeleteFilePath` to `RagTask`.
- Add `EnqueueDeletionTaskAsync(int documentId, string ragDocumentId, string filePath, bool deleteLlmCache, CancellationToken)`.
- Add deletion lifecycle helpers:
  - `MarkDeletionStartedAsync(workspace, docId)`
  - `MarkDeletionSucceededAsync(workspace, docId)`
  - overload or extend `MarkDeletionFailedAsync` with `IReadOnlyCollection<string> llmCacheIds`.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RagTaskQueueServiceTests|FullyQualifiedName~DocumentLifecycleServiceTests"
```

Commit:

```powershell
git add src/LightRAGNet/Models src/LightRAGNet/Services/TaskQueue src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests
git commit -m "feat: add deletion task contracts"
```

## Task 2: Add Test Doubles for Destructive Storage

Red tests:

- Create `tests/LightRAGNet.Tests/DocumentDeletion/GraphSourceReferenceParserTests.cs` with:
  - `Prune_RemovesDeletedSourcesAndPreservesOrder`
  - `Prune_WhenNoSourcesRemain_ReturnsEmpty`
  - `NormalizeRelationKey_SortsEndpointsForTracking`
- Create empty failing references to `InMemoryKvStore`, `InMemoryVectorStore`, and `InMemoryGraphStore` in `DocumentDeletionServiceTests`.

Implementation:

- Add `GraphSourceReferenceParser` with:
  - `Split(string? sourceId)`
  - `Join(IEnumerable<string> sourceIds)`
  - `Prune(string? sourceId, ISet<string> deletedChunkIds)`
  - `MakeRelationKey(string sourceId, string targetId)`
- Add in-memory stores supporting `GetByIdAsync`, `GetByIdsAsync`, `UpsertAsync`, `DeleteAsync`, and inspection helpers.
- In-memory graph store should preserve node/edge properties exactly and expose deleted node/edge sets for assertions.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletion
```

Commit:

```powershell
git add src/LightRAGNet/Services/DocumentDeletion tests/LightRAGNet.Tests
git commit -m "test: add deletion storage test doubles"
```

## Task 3: Compute Deletion Impact Before Destructive Writes

Red tests in `DocumentDeletionServiceTests`:

- `AnalyzeAsync_DocumentWithOnlyOwnedEntityAndRelation_MarksThemForDelete`
- `AnalyzeAsync_SharedEntityAndRelation_MarksThemForUpdate`
- `AnalyzeAsync_MissingEntityTrackingFallsBackToGraphSourceId`
- `AnalyzeAsync_WhenLlmCacheDisabled_DoesNotCollectCacheDeletes`
- `AnalyzeAsync_WhenLlmCacheEnabled_CollectsChunkCacheIdsAndMetadataCacheIds`

Implementation:

- Add `DocumentDeletionRequest` fields: `Workspace`, `DocId`, `ChunkIds`, `DeleteLlmCache`.
- Add `DocumentDeletionContext` loaded from:
  - `text_chunks`
  - `full_entities`
  - `full_relations`
  - `entity_chunks`
  - `relation_chunks`
  - graph nodes/edges
- Add `DocumentDeletionImpact` sets:
  - `ChunkIdsToDelete`
  - `EntitiesToDelete`
  - `EntitiesToUpdate`
  - `RelationsToDelete`
  - `RelationsToUpdate`
  - `EntityTrackingToDelete`
  - `EntityTrackingToUpdate`
  - `RelationTrackingToDelete`
  - `RelationTrackingToUpdate`
  - `LlmCacheIdsToDelete`
- Keep analysis pure: tests should assert no delete/upsert calls happen during analysis.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletionServiceTests
```

Commit:

```powershell
git add src/LightRAGNet/Services/DocumentDeletion tests/LightRAGNet.Tests/DocumentDeletion
git commit -m "feat: analyze document deletion impact"
```

## Task 4: Execute Core Deletion and Retry Semantics

Red tests in `DocumentDeletionServiceTests`:

- `DeleteAsync_RemovesChunksVectorsFullDocsAndDocMetadata`
- `DeleteAsync_RemovesOwnedGraphEntitiesRelationsAndVectors`
- `DeleteAsync_PrunesSharedGraphReferencesAndUpsertsVectors`
- `DeleteAsync_DeleteLlmCacheFalse_SkipsCacheStore`
- `DeleteAsync_DeleteLlmCacheTrue_RemovesCacheIds`
- `DeleteAsync_WhenGraphUpdateFails_RecordsFailureStageAndKeepsStatus`
- `DeleteAsync_RetryAfterGraphFailure_UsesPersistedCacheIdsAndSucceeds`
- `DeleteAsync_WhenFinalDocStatusDeleteFails_DoesNotZombieUpsertDeletionFailed`

Implementation:

- `DocumentDeletionService.DeleteAsync` executes stage order from the spec.
- Before each destructive block, set current stage.
- On failure before final status delete, call `MarkDeletionFailedAsync` with stage, message, and collected cache ids.
- On final status delete failure, return failure without re-upserting `DeletionFailed`.
- Use existing `IVectorStore.UpsertAsync` to rebuild retained entity/relation vectors from retained graph properties:
  - entity vector content: entity name + description.
  - relation vector content: source + target + keywords + description.
  - metadata preserves `id`, `entity_name` or relation id, `source_id`, `file_path`, and `created_at` when available.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentDeletionServiceTests
```

Commit:

```powershell
git add src/LightRAGNet/Services/DocumentDeletion src/LightRAGNet/Services/DocumentLifecycle tests/LightRAGNet.Tests/DocumentDeletion
git commit -m "feat: execute retryable document deletion"
```

## Task 5: Add `LightRAG.DeleteDocumentAsync`

Red tests in `LightRAGLifecycleIntegrationTests`:

- `DeleteDocumentAsync_NotFound_ReturnsNotFoundWithoutStorageCalls`
- `DeleteDocumentAsync_ProcessedDocument_DeletesRagStorageAndStatus`
- `DeleteDocumentAsync_DeleteFailure_EmitsDeletionFailedState`

Implementation:

- Inject `DocumentDeletionService` into `LightRAG`.
- Add `DeleteDocumentAsync(string docId, bool deleteLlmCache = false, CancellationToken cancellationToken = default)`.
- Emit task states:
  - `DeletingDocument`
  - `DeletingGraph`
  - `DeletingVectors`
  - `DeletingMetadata`
  - `Completed`
- Keep insert behavior unchanged.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGLifecycleIntegrationTests
```

Commit:

```powershell
git add src/LightRAGNet/LightRAG.cs src/LightRAGNet/Models src/LightRAGNet.Hosting tests/LightRAGNet.Tests/DocumentLifecycle
git commit -m "feat: expose document deletion in lightrag"
```

## Task 6: Route Delete Tasks Through the Background Processor

Red tests in `RagTaskQueueServiceTests` and new processor tests if needed:

- `GetNextTaskAsync_ReturnsDeleteTaskWhenPending`
- `ProcessTaskAsync_DeleteDocument_CallsLightRagDelete`
- `DeleteTaskCompletion_RemovesMarkdownRecordOnlyAfterRagDeleteSuccess`
- `DeleteTaskFailure_KeepsMarkdownRecordAndSetsDeletionFailed`

Implementation:

- `RagTaskProcessorService` switches on `OperationType`.
- Index task path keeps current `InsertAsync` behavior.
- Delete task path calls `DeleteDocumentAsync`.
- Add a server-side completion hook or handler behavior so successful delete removes the Markdown row and uploaded file.
- Failure maps to `RagStatus = DeletionFailed`.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~TaskQueue
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DocumentDeletion
```

Commit:

```powershell
git add src/LightRAGNet/Services/TaskQueue src/LightRAGNet.Server tests
git commit -m "feat: process document deletion tasks"
```

## Task 7: Change API Delete Semantics

Red tests in `tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs`:

- `DeleteMarkdownDocument_LocalOnly_ReturnsNoContent`
- `DeleteMarkdownDocument_Indexed_ReturnsAcceptedAndMarksDeleting`
- `DeleteMarkdownDocument_InsertionPending_ReturnsConflict`
- `DeleteMarkdownDocument_DeletionFailed_ReturnsAcceptedForRetry`

Implementation:

- Add `MarkdownDocumentDeleteResult` shared DTO with `Accepted`, `TaskId`, `Status`, and `Message`.
- `DELETE /api/MarkdownDocuments/{id}`:
  - local-only -> existing synchronous delete -> `204`.
  - indexed/deletion-failed -> enqueue delete -> `202`.
  - active insertion/delete -> `409`.
- Query flag: `deleteLlmCache=false` default.
- Preserve existing upload/add-to-rag API behavior.

Verification:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~DocumentDeletionApiTests
```

Commit:

```powershell
git add src/LightRAGNet.Share src/LightRAGNet.Server tests/LightRAGNet.Server.Tests
git commit -m "feat: allow indexed document deletion api"
```

## Task 8: Update Blazor Document List Behavior

Red/check tests:

- If no component test harness exists, use compile verification plus targeted manual browser check after implementation.
- Add API-client unit coverage if current test project can instantiate `ApiClient` with fake `HttpMessageHandler`.

Implementation:

- `ApiClient.DeleteMarkdownDocumentAsync` returns a result object:
  - `DeletedImmediately`
  - `Accepted`
  - `Conflict`
  - `ErrorMessage`
- `MarkdownDocuments.razor` shows delete for indexed docs unless `RagStatus` is `Pending`, `Processing`, or `Deleting`.
- Confirmation text changes based on `IsInRagSystem`.
- On `202`, keep row and show `Deleting`.
- On completion SignalR refresh removes row.
- On deletion failure, keep row and allow retry.

Verification:

```powershell
dotnet build .\LightRAGNet.slnx
```

Optional manual check after starting server/web:

```powershell
dotnet run --project .\src\LightRAGNet.Server
dotnet run --project .\src\LightRAGNet.Web
```

Commit:

```powershell
git add src/LightRAGNet.Web src/LightRAGNet.Share
git commit -m "feat: update document deletion ui"
```

## Task 9: Align Clear-All and Full Verification

Red tests:

- `ClearAllData_IncludesDocStatusStore`
- `ClearAllData_StopsTasksBeforeDroppingStorage`
- `ClearAllData_PushesDataClearedEvent`

Implementation:

- Ensure `KVContracts.GetKVStoreNames()` includes `doc_status`; if already included, add regression test.
- Keep clear-all full reset path separate from single-document pruning.
- Ensure active tasks are stopped before storage drop.

Verification:

```powershell
dotnet test .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
```

Commit:

```powershell
git add src tests
git commit -m "test: cover deletion cleanup boundaries"
```

## Task 10: Optional Real Storage Integration Gate

Only add these if the fake-store implementation is green and time remains.

Tests must be skipped unless explicitly enabled:

```text
LIGHTRAGNET_RUN_STORAGE_INTEGRATION=1
```

Candidate tests:

- Qdrant delete/upsert round trip for `chunks`, `entities`, `relationships`.
- Neo4j upsert/update/delete node and edge with pruned `source_id`.

Verification:

```powershell
$env:LIGHTRAGNET_RUN_STORAGE_INTEGRATION='1'
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter Category=StorageIntegration
```

Commit only if added:

```powershell
git add tests
git commit -m "test: add optional storage deletion integration"
```

## Final Review and Merge Preparation

Run:

```powershell
dotnet test .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "document deletion parity" --json
```

Manual review checklist:

- No real API keys or machine-only secrets changed.
- `docker-compose.yml` untouched unless integration tests require docs only.
- No unrelated formatter churn.
- `src/LightRAGNet/LightRAG.cs` remains understandable after adding delete path; split helpers if it becomes too large.
- API returns `202` for indexed deletion and `204` for local-only deletion.
- Failed deletion leaves document visible.
- Successful deletion removes Markdown row only after RAG storage deletion succeeds.

Expected final commit if asset archive is written:

```powershell
git add docs/superpowers/archives docs/superpowers/problems docs/superpowers/inbox
git commit -m "docs: archive document deletion parity"
```

