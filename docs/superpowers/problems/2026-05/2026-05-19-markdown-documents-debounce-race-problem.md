# Markdown Documents Debounce Race

- Date: `2026-05-19`
- Topic slug: `markdown-documents-debounce-race`
- Status: `Captured`
- Scope: `UI`
- Tags: `Blazor`, `SignalR`, `debounce`, `race-condition`

## Symptom

`RagTaskNotificationService` 调用任务状态更新事件处理器时记录 `NullReferenceException`：

```text
Error calling task status update event handlers: TaskId=...
System.NullReferenceException: Object reference not set to an instance of an object.
   at LightRAGNet.Web.Components.Pages.MarkdownDocuments.DebouncedReloadServerDataAsync()
```

堆栈指向 `MarkdownDocuments.razor` 的 debounce 刷新逻辑，而不是后端任务本身。

## Trigger / Context

- Markdown 文档列表页订阅 `RagTaskNotificationService.TaskStatusUpdated`。
- 删除或 RAG 处理任务快速发送多个状态更新。
- 多个通知回调几乎同时进入 `OnTaskStatusUpdated`，并调用 `DebouncedReloadServerDataAsync()`。
- 页面 dispose、数据清空通知或最终状态刷新也可能和任务状态通知交错触发同一 debounce 路径。

## Root Cause

`MarkdownDocuments.razor` 用共享字段 `_debounceCts` 手写 debounce，但方法中间存在 `await`。一个调用在执行 `await _debounceCts.CancelAsync()` 后恢复时，另一个调用可能已经替换、释放或清空了同一个字段。于是前一调用继续执行 `_debounceCts.Dispose()` 时，字段状态已经不再属于它。

更细的竞态是：即便把 CTS 包进 helper，如果新请求能在旧请求创建 CTS 后、旧请求读取 `Token` 前 dispose 旧 CTS，也会触发 `ObjectDisposedException`。所以修复必须让每次 debounce 调用只操作自己的本地 CTS/token，不能在 await 之后继续依赖共享字段。

## Fix

- 新增 `AsyncDebouncer`，用锁保护当前 CTS 的替换和 dispose 状态。
- 每次 debounce 调用在锁内创建 CTS 并立即捕获 `CancellationToken`，后续等待和 action 都只使用本地 token。
- 新请求取消并释放前一个 CTS；旧请求的 finally 只在自己仍是当前 CTS 时才清理。
- dispose 时标记 debouncer 已关闭、取消当前 CTS，并让后续 debounce 请求安全 no-op。
- `MarkdownDocuments.razor` 改用 `AsyncDebouncer`，不再直接管理共享 `_debounceCts`。
- 增加 `AsyncDebouncerTests` 覆盖并发请求只运行最后一次 action、dispose 取消 pending action 且不抛异常。

## Why This Fix

把修复做成可测试 helper，优于继续在 Razor 私有方法里堆局部 null 判断。根因是异步生命周期所有权不清，单纯写 `_debounceCts?.Dispose()` 只能遮住一条路径，不能解决并发替换、dispose 和 token 获取的竞态。helper 把所有权规则集中起来，并用测试锁住。

## Recognition Clues

- 堆栈在 Blazor 事件回调或 `InvokeAsync` 后进入页面刷新逻辑。
- 异常点在 `CancellationTokenSource.Dispose()`、`CancelAsync()`、`Token` 或字段访问附近。
- 代码在 `await` 之后继续读写共享 CTS 字段。
- SignalR、任务通知、清空数据、页面 dispose 等路径都能触发同一个 debounce/reload 方法。

## Applicability / Non-Applicability

### Applies When

- Blazor 页面通过 SignalR 或后台通知高频刷新 UI。
- 多个异步事件处理器共享同一个 `CancellationTokenSource` 字段。
- debounce/throttle 逻辑需要在 dispose 后安全取消 pending 操作。

### Does Not Apply When

- 异常来自 `MudTable.ReloadServerData()` 内部数据加载失败；那应优先检查 API 调用和 table state。
- CTS 是严格局部变量，没有跨调用共享，也没有在 await 后通过字段再次访问。
- 需要的是业务去重或任务状态幂等，而不是 UI 刷新 debounce。

## Related Artifacts

- Spec: `None yet.`
- Plan: [2026-05-18-document-deletion-parity-implementation-plan.md](../../plans/2026-05-18-document-deletion-parity-implementation-plan.md)
- Archive: [2026-05-18-document-deletion-parity-archives.md](../../archives/2026-05/2026-05-18-document-deletion-parity-archives.md)
- Related Problems:
  - [2026-05-18-document-deletion-review-gaps-problem.md](./2026-05-18-document-deletion-review-gaps-problem.md)
- Code or Test:
  - [MarkdownDocuments.razor](../../../../src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor)
  - [AsyncDebouncer.cs](../../../../src/LightRAGNet/Services/Utilities/AsyncDebouncer.cs)
  - [AsyncDebouncerTests.cs](../../../../tests/LightRAGNet.Tests/Utilities/AsyncDebouncerTests.cs)
