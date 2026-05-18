# Document Deletion Parity Design

- Date: `2026-05-18`
- Topic slug: `document-deletion-parity`
- Status: `Ready for review`
- Scope: `Core + Server + Web`
- Tags: `lightrag-alignment`, `document-deletion`, `background-task`, `tdd`, `storage-consistency`

## Purpose

LightRAGNet should treat document deletion as a first-class lifecycle operation, not as a UI-only metadata delete. This phase completes the deletion half that was intentionally deferred by the document lifecycle alignment work.

The target behavior is Python LightRAG `adelete_by_doc_id` parity for single-document deletion: remove the document, chunks, vectors, graph references, optional LLM cache entries, and retry metadata in a way that is observable, testable, and recoverable after partial failure.

## Product Decision

Deletion of RAG-indexed documents must be allowed. The current server and UI block deletion once `IsInRagSystem` is true; this must change because a real knowledge-base system needs document removal.

Chosen behavior:

- Not indexed: delete the Markdown metadata and uploaded file synchronously, as today.
- Indexed or deletion-failed: enqueue a deletion task and expose progress/status.
- Processing or pending insertion: block deletion until the insertion task is stopped, failed, or completed.
- Deletion failure: keep the Markdown row visible with `RagStatus = DeletionFailed`, preserve `RagDocumentId`, and show the failure message.
- Deletion success: remove the Markdown row and uploaded file after RAG storage deletion succeeds.

## Python Reference Semantics

The C# implementation should align with these Python behaviors:

- `adelete_by_doc_id` collects chunk ids from `doc_status`.
- It removes `full_docs`, `text_chunks`, and chunk vectors.
- It reads document-level entity/relation indexes.
- It prunes graph node and edge `source_id` values.
- It deletes entities/relations with no remaining source chunks.
- It rebuilds or updates entities/relations that still have remaining source chunks.
- It updates or deletes `entity_chunks` and `relation_chunks` tracking records.
- It optionally deletes LLM cache ids from chunk `llm_cache_list`.
- It records failed deletion stages and allows retry.

## Architecture

Add a focused deletion orchestration area:

```text
src/LightRAGNet/Services/DocumentDeletion/
  DocumentDeletionService.cs
  DocumentDeletionRequest.cs
  DocumentDeletionContext.cs
  DocumentDeletionImpact.cs
  DocumentDeletionStage.cs
  DocumentDeletionOptions.cs
  GraphSourceReferenceParser.cs
```

`DocumentDeletionService` owns destructive storage work. `DocumentLifecycleService` remains responsible for `doc_status` state transitions, deletion failure metadata, retry metadata, and final status deletion. `LightRAG.DeleteDocumentAsync` becomes the public RAG entry point and emits deletion progress events.

Server and UI integrate deletion through the existing task/status pattern instead of running long deletion work inside the HTTP request.

## Data Flow

For an indexed document:

1. Server validates the document row and current task state.
2. Server enqueues a deletion task with `DocumentId`, `RagDocumentId`, file path, and `deleteLlmCache`.
3. Background processor calls `LightRAG.DeleteDocumentAsync`.
4. `LightRAG` creates a deletion plan from `doc_status`.
5. `DocumentDeletionService` loads chunks, full entity metadata, full relation metadata, graph nodes/edges, tracking KV records, and optional LLM cache ids.
6. It computes the deletion impact:
   - chunk ids to remove
   - entities to delete
   - entities to update/rebuild
   - relations to delete
   - relations to update/rebuild
   - cache ids to delete
7. It executes deletion in staged order.
8. On success, server removes the Markdown row and uploaded file.
9. On failure, server keeps the row, sets `RagStatus = DeletionFailed`, stores the message, and keeps retry possible.

## Deletion Stage Order

Use explicit stage names so failures are searchable and retryable:

```text
prepare_deletion
collect_chunks
collect_llm_cache
analyze_graph_references
delete_chunk_vectors
delete_text_chunks
delete_graph_relations
delete_graph_entities
update_graph_references
delete_relation_vectors
delete_entity_vectors
update_relation_vectors
update_entity_vectors
delete_relation_tracking
delete_entity_tracking
delete_llm_cache
delete_document_metadata
delete_doc_status
delete_markdown_record
delete_uploaded_file
```

Stages are not all separate methods, but each destructive block records the current stage before executing.
`DocumentDeletionStage` is the shared stage vocabulary for core and server deletion work. The core RAG deletion service executes only core storage stages through `delete_doc_status`; server/API code owns `delete_markdown_record` and `delete_uploaded_file`.

## Storage Semantics

KV stores:

- `full_docs`: delete by `docId`.
- `text_chunks`: delete all chunk ids in the plan.
- `full_entities`: delete by `docId`.
- `full_relations`: delete by `docId`.
- `entity_chunks`: update or delete per entity name.
- `relation_chunks`: update or delete per relation key.
- `llm_cache`: delete collected cache ids only when requested.
- `doc_status`: delete only after all RAG storage deletion succeeds.

Every successful KV mutation must be persisted with `IndexDoneCallbackAsync`, because `JsonKVStore` keeps changes in memory until that callback. Deletion logic must also parse `JsonElement` values produced by a persisted/reloaded `JsonKVStore`.

Vector stores:

