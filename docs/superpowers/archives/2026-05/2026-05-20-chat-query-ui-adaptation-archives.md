# Chat Query UI Adaptation

- Date: `2026-05-20`
- Topic slug: `chat-query-ui-adaptation`
- Status: `Archived`
- Scope: `UI`
- Tags: `chat-ui`, `rag-query`, `sse`, `query-mode`, `references`, `error-handling`

## Summary

本轮交付把 Chat 从只发送 `{ query }` 的纯流式文本框，升级为一个轻量查询工作台：用户可以选择查询模式、Streaming/Cacheable 输出、References 和高级查询参数；前端和后端共享 `RagQueryRequest`/SSE metadata 合同，assistant 消息能显示模式、输出类型、references、keywords、diagnostics 和可见错误。

## Delivered Scope

- Added shared `RagQueryRequest`, `QueryMetadataEvent`, and `RagQueryReferenceDto` contracts so Web and Server stop relying on anonymous query payloads.
- Added server-side request mapping from `RagQueryRequest` to `QueryParam`, plus metadata SSE emission after query content and before `done`.
- Reworked `ApiClient.QueryRagAsync` to send the full request, preserve streaming `ResponseHeadersRead`, surface `ErrorEvent` as `RagQueryException`, expose metadata callbacks, and stop swallowing broad exceptions.
- Added `ChatMessageModel` metadata fields, `ChatQuerySettingsModel`, and RagChat controls for mode, output type, references, RAG parameters, keywords, and mutually exclusive debug output.
- Added `LightRAGNet.Web.Tests` with behavior coverage for ApiClient streaming, cancellation, request body shape, chat query settings, metadata application, and chat source guards.

## Out of Scope

- Persistent chat sessions, multi-session chat history, prompt template editing, cache management UI, cache key inspection, and raw JSON viewer remain outside this slice.
- Core query ranking, answer cache key semantics, workspace revision rules, and retrieval algorithms were not changed.
- Full browser visual/manual RAG run-through is still a follow-up validation option rather than a prerequisite for this archive.

## Verification Snapshot

- `dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal`: passed with `0` warnings and `0` errors.
- `dotnet test .\LightRAGNet.slnx --no-restore --no-build --verbosity minimal`: passed with `LightRAGNet.Tests` 255/255, `LightRAGNet.Web.Tests` 17/17, and `LightRAGNet.Server.Tests` 29/29.
- Final review confirmed the empty successful response path is closed: assistant messages now track `IsComplete`, stop showing the spinner after completion, and display `No content returned.` for successful empty results.
- Task-level reviews covered shared contracts, server mapping, Web streaming behavior, chat message model, UI state semantics, and final release blockers.

## Source Documents

- Spec: [chat query UI adaptation design](../../specs/2026-05-20-chat-query-ui-adaptation-design.md)
- Visual: None found for this topic.
- Plan: [chat query UI adaptation implementation plan](../../plans/2026-05-20-chat-query-ui-adaptation-implementation-plan.md)

## Related Problems

- None at archive time.

## Notes

- The prior inbox signal [QueryRagAsync Swallowed Exception](../../inbox/2026-05/2026-05-19-queryragasync-swallowed-exception-inbox.md) was closed by this delivery: `ApiClient.QueryRagAsync` now throws typed stream errors and has runtime Web.Tests coverage.
- `OnlyNeedContext` and `OnlyNeedPrompt` remain public request booleans for core/API compatibility, but RagChat UI now exposes them as one `ChatQueryDebugOutputMode` so users cannot select both in the chat workflow.
