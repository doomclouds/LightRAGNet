# Task State File Replace Lock

- Date: `2026-05-19`
- Topic slug: `task-state-file-replace-lock`
- Status: `Captured`
- Scope: `Environment`
- Tags: `Windows`, `task-state`, `file-io`, `race-condition`

## Symptom

任务状态仍然能继续推送到前端，但服务端日志出现任务状态持久化失败：

```text
fail: LightRAGNet.Services.TaskQueue.RagTaskStateStore[0]
      Failed to save task state to file
      System.UnauthorizedAccessException: Access to the path is denied.
         at System.IO.FileSystem.MoveFile(...)
         at LightRAGNet.Services.TaskQueue.RagTaskStateStore.SaveToFileAsync(...)
```

同一 failure family 也可能表现为测试临时目录清理失败：

```text
System.IO.IOException: The process cannot access the file 'tasks.json' because it is being used by another process.
   at System.IO.FileSystem.RemoveDirectoryRecursive(...)
   at LightRAGNet.Tests.TaskQueue.RagTaskStateStoreTests.TempDirectoryCleanup.DisposeAsync(...)
```

RAGAS evaluation 测试里也出现过同族症状：

```text
System.IO.IOException: The process cannot access the file 'ragas_runs.json' because it is being used by another process.
   at System.IO.FileSystem.RemoveDirectoryRecursive(...)
   at LightRAGNet.Server.Tests.Evaluation.RagasEvaluationRunCoordinatorTests.Dispose()
```

## Trigger / Context

- RAG 任务处理过程中频繁更新状态和进度，例如 `MergingRelations` 阶段。
- `RagTaskStateStore` 将内存中的任务状态保存到 `tasks.json`。
- Windows 上目标 `tasks.json` 可能被另一个运行实例、短暂读取者、文件扫描器或其它文件系统观察者占用。
- 原实现每次都写固定临时文件 `tasks.json.tmp`，随后直接 `File.Move(temp, tasks.json, overwrite: true)`。
- `RagTaskStateStore` 构造函数会启动 fire-and-forget startup load；如果测试很快写入并清理临时目录，后台 load 可能仍在读同一个 `tasks.json`。

## Root Cause

`File.Move(..., overwrite: true)` 在 Windows 上替换目标文件时，如果目标文件被短暂打开且不允许删除/写入，会抛出 `UnauthorizedAccessException` 或 `IOException`。原实现没有重试，也使用固定临时文件名；一旦目标文件或临时文件被短锁，就会立即把任务状态保存流程打失败。

实例内的 `_fileLock` 只能串行化同一个 `RagTaskStateStore` 实例，不能防住另一个进程、另一个 Store 实例或外部文件系统观察者对目标文件的短暂占用。

后续测试清理失败的直接根因是构造函数中的 startup load 被 fire-and-forget 丢弃，测试没有可等待的 store 生命周期。即使 `SaveTaskStateAsync` 已经 await，构造时启动的 `LoadAllTasksAsync` 仍可能在测试结尾短暂持有 `tasks.json` 读句柄，Windows 递归删除目录因此失败。

`ragas_runs.json` 的变体根因相同：测试只断言 `CreateAsync` 返回 queued，没有等待后台 evaluation runner 消费 evaluator、取消 run 并完成取消路径。`Dispose` 随即递归删除临时目录时，后台 runner 或 run store 仍可能短暂读写 `ragas_runs.json`。

## Fix

- 写入时改用唯一临时文件名：`tasks.json.<guid>.tmp`。
- 替换目标文件时对 `UnauthorizedAccessException` 和 `IOException` 做短间隔重试。
- 每次重试前检查 cancellation token。
- 替换成功或失败后清理本次生成的临时文件。
- 增加回归测试：先锁住 `tasks.json`，启动保存任务，短暂延迟后释放锁，验证保存会等待并最终成功。
- Store 实现 `IAsyncDisposable`，保存 startup load task，并在 `DisposeAsync` 中等待启动加载完成后再释放 `_fileLock`。
- `RagTaskStateStoreTests` 使用 `await using var store = ...`，确保临时目录清理前没有该 store 自己遗留的后台读取。
- RAGAS coordinator 测试使用可控的 `BlockingRagasEvaluator`，等待 evaluator 被调用，显式 `CancelAsync`，再等待取消完成后才允许测试对象 `Dispose` 删除临时目录。

## Why This Fix

这不是权限配置错误，而是 Windows 文件替换语义下的短暂占用问题。直接放宽权限或吞掉异常都会掩盖任务状态丢失风险；唯一临时文件加有界重试既保留原子的“先写临时文件、再替换目标文件”策略，又能穿过短锁窗口。

测试清理场景也不应该靠 `Task.Delay` 或给 `Directory.Delete` 重试硬躲，因为真正缺的是 store 生命周期边界。让 store 可异步释放，可以把构造期后台加载纳入宿主/测试的生命周期管理，同时保持现有 startup load 语义。

## Recognition Clues

- 日志中保存状态失败，但任务处理和前端状态推送仍继续。
- 异常堆栈停在 `FileSystem.MoveFile` 或 `File.Move(... overwrite: true)`。
- 失败发生在频繁进度更新、清空数据、重启多个本地服务实例或杀软/同步工具扫描目录时。
- 同一方法被实例内锁保护，但仍出现文件替换级别的访问拒绝。
- 堆栈停在测试 `TempDirectoryCleanup.DisposeAsync` / `Directory.Delete(..., recursive: true)`，目标文件是 `tasks.json`，且测试刚创建过 `RagTaskStateStore`。
- 堆栈停在测试 `Dispose` / `Directory.Delete(..., recursive: true)`，目标文件是 `ragas_runs.json`，且测试刚启动过后台 evaluation run 但没有等待取消或完成。
- 单独重跑同一测试可能通过，循环也可能不复现，因为后台读取窗口很短。

## Applicability / Non-Applicability

### Applies When

- Windows 上用临时文件替换 JSON 状态文件。
- 目标文件可能被短暂读取或扫描。
- 同一应用可能存在多个运行实例指向同一个工作目录。
- 异常类型是文件替换阶段的 `UnauthorizedAccessException` 或 `IOException`。

### Does Not Apply When

- 目录本身没有写权限；那需要修配置或目录 ACL。
- 路径指向目录而不是文件；那是路径配置错误。
- 需要跨进程强一致合并多个 Store 的内存状态；本修复只保证短锁下保存不立即失败，不解决多实例最后写入者覆盖问题。
- 外部进程、杀软或编辑器仍长期持有 `tasks.json`；`DisposeAsync` 只保证当前 store 自己的 startup load 不再悬空。

## Related Artifacts

- Spec: `None yet.`
- Plan: [2026-05-17-testability-foundation-implementation-plan.md](../../plans/2026-05-17-testability-foundation-implementation-plan.md)
- Archive: [2026-05-18-document-deletion-parity-archives.md](../../archives/2026-05/2026-05-18-document-deletion-parity-archives.md)
- Related Problems:
  - [2026-05-19-server-filesystem-test-parallelism-problem.md](./2026-05-19-server-filesystem-test-parallelism-problem.md)
- Code or Test:
- [RagTaskStateStore.cs](../../../../src/LightRAGNet/Services/TaskQueue/RagTaskStateStore.cs)
- [RagTaskStateStoreTests.cs](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskStateStoreTests.cs)
  - [RagasEvaluationRunCoordinatorTests.cs](../../../../tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunCoordinatorTests.cs)
