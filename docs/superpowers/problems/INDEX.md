# Superpowers Problem Index

## 2026-05

- [2026-05-19-markdown-documents-debounce-race-problem.md](./2026-05/2026-05-19-markdown-documents-debounce-race-problem.md): Blazor 页面里的任务状态通知 debounce 必须把 CTS 所有权收进可测试 helper，避免并发回调在 await 后释放已被替换的共享 CTS。
- [2026-05-19-mudfileupload-customcontent-picker-problem.md](./2026-05/2026-05-19-mudfileupload-customcontent-picker-problem.md): MudBlazor 9 的 `MudFileUpload.CustomContent` 必须显式调用 `OpenFilePickerAsync()`，旧的 label 触发方式会让文件选择框静默失效。
- [2026-05-19-server-filesystem-test-parallelism-problem.md](./2026-05/2026-05-19-server-filesystem-test-parallelism-problem.md): 共享 `Uploads` 目录的 Server 测试必须串行化，否则 solution 级并行测试会让 clear-all 删除其它用例的保留文件。
- [2026-05-19-task-state-file-replace-lock-problem.md](./2026-05/2026-05-19-task-state-file-replace-lock-problem.md): Windows 上任务状态 JSON 文件替换可能被短暂占用打断，写入应使用唯一临时文件和有界重试。
- [2026-05-18-document-deletion-review-gaps-problem.md](./2026-05/2026-05-18-document-deletion-review-gaps-problem.md): 删除能力完成前必须检查跨文档图关系、bulk clear-all 文件安全和后台任务真实取消这些旁路。
- [2026-05-18-testability-refactor-completion-gaps-problem.md](./2026-05/2026-05-18-testability-refactor-completion-gaps-problem.md): 结构迁移完成门禁必须同时验证 Git tracked 移动、ignored 旧目录残留和 `.slnx` solution folder 视图。
