# Superpowers Problem Index

## 2026-05

- [2026-05-24-graph-workbench-sigma-canvas-background-problem.md](./2026-05/2026-05-24-graph-workbench-sigma-canvas-background-problem.md): React Sigma 多层 canvas 不能逐层涂背景色；暗色底色应放在容器上，canvas 保持透明，否则上层交互层会盖住节点和边。
- [2026-05-24-json-kv-delete-flush-problem.md](./2026-05/2026-05-24-json-kv-delete-flush-problem.md): `JsonKVStore.DeleteAsync` 只改内存，用户可见删除必须跟随 `IndexDoneCallbackAsync` 并用真实 JSON round-trip 测试防止重启后数据复活。
- [2026-05-22-graph-workbench-camera-focus-coordinate-race-problem.md](./2026-05/2026-05-22-graph-workbench-camera-focus-coordinate-race-problem.md): React Sigma 选中节点后必须等 refresh 完成再读取 display coordinates，否则相机会拿原始 graph 坐标跳出视野，表现为搜索结果点击后图谱消失。
- [2026-05-22-graph-workbench-sigma-settings-instability-problem.md](./2026-05/2026-05-22-graph-workbench-sigma-settings-instability-problem.md): React Sigma renderer settings 必须保持稳定引用，避免 program class 在 render 中重建导致 Sigma 实例反复销毁、WebGL context 被清理并出现知识图谱白屏。
- [2026-05-22-neo4j-labels-unwind-filter-problem.md](./2026-05/2026-05-22-neo4j-labels-unwind-filter-problem.md): Neo4j `UNWIND labels(n)` 后过滤 workspace label 前必须先 `WITH label`，否则 `/api/graph/labels` 会因 Cypher parse error 返回 500。
- [2026-05-21-di-constructor-activation-boundary-problem.md](./2026-05/2026-05-21-di-constructor-activation-boundary-problem.md): internal 构造器、`InternalsVisibleTo` 和默认 DI 类型激活不是同一件事；迁移 coordinator 依赖时不要用 `object` 桥接隐藏编译期遗漏，应使用强类型构造器、Hosting factory 和 ServiceProvider 回归测试。
- [2026-05-21-document-task-recovery-state-drift-problem.md](./2026-05/2026-05-21-document-task-recovery-state-drift-problem.md): 文档 task 的 shutdown/restart 恢复、terminal snapshot 和 document-id active lookup 必须区分历史终态与当前活跃任务，避免中断任务回到 `Pending` 或过期终态污染状态判断。
- [2026-05-20-mudtable-reload-cancellation-snackbar-problem.md](./2026-05/2026-05-20-mudtable-reload-cancellation-snackbar-problem.md): MudTable 主动取消旧的 ServerData reload 不应弹成文档加载失败；取消路径要单独处理并保留真实失败日志。
- [2026-05-20-server-tests-real-rag-storage-isolation-problem.md](./2026-05/2026-05-20-server-tests-real-rag-storage-isolation-problem.md): Server/API 测试必须隔离真实 Qdrant/Neo4j 和后台任务；clear-all 这类清库接口只能走可替换的测试清理器，不能继承本机开发库。
- [2026-05-19-markdown-documents-debounce-race-problem.md](./2026-05/2026-05-19-markdown-documents-debounce-race-problem.md): Blazor 页面里的任务状态通知 debounce 必须把 CTS 所有权收进可测试 helper，避免并发回调在 await 后释放已被替换的共享 CTS。
- [2026-05-19-mudfileupload-customcontent-picker-problem.md](./2026-05/2026-05-19-mudfileupload-customcontent-picker-problem.md): MudBlazor 9 的 `MudFileUpload.CustomContent` 必须显式调用 `OpenFilePickerAsync()`，旧的 label 触发方式会让文件选择框静默失效。
- [2026-05-19-server-filesystem-test-parallelism-problem.md](./2026-05/2026-05-19-server-filesystem-test-parallelism-problem.md): 共享 `Uploads` 目录的 Server 测试必须串行化，否则 solution 级并行测试会让 clear-all 删除其它用例的保留文件。
- [2026-05-19-task-state-file-replace-lock-problem.md](./2026-05/2026-05-19-task-state-file-replace-lock-problem.md): Windows 上任务状态 JSON 文件替换可能被短暂占用打断，写入应使用唯一临时文件和有界重试。
- [2026-05-18-document-deletion-review-gaps-problem.md](./2026-05/2026-05-18-document-deletion-review-gaps-problem.md): 删除能力完成前必须检查跨文档图关系、bulk clear-all 文件安全和后台任务真实取消这些旁路。
- [2026-05-18-testability-refactor-completion-gaps-problem.md](./2026-05/2026-05-18-testability-refactor-completion-gaps-problem.md): 结构迁移完成门禁必须同时验证 Git tracked 移动、ignored 旧目录残留和 `.slnx` solution folder 视图。
