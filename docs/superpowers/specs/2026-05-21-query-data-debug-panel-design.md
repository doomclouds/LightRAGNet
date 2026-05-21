# Query Data Debug Panel Design

- Date: `2026-05-21`
- Topic slug: `query-data-debug-panel`
- Status: `Ready for review`
- Scope: `Message-level retrieval data inspection for RAG chat`
- Tags: `lightrag-alignment`, `query-data`, `retrieval-debugging`, `blazor-chat`, `server-api`, `tdd`

## Purpose

LightRAGNet 已经连续补齐 query mode、query cache、KG context builder、rerank chunking 和 document intake pipeline。系统现在能回答问题，但当回答质量不理想时，用户仍然缺少一个直接入口去判断“这次回复到底拿了哪些检索材料”。如果只能看最终答案，排查会变成猜：是关键词抽错、召回不足、rerank 排错、context 被截断，还是 LLM 没组织好答案。

本阶段要补的是一个消息级检索数据透视能力：在每条 AI 回复上提供“查看检索数据”按钮，按需调用 raw retrieval data endpoint，展示本次回复对应的 entities、relationships、chunks、references 和 metadata。它不直接提升答案质量，但让答案质量问题变得可诊断、可比较、可迭代。

这不是把聊天主流程改成调试流程。正常聊天仍然只调用现有 query endpoint；只有用户主动查看某条回复时，才补跑一次数据查询。价值要体现在“本次回复可追溯”，不是给系统多加一个没人用的接口。

## Python Reference Semantics

Python LightRAG 后端已经提供 `/query/data`：

- `LightRAG/lightrag/api/routers/query_routes.py`
  - `/query/data` 被定义为 structured RAG data retrieval endpoint。
  - 它不生成 LLM answer，只返回检索结构数据。
  - 用途包括 data analysis、debugging、research 和 system integration。
  - 返回结构包含 `status`、`message`、`data`、`metadata`。
  - `data` 包含 `entities`、`relationships`、`chunks`、`references`。
  - 该 endpoint 总是包含 references，不受 `include_references=false` 影响。

Python WebUI 的 Retrieval tab 当前主路径仍调用 `/query` 和 `/query/stream`。也就是说，Python 也没有把 `/query/data` 放进每次聊天主请求，而是把它作为后端调试/集成入口。LightRAGNet 应沿用这个边界：聊天回答和检索数据 inspection 分离。

## Current .NET State

当前 .NET 侧已有：

- `RagQueryController` 只有 `POST /api/RagQuery/query`，返回 SSE event stream。
- `RagQueryRequest` 已包含 mode、stream、references、topK、chunkTopK、rerank、keywords、`OnlyNeedContext` 和 `OnlyNeedPrompt`。
- `LightRAG.QueryAsync` 已经能在 KG、Naive、Bypass 模式返回 `QueryResult.RawData`。
- KG raw data 已包含 entities、relationships、chunks、references 和 metadata。
- Naive raw data 已包含 chunks、references 和 metadata。
- `QueryResult.ReferenceList` 能从 raw data 中读取 references。
- Blazor `RagChat.razor` 已在每条 assistant message 上显示 references 和 diagnostics。
- `ChatQuerySettingsModel.BuildRequest` 能构造当前 query 请求。

主要缺口：

- 没有独立 JSON endpoint 返回 raw retrieval data。
- Chat message 没有保存产生该回复时的原始 `RagQueryRequest` 快照。
- UI 上没有消息级入口能按本次回复查看检索数据。
- 当前 diagnostics 只展示扁平 metadata，无法检查 chunks/entities/relationships。

## Product Decision

采用 `message-level inspection` 方案：

- 在每条已完成的 assistant message 上显示 `查看检索数据` 按钮。
- 按钮只对 RAG 检索模式显示，`Bypass` 不显示，因为它没有检索数据。
- 点击按钮时，使用该 assistant message 保存的原始请求快照调用新 endpoint。
- 新 endpoint 返回 JSON 数据，前端在 dialog 中分组展示。
- 正常聊天主流程不自动调用 raw data endpoint。

这个设计的关键点是“本次回复”。用户点开某条历史回复时，看到的必须是这条回复当时对应的 query 参数，而不是当前右侧 toolbar 的最新设置。否则排障会被参数漂移污染。

