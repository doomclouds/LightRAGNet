# Server Filesystem Test Parallelism

- Date: `2026-05-19`
- Topic slug: `server-filesystem-test-parallelism`
- Status: `Captured`
- Scope: `Repo`
- Tags: `server-tests`, `xunit`, `uploads`, `parallelism`, `filesystem`

## Symptom

`dotnet test .\LightRAGNet.slnx --no-restore` 稳定失败在 `DocumentDeletionApiTests.DeleteTaskFailure_KeepsMarkdownRowAndUploadedFile`，断言上传文件应保留但实际已被删除。单独运行该测试或只运行 `LightRAGNet.Server.Tests` 又能通过。

## Trigger / Context

当 solution 同时运行多个测试程序集、且 xUnit 在 `LightRAGNet.Server.Tests` 内并行执行不同测试类时触发。`DocumentDeletionApiTests` 会在测试输出目录创建 `Uploads/<unique>.md`，同时 `MarkdownDocumentsControllerTests.ClearAllData...` 会调用 `/api/MarkdownDocuments/clear-all`。

## Root Cause

两个测试类共享 `AppDomain.CurrentDomain.BaseDirectory/Uploads`。`clear-all` 的业务语义会删除 Uploads 文件夹内所有文件，包括其它并行测试刚创建、但预期保留的文件。文件名带 GUID 只能避免同名冲突，不能隔离“清空目录”这种共享状态操作。

## Fix

- 新增 `ServerFilesystemTestCollection`。
- 给 `DocumentDeletionApiTests` 和 `MarkdownDocumentsControllerTests` 标注同一个 `[Collection(ServerFilesystemTestCollection.Name)]`。
- 保持其它测试并行能力，只串行化共享 Uploads 目录的测试类。

## Why This Fix

这个修复直接约束共享状态边界，比关闭整个测试程序集并行更小；也比继续生成唯一文件名更有效，因为真正的冲突是目录级清空，不是文件名碰撞。

## Recognition Clues

- 某个文件保留断言只在 solution 全量测试中失败，单测或单程序集运行通过。
- 失败文件位于 `tests/<project>/bin/.../Uploads` 或 `AppDomain.CurrentDomain.BaseDirectory/Uploads`。
- 同一测试程序集里存在会清空 Uploads 目录的 clear-all / cleanup 类测试。
- 测试使用唯一文件名但仍出现文件被删除，说明冲突发生在目录级别。

## Applicability / Non-Applicability

### Applies When

- 多个测试类共享同一个真实文件系统目录。
- 其中一个测试会清空目录、删除 orphan 文件或执行 bulk cleanup。
- 失败只在并行或 solution 级测试中出现。

### Does Not Apply When

- 每个测试 factory 都有独立 ContentRoot、BaseDirectory 或临时 Uploads 根目录。
- 冲突来自数据库、端口或静态内存状态，而不是共享文件夹。
- 业务代码错误地删除了不该删除的文件；这时应修业务路径安全，而不是只串行化测试。

## Related Artifacts

- Archive: [query mode context parity archive](../../archives/2026-05/2026-05-19-query-mode-context-parity-archives.md)
- Related Problems:
  - [document deletion review gaps](./2026-05-18-document-deletion-review-gaps-problem.md)
- Code or Test:
  - [DocumentDeletionApiTests.cs](../../../../tests/LightRAGNet.Server.Tests/DocumentDeletionApiTests.cs)
  - [MarkdownDocumentsControllerTests.cs](../../../../tests/LightRAGNet.Server.Tests/MarkdownDocumentsControllerTests.cs)
  - [ServerFilesystemTestCollection.cs](../../../../tests/LightRAGNet.Server.Tests/ServerFilesystemTestCollection.cs)
