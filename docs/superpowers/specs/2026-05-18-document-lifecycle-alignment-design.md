# Document Lifecycle Alignment Design

- Date: `2026-05-18`
- Topic slug: `document-lifecycle-alignment`
- Status: `Ready for review`
- Scope: `Core`
- Tags: `lightrag-alignment`, `document-lifecycle`, `tdd`, `workspace`

## Purpose

LightRAGNet should align the most important Python LightRAG indexing and data lifecycle behavior before more query features are added. This phase focuses on making document ingestion state, failure recovery, deletion contracts, and workspace isolation explicit and testable.

The goal is not a broad rewrite. The goal is to introduce a small lifecycle core that can be driven by TDD, then connect it to the current .NET insertion flow without requiring real Qdrant, Neo4j, LLM, embedding, or Web UI dependencies.

## Priority Decision

Phase 1 prioritizes indexing and data lifecycle over query mode parity and token-cost controls.

Chosen order:

1. Document lifecycle and data consistency.
2. Token/context/caching controls.
3. Query mode parity such as `Naive` and `Bypass`.

This order keeps the storage truth reliable before later retrieval and cost-control work depends on it.

## Scope

In scope:

- Add a testable document lifecycle model and service.
- Track `pending`, `processing`, `processed`, `failed`, `deleting`, `deleted`, and `deletion_failed` lifecycle states.
- Preserve `chunks_list` and `chunks_count` after chunking succeeds, even if extraction or graph merge fails later.
- Preserve an existing chunk snapshot when failure happens before new chunking succeeds.
- Model deletion plans and deletion failure metadata without implementing full graph rebuild.
- Make workspace an explicit lifecycle and status-store dimension.
- Add fast core tests and a small number of server contract or smoke tests.

Out of scope for this phase:

- Full Python `adelete_by_doc_id` parity.
- Real Qdrant vector deletion.
- Real Neo4j entity/relation source-id pruning.
- LLM cache deletion.
- Graph rebuild after deletion.
- `Naive` and `Bypass` query modes.
- Blazor UI automation.
- Docker/Testcontainers integration tests.

## Python Reference Semantics

This phase ports the lifecycle contracts behind these Python tests rather than translating them line-for-line:

- `test_extract_failure_preserves_chunks_and_allows_delete_with_cache_cleanup`
- `test_extract_failure_before_chunking_preserves_previous_chunk_snapshot`
- `test_merge_failure_preserves_chunks_and_skip_cache_cleanup_when_disabled`
- `test_delete_retry_succeeds_after_rebuild_failure`
- `test_validate_and_fix_consistency_repairs_unknown_file_path_from_full_docs`
- `test_pipeline_cancellation_preserves_file_path_for_queued_docs`

The immediate .NET target is the behavior contract: state is explainable, chunk snapshots survive failures, deletion failures are retryable, and workspace data does not leak.

## Architecture

Add a focused lifecycle area under the core `LightRAGNet` project:

```text
src/LightRAGNet/Services/DocumentLifecycle/
  DocumentLifecycleService.cs
  DocumentLifecycleOptions.cs
  DocumentIngestionResult.cs
  DocumentDeletionPlan.cs
  DocumentDeletionResult.cs
  DocumentStatusRecord.cs
  DocumentLifecycleStatus.cs
  IDocumentStatusStore.cs
```

Responsibilities:

- `DocumentLifecycleService` owns lifecycle rules: document id generation, duplicate detection, status transitions, chunk snapshot recording, failure recording, deletion plan creation, and deletion failure metadata.
- `IDocumentStatusStore` abstracts status persistence and supports workspace-scoped reads and writes.
- `DocumentStatusRecord` mirrors the important Python `doc_status` fields: `DocId`, `Status`, `ContentSummary`, `ContentLength`, `ChunksCount`, `ChunksList`, `FilePath`, `TrackId`, `ErrorMessage`, `Workspace`, `Metadata`, `CreatedAt`, and `UpdatedAt`.
- `DocumentDeletionPlan` describes what should be deleted and what must be preserved for retry.
- `LightRAG.InsertAsync` stays as an orchestrator. It delegates lifecycle decisions to `DocumentLifecycleService`.
- `RagTaskQueueService` remains a queue service and should not become the source of lifecycle truth.

The first production store implementation will adapt the existing `IKVStore` contract unless implementation planning finds a blocking mismatch. Tests use an in-memory fake store.

## Lifecycle State Machine

Target lifecycle:

```text
pending -> processing -> processed
pending -> processing -> failed
failed  -> pending
processed -> deleting -> deleted
processed -> deleting -> deletion_failed
deletion_failed -> deleting -> deleted
```

Required behavior:

- New document ingestion creates `pending` with doc id, file path, track id, content length, a short content summary, workspace, and an empty chunk list.
- Processing starts before expensive chunk, embedding, extraction, or graph work.
- Once chunking succeeds, `chunks_list` and `chunks_count` are recorded immediately.
- Failures after chunking preserve the new chunk snapshot and record the failure stage.
- Failures before chunking do not overwrite a previous failed record's chunk snapshot.
- Deletion failure records `deletion_failed`, `deletion_failure_stage`, and retry metadata.
- Workspace-scoped stores never return another workspace's status record. The default workspace remains `"_"` unless the caller or options supply a different value.

## Testing Strategy

Core lifecycle tests live under:

```text
tests/LightRAGNet.Tests/DocumentLifecycle/
```

Initial test matrix:

- `CreatePending_NewDocument_WritesPendingStatus`
- `StartProcessing_PendingDocument_MarksProcessing`
- `RecordChunks_AfterChunking_PreservesChunkSnapshot`
- `FailAfterChunking_ProcessingDocument_PreservesChunksAndError`
- `FailBeforeChunking_ExistingFailedDocument_PreservesPreviousChunkSnapshot`
- `MarkProcessed_ProcessingDocument_WritesProcessedStatus`
- `DuplicateDocument_SameWorkspace_ReturnsExistingStatus`
- `DuplicateDocument_DifferentWorkspace_AllowsSeparateStatus`
- `CreateDeletionPlan_ProcessedDocument_IncludesKnownChunkIds`
- `MarkDeletionFailed_WhenStageFails_RecordsRetryMetadata`
- `CreateDeletionPlan_AfterDeletionFailure_UsesPreservedChunkSnapshot`
- `CreateDeletionPlan_UnknownDocument_ReturnsNotFound`

LightRAG orchestration characterization tests:

- `InsertAsync_SuccessfulDocument_RecordsProcessedLifecycle`
- `InsertAsync_ChunkProcessingFails_RecordsFailedWithChunkSnapshot`
- `InsertAsync_DuplicateDocument_DoesNotReprocess`

Server tests stay thin:

- `UploadDocument_WhenAccepted_ReturnsTaskAndLifecycleIdentity`
- `DocumentList_WhenDocumentHasLifecycleStatus_ReturnsStatusFields`

If the API surface does not yet expose lifecycle status cleanly, keep the server tests as smoke tests and avoid forcing controller rewrites in this phase.

## Delivery Slices

### Slice 1: Lifecycle Model And Store

Deliver:

- `DocumentLifecycleStatus`
- `DocumentStatusRecord`
- `IDocumentStatusStore`
- In-memory test fake
- Basic `DocumentLifecycleService` transitions

Tests must prove pending, processing, processed, and failed transitions.

### Slice 2: Insert Flow Integration

Deliver:

- `LightRAG.InsertAsync` integration with lifecycle service.
- Chunk snapshot recording immediately after chunking.
- Failure handling that preserves chunk snapshots and file paths.
- Duplicate insertion short-circuit through lifecycle status.

Tests must prove successful insertion, duplicate insertion, and post-chunk failure behavior.

### Slice 3: Workspace Isolation

Deliver:

- Workspace option or equivalent explicit lifecycle input.
- Workspace-aware status store behavior.
- Same-content documents isolated across workspaces.

Tests must prove same-workspace duplicate handling and different-workspace independence.

### Slice 4: Deletion Contract And Thin API Coverage

Deliver:

- `DocumentDeletionPlan`
- `DocumentDeletionResult`
- Deletion failure stage metadata.
- Retry-safe deletion contract.
- Thin server smoke or contract tests.

Tests must prove deletion plan creation, deletion failure recording, retry metadata preservation, and solution-level test stability.

## Verification

The phase is complete only when these commands pass:

```powershell
dotnet restore .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
dotnet test .\LightRAGNet.slnx
```

The final handoff must include the implementation summary, verification output, and an asset-compounding `asset_gate` decision.

## Deferred Phase 2 Work

Phase 2 should implement real deletion and rebuild behavior:

- Qdrant chunk vector deletion.
- Neo4j entity/relation source-id pruning.
- Entity/relation vector cleanup.
- Optional LLM cache cleanup.
- Graph rebuild from remaining chunks.
- Retry after partial deletion failure.
- Python `adelete_by_doc_id` parity tests at a higher integration level.