## User Experience

AI 回复完成后，assistant message 的 metadata 区域提供一个小按钮：

```text
查看检索数据
```

显示规则：

- message role 是 `Assistant`。
- message 已完成。
- message 有保存的 query request。
- request mode 不是 `Bypass`。
- message 不是错误态，或者错误态但仍有可查询的 request 时可以显示；首版建议错误态也显示，便于排查 no-context 或 provider error 前的检索数据。

点击后打开 `Retrieval Data` dialog：

- Header 显示 query、mode、topK、chunkTopK、rerank、stream/cacheable。
- Loading state：展示进度条或 skeleton。
- Error state：展示错误摘要和重试按钮。
- Success state：用 tabs 或 expansion panels 展示：
  - `Entities`
  - `Relationships`
  - `Chunks`
  - `References`
  - `Metadata`
  - `Raw JSON`

首版展示以可读和稳定为先：

- Entities/relationships/chunks 可以先用 table 或 dense list。
- Raw JSON 必须保留，便于复制和排查 provider/storage 细节。
- 不做图谱可视化，不做复杂 diff，不做下载。

## Backend API Contract

新增 endpoint：

```text
POST /api/RagQuery/data
```

Request：复用 `RagQueryRequest`。

Response：

```csharp
public sealed class RagQueryDataResponse
{
    public string Status { get; init; } = "success";
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = [];
}
```

Controller 行为：

- request 为 null 或 query 为空时返回 400。
- 强制构造 non-answer query：
  - `Stream = false`
  - `OnlyNeedContext = true`
  - `OnlyNeedPrompt = false`
  - `IncludeReferences = true`
- 调用 `LightRAG.QueryAsync`。
- 从 `QueryResult.RawData` 中拆出 `data` 和 `metadata`。
- 如果 RawData 为空但 query 成功，返回 `status=success`、空 data 和 metadata，并用 message 说明没有检索数据。
- 如果底层抛业务错误，返回 500 或可映射状态，并保持错误 JSON 结构。

`Bypass` 模式：

- 后端可以返回成功的空 data，metadata 中保留 `query_mode=Bypass`。
- 前端默认不展示按钮，所以此行为主要用于 API 兼容和测试。

## Data Consistency

按钮必须使用 message 上保存的 request snapshot。

发送消息时：

- 调用 `_querySettings.BuildRequest(userMessage)`。
- 将 request clone 保存到 assistant message，例如 `RetrievalDataRequest`。
- clone 必须深拷贝 keyword lists，避免后续 toolbar 修改影响历史消息。

点击按钮时：

- 使用 `assistantMessage.RetrievalDataRequest`。
- 再调用 `/api/RagQuery/data`。
- 不从当前 `_querySettings` 重新构造请求。

首版不把 raw data 持久化到数据库；它可以只存在当前 Blazor session 的 message model 中。若用户刷新页面后历史来自 `ChatHistoryService`，只要 message model 仍保留 request snapshot，就可以重新查询。

## Frontend Architecture

推荐新增/修改：

```text
src/LightRAGNet.Share/Models/
  RagQueryDataResponse.cs

src/LightRAGNet.Web/Models/
  ChatMessageModel.cs
  RagQueryDataViewModel.cs

src/LightRAGNet.Web/Components/Pages/
  RagChat.razor
  RagQueryDataDialog.razor

src/LightRAGNet.Web/
  ApiClient.cs
```

`ApiClient` 新增：

```csharp
Task<RagQueryDataResponse?> GetRagQueryDataAsync(
    RagQueryRequest request,
    CancellationToken cancellationToken = default)
```

`RagChat.razor` 负责：

- 给 assistant message 保存 request snapshot。
- 判断按钮可见性。
- 打开 dialog。
- 调用 ApiClient 获取 retrieval data。
- 将结果传给 dialog 展示。

`RagQueryDataDialog.razor` 负责：

- 展示 loading/error/success 状态。
- 分组呈现 data 和 metadata。
- 提供 raw JSON 面板。

## Conflict Avoidance

本需求刻意避开正在进行的 graph curation / React workbench 重构。

不要修改：