- `chunks`: delete chunk ids.
- `entities`: delete vectors for removed entities; upsert rebuilt vectors for retained entities.
- `relationships`: delete vectors for removed relations; upsert rebuilt vectors for retained relations.

Graph store:

- Delete relations that have no remaining source chunks.
- Delete nodes that have no remaining source chunks.
- Keep an entity if any retained relation still references it, even when the entity's own tracking only lists deleted chunks; update its tracking/source ids from the retained relation chunks.
- Update retained nodes/edges with pruned `source_id`.
- Preserve node/edge properties not owned by deletion logic.

The implementation may extend `IGraphStore` with focused batch update/delete helpers if the current interface causes inefficient loops.

## Rebuild Semantics

Python rebuilds retained knowledge from remaining chunks. The .NET implementation should rebuild retained entity/relation vector payloads from existing graph/KV metadata first, because this phase is deletion consistency work, not a full re-extraction pipeline rewrite.

If remaining source chunks are available but graph/KV metadata is insufficient to rebuild a vector payload safely, deletion must fail at a named rebuild stage rather than silently leaving stale vectors.

## Background Task Model

Extend the task model to support operation type:

```text
IndexDocument
DeleteDocument
```

Deletion tasks use the same persistence and SignalR update path as indexing tasks. The queue must reject a delete task when the same document has a pending or processing index task. It must also reject a second delete task while deletion is pending or processing.

Task statuses remain compact:

```text
Pending
Processing
Completed
Failed
```

Document `RagStatus` exposes the user-facing state:

```text
Pending
Processing
Completed
Failed
Deleting
DeletionFailed
```

## API Behavior

`DELETE /api/MarkdownDocuments/{id}` changes behavior:

- Not indexed: returns `204 NoContent` after local delete.
- Indexed: enqueues deletion and returns `202 Accepted` with task info.
- Deletion failed: enqueues a retry deletion and returns `202 Accepted`.
- Pending/processing insertion or deletion already in progress: returns `409 Conflict`.
- Missing row: returns `404 NotFound`.

Add an optional request path or query flag for LLM cache cleanup:

```text
DELETE /api/MarkdownDocuments/{id}?deleteLlmCache=true
```

Default is `false`, matching Python.

## UI Behavior

The document list should show delete for indexed documents except while an index/delete task is pending or processing.

Confirmation copy must distinguish:

- local-only delete
- delete from RAG storage and local metadata
- optional cache deletion when supported

On `202 Accepted`, the row stays visible and moves to `Deleting`. On SignalR completion, the row is removed. On failure, the row stays visible with retry available.

## Clear-All Behavior

`clear-all` remains a bulk administrative operation. It can continue dropping stores and collections directly because it intentionally deletes everything.

Required alignment:

- Stop or fail active tasks before clearing.
- Include `doc_status` in KV cleanup.
- Push a refresh event after cleanup.
- Avoid reusing single-document deletion internally, because per-document graph pruning would be slower and unnecessary for full reset.

## Testing Strategy

Use TDD with fast tests first.

Core unit tests:

- deletion plan not found returns not-found without storage calls
- local chunks and vectors are deleted
- entities and relations with only deleted chunks are removed
- shared entities and relations are pruned and updated
- `entity_chunks` and `relation_chunks` are updated or deleted
- LLM cache deletion is skipped by default
- LLM cache ids are collected and deleted when requested
- deletion failure records stage and retry metadata
- cancellation propagates without marking the document as `DeletionFailed`
- retry after graph rebuild failure succeeds
- final `doc_status` delete failure does not create zombie status

Server/API tests:

- indexed document delete returns `202 Accepted`
- local-only document delete returns `204 NoContent`
- active indexing document delete returns `409 Conflict`
- deletion failure keeps the Markdown row visible
- deletion success removes the row and file

Task queue tests:

- operation type persists
- delete and index for the same document cannot run concurrently
- retry deletion works after failure

Integration tests:

- default test suite uses fake KV/vector/graph stores.
- optional Neo4j/Qdrant integration tests can be added behind an explicit trait or environment flag and should not run in normal `dotnet test` unless enabled.

## Migration and Compatibility

No EF migration is required if deletion state can use existing `RagStatus`, `RagErrorMessage`, `RagProgress`, and `RagDocumentId` fields.

If task operation type cannot be represented safely in the current persisted task JSON, add a nullable `OperationType` property with default `IndexDocument` for existing task files.

Existing documents with `IsInRagSystem = true` and `RagStatus = Completed` become deletable after this feature.

## Out of Scope

- Multi-document batch deletion parity.
- Python pipeline busy/shared status semantics beyond same-document concurrency protection.
- Full LLM re-extraction during graph rebuild.
- Redesigning the document table UI.
- Replacing the current task queue infrastructure.
- Making `clear-all` transactional across SQLite, Qdrant, Neo4j, and KV stores.

## Acceptance Criteria

- Indexed documents can be deleted through API and UI.
- Deletion removes document chunks, vectors, graph references, graph tracking metadata, full-doc metadata, and lifecycle status.
- Shared graph entities/relations survive with deleted chunk ids pruned.
- Optional LLM cache deletion behaves like Python: disabled by default, enabled by request.
- Partial deletion failure records a precise stage and leaves a retryable visible row.
- Normal test suite covers core deletion semantics without Docker.
- Optional real storage tests are isolated from normal test runs.
- Existing insert lifecycle tests remain green.
