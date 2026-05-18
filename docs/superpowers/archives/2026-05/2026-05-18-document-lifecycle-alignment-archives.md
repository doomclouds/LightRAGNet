# Document Lifecycle Alignment

- Date: `2026-05-18`
- Topic slug: `document-lifecycle-alignment`
- Status: `Archived`
- Scope: `Core`
- Tags: `lightrag-alignment`, `document-lifecycle`, `tdd`, `workspace`

## Summary

本轮交付把 Python LightRAG 的关键文档索引生命周期语义引入 LightRAGNet：文档从 `pending`、`processing` 到 `processed` / `failed` 的状态变得可测试，chunk 快照在失败场景中可保留，删除计划和删除失败重试合同有了显式模型，`doc_status` 也通过 workspace 维度持久化到现有 KV 存储体系。

## Delivered Scope

- 新增 `Services/DocumentLifecycle` 核心模型、状态机服务、`IDocumentStatusStore` 抽象和 `KvDocumentStatusStore` 生产适配器。
- 将 `LightRAG.InsertAsync` 接入生命周期服务，覆盖重复文档短路、失败重试、chunk 快照保存和成功后 `processed` 状态写入。
- 为 `doc_status` 持久化补上 length-prefix key、legacy key 迁移、legacy 碰撞保护和 workspace 隔离回归测试。
- 增加 core lifecycle、LightRAG 集成、KV roundtrip 和 Server thin smoke 覆盖，Server 测试使用隔离 SQLite 与临时工作目录。

## Out of Scope

- 未实现完整 Python `adelete_by_doc_id` 等价行为。
- 未落地真实 Qdrant chunk vector 删除、Neo4j entity/relation source-id pruning、LLM cache 删除或 graph rebuild。
- 未扩展 Blazor UI 自动化和 Docker/Testcontainers 集成测试。
- 未处理既有 `System.Security.Cryptography.Xml 9.0.0` 的 NU1903 依赖警告。

## Verification Snapshot

- `dotnet restore .\LightRAGNet.slnx` 通过，保留既有 NU1903 警告。
- `dotnet build .\LightRAGNet.slnx` 通过。
- `dotnet test .\LightRAGNet.slnx` 通过：`LightRAGNet.Tests 56/56`，`LightRAGNet.Server.Tests 2/2`。
- 使用 `.worktrees/document-lifecycle-alignment` 隔离开发，经过子代理实现、规格审查、代码质量审查和多轮 verification fix 后合并到 `main`。
- 合并提交为 `4b4db58 merge: document lifecycle alignment`，后续端口兜底提交为 `20299dc fix: set default app urls`。

## Source Documents

- Spec: [document lifecycle alignment design](../../specs/2026-05-18-document-lifecycle-alignment-design.md)
- Visual: None found for this topic.
- Plan: [document lifecycle alignment implementation plan](../../plans/2026-05-18-document-lifecycle-alignment-implementation-plan.md)

## Related Problems

- [testability refactor completion gaps](../../problems/2026-05/2026-05-18-testability-refactor-completion-gaps-problem.md)

## Notes

- 本轮最终问题门补录了资产归档遗漏：脚本 completion gate `pass` 不等于主代理可以跳过 archive/problem 判断。
