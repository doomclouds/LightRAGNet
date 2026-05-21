# Query Data Debug Panel

- Date: `2026-05-21`
- Topic slug: `query-data-debug-panel`
- Status: `Archived`
- Scope: `Feature`
- Tags: `query-data`, `retrieval-debugging`, `blazor-chat`, `server-api`, `lightrag-alignment`, `diagnostics`

## Summary

本轮交付为 RAG Chat 增加了消息级检索数据排障入口：每条符合条件的 assistant 回复可以按需打开“查看检索数据”，使用该回复生成时保存的请求快照调用独立 JSON endpoint，查看 entities、relationships、chunks、references、metadata 和 raw JSON。正常聊天仍走既有 SSE `/api/RagQuery/query`，raw retrieval data 只在用户主动 inspection 时查询。

## Delivered Scope

- 新增共享 `RagQueryDataResponse` 合同和 `POST /api/RagQuery/data`，后端强制 retrieval-data 模式并从 `QueryResult.RawData` 拆出 `data` 与 `metadata`。
- `RagQueryRequestMapper.ForceRetrievalDataRequest` 保留 mode/topK/chunkTopK/rerank/keywords 等检索选项，同时强制 `Stream=false`、`IncludeReferences=true`、`OnlyNeedContext=true`、`OnlyNeedPrompt=false`。
- RagChat assistant message 保存 cloned request snapshot，按钮点击时使用 `message.RetrievalDataRequest`，不受当前 toolbar 设置漂移影响。
- 新增 `ApiClient.GetRagQueryDataAsync`、`RagQueryDataDialog.razor` 和 `查看检索数据` 消息级按钮；Bypass 消息不显示按钮。
- Dialog 以 tabs/pre JSON 形式展示 Entities、Relationships、Chunks、References、Metadata 和 Raw JSON，并对长 JSON/URL 做 wrapping 与 overflow 控制。
- 后端错误响应不暴露 raw exception text；详细异常只写日志，客户端收到通用 `Error retrieving query data.`。

## Out of Scope

- 不把 raw retrieval data 自动附加到每次 SSE chat response。
- 不持久化 raw data、不新增长期审计表、不实现批量评测、retrieval diff、下载或图谱可视化。
- 不迁移 Chat 到 React，不修改 GraphView、SigmaGraph、KnowledgeGraphMerge 或 `/api/graph/*`。
- 不改变 query ranking、rerank、context builder、prompt、cache key 或 storage 行为。
- Dialog 内 loading/cancel/retry 和更完整的请求参数 header 属于后续 UX 增强；本次实现采用按钮 loading 禁用、请求结束后打开结果/错误 dialog 的计划内流程。

## Verification Snapshot

- TDD task flow covered shared contract, server endpoint, request snapshot clone, ApiClient method, dialog rendering, and RagChat source wiring with RED/GREEN evidence per task.
- Server focused verification: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RagQueryControllerTests|FullyQualifiedName~RagQueryControllerSourceTests|FullyQualifiedName~RagQueryRequestMapperTests" --verbosity minimal` passed 15/15 after the safe-error-response fix.
- Web focused verification: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ApiClientQueryRagTests|FullyQualifiedName~ChatMessageModelTests|FullyQualifiedName~ChatQuerySettingsModelTests|FullyQualifiedName~RagChatSourceTests" --verbosity minimal` passed 24/24.
- Full solution verification after final fix: `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` passed with `LightRAGNet.Tests` 355/355, `LightRAGNet.Server.Tests` 75/75, and `LightRAGNet.Web.Tests` 25/25.
- Full build verification after final fix: `dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal` passed with 0 warnings and 0 errors.
- Final diff scope check confirmed no GraphView, SigmaGraph, KnowledgeGraphMerge, React graph workbench, or `/api/graph/*` files changed.

## Source Documents

- Spec: [query data debug panel design](../../specs/2026-05-21-query-data-debug-panel-design.md)
- Visual: None found for this topic.
- Plan: [query data debug panel implementation plan](../../plans/2026-05-21-query-data-debug-panel-implementation-plan.md)

## Related Problems

- None at archive time.

## Notes

- `MudCodeBlock` was not recognized cleanly by the current MudBlazor/Razor build, so the dialog uses Razor-encoded `<pre>` blocks with explicit wrapping/overflow styles.
- The implementation deliberately stays separate from the concurrent graph curation / React workbench line to reduce merge conflict risk.
