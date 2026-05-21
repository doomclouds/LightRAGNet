# Document Intake Pipeline Parity

- Date: `2026-05-21`
- Topic slug: `document-intake-pipeline-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `document-intake`, `pipeline`, `server-api`, `tdd`

## Summary

本轮交付把 Markdown/text 文档接入从同步上传式操作收敛为 Python LightRAG 风格的后台 intake pipeline：提交立即返回 `track_id` 和文档状态，SQLite 文档表成为用户可见状态源，单 worker 顺序处理队列，并提供 track 查询、分页筛选、失败/取消后的重试、排队/处理中取消，以及 Web 文档列表里的基础操作闭环。

## Delivered Scope

- 扩展 `MarkdownDocument`、DTO、EF migration 和 mapper，补齐 `TrackId`、pipeline stage、active task、时间戳和 retry count 等状态字段。
- 新增 `DocumentIntakeService` 与共享 intake models，支持 text 提交、batch upload、track status 聚合、状态筛选、retry、document cancel 和 track cancel。
- 扩展 `IRagTaskQueueService` / `RagTaskQueueService`，支持 task `Cancelled`、文档级取消、失败/取消后的重试，以及 terminal snapshot 和 active task 查询边界。
- 更新 worker recovery 与状态 handler：启动恢复和 host shutdown 中断都落到 `Failed`，要求显式 retry，避免自动重入造成重复索引。
- Web 文档列表增加状态筛选、retry/cancel 操作，并修正 SignalR/筛选刷新语义，避免状态跨过滤条件后页面显示陈旧。
- 补齐 Server API、TaskQueue、Processor 和 Web source-level 回归测试，覆盖提交、track 聚合、分页筛选、retry/cancel、恢复语义和 UI 操作入口。

## Out of Scope

- 未实现 PDF/Office/image/table/formula 或 RAG-Anything 多模态深解析。
- 未引入多 worker、分布式队列、跨进程锁、集群调度或外部数据库作为状态主源。
- 未改变 query pipeline、rerank、retrieval context、prompt 模板或 Python API 路由命名逐字一致性。
- Web 首版不做复杂上传向导、解析预览或实时 pipeline timeline，只保留状态列表和基础操作闭环。

## Verification Snapshot

- Focused Server/API 回归覆盖 `DocumentIntakePipelineApiTests`，验证 text/batch upload、track status、status filter、retry/cancel、active task 绑定和 handler 状态映射。
- Focused TaskQueue/Processor 回归覆盖 queue retry/cancel、terminal snapshot lookup、startup recovery 和 host shutdown interrupted processing。
- Full core tests: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --verbosity minimal` 通过，`354/354`。
- Full server tests: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal` 通过，`66/66`。
- Full solution tests: `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` 通过，`LightRAGNet.Tests 354/354`、`LightRAGNet.Web.Tests 20/20`、`LightRAGNet.Server.Tests 66/66`。
- Final review focused verification: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~TaskQueue --verbosity minimal` 通过，`39/39`；`git diff --check` clean。

## Source Documents

- Spec: [document intake pipeline parity design](../../specs/2026-05-21-document-intake-pipeline-parity-design.md)
- Visual: None found for this topic.
- Plan: [document intake pipeline parity implementation plan](../../plans/2026-05-21-document-intake-pipeline-parity-implementation-plan.md)

## Related Problems

- [document task recovery state drift](../../problems/2026-05/2026-05-21-document-task-recovery-state-drift-problem.md)
- [DI constructor activation boundary](../../problems/2026-05/2026-05-21-di-constructor-activation-boundary-problem.md)
- [MudTable reload cancellation snackbar](../../problems/2026-05/2026-05-20-mudtable-reload-cancellation-snackbar-problem.md)
- [server tests real RAG storage isolation](../../problems/2026-05/2026-05-20-server-tests-real-rag-storage-isolation-problem.md)
- [task state file replace lock](../../problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md)

## Notes

- 首版刻意复用现有 Markdown document 和 RAG task queue 边界，避免创建第二套文档系统；这个选择让 Python-style intake 语义能落在现有 API/Web/SQLite 结构里。
- 恢复语义最终选择“中断即 Failed，显式 retry”，比自动 requeue 更保守，适合避免重复索引和半完成状态被误认为仍在处理中。