- `src/LightRAGNet.Web/Components/Pages/GraphView.razor`
- `src/LightRAGNet.Web/Components/SigmaGraph.razor`
- `src/LightRAGNet.Web/Components/SigmaGraph.razor.js`
- `src/LightRAGNet/Services/KnowledgeGraphMerge/`
- 任何未来 `/api/graph/*` curation API
- React/Vite graph island 文件

允许修改范围应集中在：

- Query API contract
- Chat message model
- Chat page button/dialog
- Server/Web tests

## Error Handling

后端：

- query 为空返回 400，错误消息与现有 query endpoint 保持一致。
- query 处理失败时返回 JSON error response，不走 SSE。
- RawData shape 不符合预期时返回成功空数据或 failure，需要测试锁定。首版建议成功空数据，message 说明 `No retrieval data was returned.`

前端：

- 点击按钮后禁用该按钮，避免重复请求。
- 网络/API 错误在 dialog 中展示，不污染原回答。
- 对空 data 显示友好提示：`No retrieval data returned for this response.`
- Bypass message 不显示按钮。

## Testing Strategy

Server tests:

- `POST /api/RagQuery/data` rejects null/empty query。
- endpoint forces `Stream=false` and `OnlyNeedContext=true` semantics。
- Mix/KG response returns data and metadata from `QueryResult.RawData`。
- Naive response returns chunks and references。
- Bypass response returns empty data with metadata。
- errors return JSON, not SSE。

Share/model tests:

- `RagQueryDataResponse` serializes Chinese text without escaping when using existing JSON options if applicable。

Web tests:

- `ChatMessageModel` stores a query request snapshot。
- request snapshot deep copies keyword lists。
- `RagChat.razor` contains message-level `查看检索数据` action。
- button visibility excludes Bypass mode。
- `ApiClient.GetRagQueryDataAsync` posts to `api/RagQuery/data`。
- dialog source test verifies tabs/sections for entities、relationships、chunks、references、metadata/raw JSON。

Manual verification:

- Send a Mix query and wait for answer。
- Click `查看检索数据` on the assistant reply。
- Confirm dialog shows chunks and references matching the answer metadata。
- Change toolbar mode/topK after the answer。
- Click the old reply button again and confirm it uses the old request snapshot。
- Send a Bypass query and confirm no retrieval data button appears。

## Acceptance Criteria

- Chat answer remains unchanged for normal use; no raw data request is sent automatically.
- Each eligible assistant reply exposes a `查看检索数据` action after completion.
- The action uses that reply's original request snapshot, not the current toolbar settings.
- `/api/RagQuery/data` returns structured JSON with `status`、`message`、`data`、`metadata`。
- KG/Mix data includes entities、relationships、chunks、references when available。
- Naive data includes chunks and references when available。
- Bypass messages do not show the UI action.
- Retrieval data dialog provides grouped readable sections and raw JSON.
- Implementation does not touch graph curation, GraphView, SigmaGraph, or KnowledgeGraphMerge files.
- Focused Server/Web tests pass, and full solution build remains green.

## Out of Scope

- 不把 raw data 自动附加到每次 SSE response。
- 不把 raw data 存入数据库或长期审计表。
- 不做图谱可视化、retrieval diff、批量评测页面。
- 不迁移 Chat 到 React。
- 不改变 query ranking、rerank、context builder、prompt、cache key 或 storage 行为。
- 不实现 Python `/query` 和 `/query/stream` 的 NDJSON 响应格式；当前 .NET 继续使用 SSE。

## Implementation Planning Notes

推荐后续切片：

1. 新增 `RagQueryDataResponse` 和 `/api/RagQuery/data` contract tests。
2. 接入 controller endpoint，复用 `LightRAG.QueryAsync` 的 `OnlyNeedContext` raw data 路径。
3. 给 `ChatMessageModel` 保存 `RagQueryRequest` snapshot，并补深拷贝测试。
4. 给 `ApiClient` 增加 raw data 方法。
5. 新增 `RagQueryDataDialog.razor` 和 Chat 消息按钮。
6. 补 source-level Web tests 和 focused Server tests。
7. 跑 focused tests、solution tests/build，再做 asset closeout。

核心边界就一句：这不是第二条聊天路径，而是每条回答的检索证据面板。
