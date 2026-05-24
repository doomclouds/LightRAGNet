# Json KV Delete Flush Problem

- Date: `2026-05-24`
- Topic slug: `json-kv-delete-flush`
- Status: `Captured`
- Scope: `Feature`
- Tags: `json-kv-store`, `cache-management`, `persistence`, `delete`, `testing`

## Symptom

Cache Management 的 clear API 返回成功后，当前进程内库存看起来已经删除，但如果重新加载 `JsonKVStore` 或重启服务，被删除的缓存条目会重新出现。用户看到的是“清理成功”，实际磁盘 JSON 仍保留旧条目。

## Trigger / Context

- 对 `IKVStore` 执行用户可见删除，例如 cache clear、document deletion、status cleanup。
- 底层实现是 `JsonKVStore`。
- 删除路径只调用 `DeleteAsync(ids)`，没有跟随 `IndexDoneCallbackAsync()`。
- 测试 double 只断言内存字典被 remove，没有覆盖重新加载 JSON 文件后的状态。

## Root Cause

`JsonKVStore.DeleteAsync` 只修改内存 `_data`，不会立即写回文件。该 store 的持久化边界是 `IndexDoneCallbackAsync`，它才调用保存逻辑。只代理 `IKVStore.DeleteAsync` 会让内存状态和磁盘状态短期不一致；进程重启后磁盘旧数据重新加载，表现为删除复活。

## Fix

- 在 cache clear 的安全删除入口中先 materialize keys。
- 空 key list 直接返回，避免无意义 flush。
- 非空时调用 `llmCacheStore.DeleteAsync(keys, cancellationToken)`，随后调用 `llmCacheStore.IndexDoneCallbackAsync(cancellationToken)`。
- 测试 double 记录 delete/flush 次数，断言无匹配条目不会触发 flush，真实删除会触发 flush。
- 增加真实 `JsonKVStore` 临时文件 round-trip 测试：删除 old query cache 后重新 new store，确认 old key 不再存在，current key 和 metadata 仍保留。

## Why This Fix

把 flush 放在调用方的删除入口，比修改 `JsonKVStore.DeleteAsync` 更符合现有 `IKVStore` 模式：upsert/delete 负责内存变更，`IndexDoneCallbackAsync` 负责提交。这样不会改变所有 `DeleteAsync` 调用的隐含性能语义，也能和已有 `DocumentDeletionService.DeleteKvRecordsAsync` 的“delete 后立即 callback”模式保持一致。

## Recognition Clues

- 删除接口或清理操作返回成功，但重启后旧 JSON 记录又出现。
- 内存 snapshot / 当前服务内查询看起来正确，重新构造 `JsonKVStore` 后失败。
- 代码路径只出现 `store.DeleteAsync(ids)`，没有紧跟 `store.IndexDoneCallbackAsync(...)`。
- 测试只用 in-memory double，未断言 flush 或磁盘 round-trip。

## Applicability / Non-Applicability

### Applies When

- `JsonKVStore` 作为 `IKVStore` 后端。
- 业务语义要求删除或 upsert 在操作返回前持久化。
- 用户可见的 clear/delete API 声称删除完成。
- 需要验证 JSON 文件重新加载后的真实状态。

### Does Not Apply When

- 底层 store 本身在 `DeleteAsync` 内立即持久化或事务提交。
- 调用方明确采用批量提交策略，并在更高层统一调用 `IndexDoneCallbackAsync`。
- 测试目标只是纯内存 store 行为，不涉及跨重启持久化语义。
- 问题是文件写入被 Windows 短锁打断；那属于文件替换/重试边界。

## Related Artifacts

- Spec: [Cache Management Workbench Design](../../specs/2026-05-24-cache-management-workbench-design.md)
- Plan: [Cache Management Workbench Implementation Plan](../../plans/2026-05-24-cache-management-workbench-implementation-plan.md)
- Archive: [Cache Management Workbench](../../archives/2026-05/2026-05-24-cache-management-workbench-archives.md)
- Related Problems:
  - [Task State File Replace Lock](./2026-05-19-task-state-file-replace-lock-problem.md)
- Code or Test:
  - [CacheEntryInspector.cs](../../../../src/LightRAGNet.Server/Services/CacheManagement/CacheEntryInspector.cs)
  - [CacheManagementServiceTests.cs](../../../../tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs)
  - [JsonKVStore.cs](../../../../src/LightRAGNet.Storage/JsonKVStore.cs)
