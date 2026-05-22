# ManagedCode MarkItDown PDF/Word Document Intake

- Date: `2026-05-22`
- Topic slug: `managedcode-markitdown-document-intake`
- Status: `Archived`
- Scope: `Feature`
- Tags: `document-intake`, `managedcode-markitdown`, `pdf`, `docx`, `offline-conversion`, `add-to-rag`

## Summary

本需求把 LightRAGNet 的文档上传从 Markdown/text 扩展到 PDF 和 DOCX，同时保留产品主语义：上传只保存原始文件并显示原始文件名，真正进入 RAG 必须由用户点击 `Add to RAG` 触发。实现采用本地 `ManagedCode.MarkItDown` 转换，保存 `converted.md` 长期 artifact，再把转换后的 Markdown 交给既有 RAG indexing 队列。

## Delivered Scope

- 支持 Web 和 API 上传 `.pdf` / `.docx`，保存 `documents/{documentId}/original.pdf|docx`、原始文件 hash、content type 和转换状态元数据。
- `Add to RAG` 对 PDF/DOCX 进入 conversion queue；Markdown/text 仍走既有直接 RAG enqueue 路径。
- 增加本地 converter adapter、artifact store、conversion processor 和 hosted worker，转换成功后写入 `converted.md` 并立即 handoff 到现有 RAG task queue。
- 补齐 conversion retry、cancel、delete、clear-all 和 Web batch upload 行为，避免 upload 阶段自动转换或自动加入 RAG。

## Out of Scope

- 不支持 `.doc`、`.pptx`、`.xlsx`、图片 OCR、扫描版 PDF OCR、URL 抓取、目录扫描或云 provider。
- 不把 Python MarkItDown CLI、`markitdown` executable、Azure/OpenAI/Google/AWS 服务作为运行时依赖。
- 不改变现有 RAG indexing pipeline 的 chunking、embedding、graph merge 或 query 语义。

## Verification Snapshot

- Targeted server verification: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentIntakePipelineApiTests|FullyQualifiedName~DocumentArtifactStoreTests|FullyQualifiedName~DocumentConversionProcessorTests|FullyQualifiedName~ManagedCodeDocumentMarkdownConverterTests" --no-restore --verbosity minimal` passed `87/87`.
- Targeted Web verification: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownUpload|FullyQualifiedName~MarkdownDocumentsSourceTests" --no-restore --verbosity minimal` passed `17/17`.
- Diff hygiene: `git diff --check` passed with no output.
- Full solution verification was attempted with `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`; it still fails in the pre-existing `Neo4jGraphStoreSourceTests.GetPopularLabelsAsync_FiltersUnwoundLabelsAfterWithClause` source-string assertion, outside this feature path.

## Source Documents

- Spec: [ManagedCode MarkItDown PDF/Word Document Intake Design](../../specs/2026-05-22-markitdown-document-intake-design.md)
- Visual: None found for this topic.
- Plan: [ManagedCode MarkItDown Document Intake Implementation Plan](../../plans/2026-05-22-managedcode-markitdown-document-intake-implementation-plan.md)

## Related Problems

- [Document deletion review gaps](../../problems/2026-05/2026-05-18-document-deletion-review-gaps-problem.md)
- [Document task recovery state drift](../../problems/2026-05/2026-05-21-document-task-recovery-state-drift-problem.md)

## Notes

- Code review hardening focused on conversion handoff idempotency, conversion-only cancel races, best-effort artifact cleanup, and Web batch upload track consistency.
