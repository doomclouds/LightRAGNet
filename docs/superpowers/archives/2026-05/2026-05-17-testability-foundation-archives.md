# LightRAGNet Testability Foundation

- Date: `2026-05-17`
- Topic slug: `testability-foundation`
- Status: `Archived`
- Scope: `Test`
- Tags: `testability`, `src-tests-layout`, `characterization-tests`

## Summary

本轮交付把 LightRAGNet 从零测试状态推进到可维护、可验证的基础结构：生产项目迁入 `src/`，测试项目落到 `tests/`，并围绕文档分块、检索上下文、图谱合并和任务队列补上第一层高价值 characterization tests。

## Delivered Scope

- 生产项目统一迁移到 `src/`，测试项目统一创建在 `tests/`，并更新 `LightRAGNet.slnx`。
- 新增 `LightRAGNet.Tests` 与 `LightRAGNet.Server.Tests`，接入 xUnit、FluentAssertions、NSubstitute、coverlet 和 server host smoke test。
- 覆盖文档分块、检索上下文纯组件、source id 限制、描述合并和任务队列状态流。
- 抽出 `TokenBudgetPlanner`、`ChunkTokenLimiter`、`ReferenceListBuilder` 作为可直接测试的检索上下文测试缝。

## Out of Scope

- 未加入 `LightRAGNet.Web` UI 自动化测试。
- 未加入 Qdrant、Neo4j、Docker 或 Testcontainers 集成测试。
- 未重写检索、图谱合并、队列、存储或 Python 参考实现。
- 未处理 `System.Security.Cryptography.Xml 9.0.0` 的 NU1903 传递依赖警告。

## Verification Snapshot

- `dotnet restore .\LightRAGNet.slnx` 通过，保留已记录的 NU1903 warning。
- `dotnet build .\LightRAGNet.slnx` 通过，0 errors，4 warnings。
- `dotnet test .\LightRAGNet.slnx` 通过：`LightRAGNet.Tests` 26/26，`LightRAGNet.Server.Tests` 1/1。
- 每个实现任务均经过实现子代理、规格审查和代码质量审查；Task 6 与 Task 8 的审查发现已回修并复审通过。

## Source Documents

- Spec: [testability foundation design](../../specs/2026-05-17-testability-foundation-design.md)
- Visual: None found for this topic.
- Plan: [testability foundation implementation plan](../../plans/2026-05-17-testability-foundation-implementation-plan.md)

## Related Problems

- [testability refactor completion gaps](../../problems/2026-05/2026-05-18-testability-refactor-completion-gaps-problem.md)

## Notes

- 同线程弱信号已停在 inbox：[NU1903 test project transitive vulnerability](../../inbox/2026-05/2026-05-17-nu1903-test-project-transitive-vulnerability-inbox.md)。
