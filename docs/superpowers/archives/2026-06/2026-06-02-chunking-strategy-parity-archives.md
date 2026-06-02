# Chunking Strategy Parity

- Date: `2026-06-02`
- Topic slug: `chunking-strategy-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `chunking`, `python-parity`, `document-processing`, `semantic-vector`, `paragraph-semantic`

## Summary

本次交付将 LightRAGNet 的文档分块从单一固定 token 窗口升级为可配置的 F/R/V/P 策略体系，对齐 Python LightRAG 的固定分块、递归字符分块、语义向量断点分块和段落语义分块能力。默认仍保持 F 策略兼容，索引流程切换到异步分块入口，并在文档生命周期 metadata 中记录本次分块策略与实际 token size。

## Delivered Scope

- 新增 `LightRagChunkingService` 与 `IChunkingStrategy` 策略接口，落地 `FixedToken`、`RecursiveCharacter`、`SemanticVector`、`ParagraphSemantic` 四类策略并接入 DI。
- 保留旧同步 `DocumentProcessingService.ChunkDocument(...)` 兼容层，主索引流程改为 `ChunkDocumentAsync(...)`，支持 V 策略调用 `IEmbeddingService`。
- 为 R/V/P 覆盖大块递归拆分、小块合并、source span、heading metadata、table row split、embedding breakpoint threshold 和 fallback 边界。
- 在 `LightRAG.InsertAsync` 冻结 chunking snapshot，并通过 `DocumentLifecycleService.RecordChunkingMetadataAsync(...)` 写入 `chunking_strategy` 和 `chunk_token_size`。

## Out of Scope

- 未新增 React 策略切换控件。
- 未实现已有文档批量重索引入口；策略切换只影响新索引文档。
- 未完整复刻 Python 的 MinerU、Docling 或 native `.blocks.jsonl` sidecar 管线。
- V 策略只做 semantic breakpoint chunking，不接 Qdrant、不做向量检索。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentProcessing" --no-restore --verbosity minimal`: 64 passed.
- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --verbosity minimal`: 468 passed.
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal`: 301 passed.
- `dotnet test .\LightRAGNet.slnx --verbosity minimal`: LightRAGNet.Tests 468 passed, LightRAGNet.Server.Tests 301 passed.
- `git diff --check`: passed with no whitespace errors.

## Source Documents

- Spec: [2026-06-02-chunking-strategy-parity-design.md](../../specs/2026-06-02-chunking-strategy-parity-design.md)
- Visual: None found for this topic.
- Plan: [2026-06-02-chunking-strategy-parity-implementation-plan.md](../../plans/2026-06-02-chunking-strategy-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- Implementation commit range: `9bcb102..77d2b0b` on branch `feat/chunking-strategy-parity`.
- Follow-up candidates remain UI strategy selection, explicit reindex workflow, and deeper Python paragraph sidecar parity if converted document pipelines start emitting block sidecars.
