# MudTable Reload Cancellation Snackbar

- Date: `2026-05-20`
- Topic slug: `mudtable-reload-cancellation-snackbar`
- Status: `Captured`
- Scope: `UI`
- Tags: `Blazor`, `MudTable`, `cancellation`, `snackbar`, `document-list`

## Symptom

用户在上传多个 Markdown 文件后，逐个点击添加到 RAG 时，页面偶发弹出 `Failed to load document list` / `Failed to load document` 一类失败提示，但后端日志没有对应 error。

## Trigger / Context

- `MarkdownDocuments.razor` 的 Add-to-RAG 成功后会调用 `DebouncedReloadServerDataAsync()`。
- 任务状态 SignalR 更新在 `Pending`、`Processing`、`Completed` 等状态间继续推动列表刷新。
- `MudTable.ReloadServerData()` 会取消上一轮还没完成的 `ServerData` 请求。
- 旧请求取消后抛出的 `OperationCanceledException` 落入页面的通用 `catch (Exception ex)`。

## Root Cause

`ServerReload` 没有把 MudTable 主动取消的 reload 作为正常控制流处理。被取消的请求不代表后端接口失败，但原实现会显示 `Failed to load document list: ...`，并清空 `_documents` / `_totalCount`。同时页面 catch 只弹 snackbar，没有组件级日志，所以用户看到 UI 失败提示时，服务端接口日志里没有错误。

## Fix

- 在 `ServerReload` 中增加 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`。
- 取消路径只写 debug 日志，不弹 snackbar，也不清空当前列表。
- 给 `MarkdownDocuments.razor` 注入 `ILogger<MarkdownDocuments>`。
- 对真实的列表/详情加载失败写 warning 日志，详情日志包含 `DocumentId`。
- 增加 source-level 回归测试，锁住取消路径和详情加载失败日志。
- 后续重构把页面刷新收敛到 `RefreshDocumentsAsync(DocumentRefreshReason reason)`：用户操作和 SignalR 先更新本地行状态，只有清空数据、删除完成、当前页耗尽、任务终态这类需要重新取页的场景才统一重载表格。

## Why This Fix

MudTable 的取消是正常的 UI reload 协调行为，不应该被呈现为用户可见错误。把取消路径从通用异常里拆出来，比扩大 debounce 时间或吞掉所有加载异常更准：真正的接口失败仍会弹窗并记录日志，取消的旧请求则安静退出。

## Recognition Clues

- 弹窗提示加载文档或列表失败，但 Server/API 没有 error。
- 发生在批量上传、连续 Add-to-RAG、任务状态高频刷新或分页快速切换时。
- 失败文本里可能包含 request/task cancelled。
- 相关页面使用 `MudTable.ServerData`，并在多处调用 `ReloadServerData()`。

## Applicability / Non-Applicability

### Applies When

- Blazor 页面用 MudTable 的 `ServerData` 加载数据。
- 页面会因为 SignalR、任务进度或用户操作频繁调用 `ReloadServerData()`。
- `ServerData` 方法把 `OperationCanceledException` 落进通用错误提示。

### Does Not Apply When

- 后端接口返回真实 500/400/404，需要按业务错误处理。
- 页面详情加载是用户主动点击不存在的记录；那应优先检查记录是否已删除或链接是否过期。
- SignalR 事件处理器本身抛异常；那属于通知分发隔离问题。

## Related Artifacts

- Spec: [concurrency race governance design](../../specs/2026-05-19-concurrency-race-governance-design.md)
- Plan: [concurrency race governance implementation plan](../../plans/2026-05-19-concurrency-race-governance-implementation-plan.md)
- Archive: [concurrency race governance archive](../../archives/2026-05/2026-05-19-concurrency-race-governance-archives.md)
- Related Problems:
  - [markdown documents debounce race](./2026-05-19-markdown-documents-debounce-race-problem.md)
- Code or Test:
  - [MarkdownDocuments.razor](../../../../src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor)
  - [MarkdownDocumentsSourceTests.cs](../../../../tests/LightRAGNet.Tests/Web/MarkdownDocumentsSourceTests.cs)
