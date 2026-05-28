# RAGAS Evaluation Power Workflow

- Date: `2026-05-28`
- Topic slug: `ragas-evaluation-power-workflow`
- Status: `Archived`
- Scope: `Feature`
- Tags: `evaluation`, `ragas`, `workflow`, `export`, `baseline`, `operations`

## Summary

本归档记录 RAGAS evaluation 从“能创建/读取/取消 run”的 API primitive，升级为可复用的 operations workflow：Server 现在可以列出历史 run、导出安全 JSON/CSV、计算 benchmark summary，并用显式 baseline run id 比较分数变化，同时保留真实 evaluator smoke 的本地 opt-in 边界。

## Delivered Scope

- 增加 `GET /api/evaluation/ragas/runs`，按 `CreatedAt` 降序返回轻量 summary，不暴露 cases、diagnostics 或 full text。
- 增加 `GET /api/evaluation/ragas/runs/{runId}/export?format=json|csv`，JSON 保持存储隐私边界，CSV 使用安全列、RFC-style escaping、公式注入防护和 UTF-8 下载响应。
- 扩展 run summary，记录 success rate、elapsed seconds、average seconds per case、min/max RAGAS score 和 primary failure reason counts。
- 增加 `GET /api/evaluation/ragas/runs/{runId}/compare/{baselineRunId}`，按显式 baseline 比较 `ragasScore`、faithfulness、answer relevance、context recall、context precision，并报告 case-set diagnostics。
- 增加 `docs/evaluation/ragas-power-workflow.md`，说明本地配置、smoke request、inspect/export/compare API、证据包和外部模型费用边界。

## Out of Scope

- 本次没有增加 React dashboard、run deletion、自动样本文档 seeding 或 Python worker/package 集成。
- 本次没有执行真实 evaluator smoke；外部模型调用仍然需要本地显式配置 token/API key 后手动触发。
- 本次没有把默认测试、CI 或 server startup 改成依赖 Qdrant、Neo4j、真实 evaluator key 或付费模型调用。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal`：91 passed。
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal`：首次发现 coordinator env-key 测试清理竞态，修复后 338 passed。
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`：338 passed。
- Task review：Task 6 coordinator、Task 7 controller routes、Task 8 DI registration 均完成 spec/code quality review，最终无阻塞 finding。

## Source Documents

- Spec: [RAGAS Evaluation Power Workflow Design](../../specs/2026-05-28-ragas-evaluation-power-workflow-design.md)
- Visual: None found for this topic.
- Plan: [RAGAS Evaluation Power Workflow Implementation Plan](../../plans/2026-05-28-ragas-evaluation-power-workflow-implementation-plan.md)
- Workflow Guide: [RAGAS Evaluation Power Workflow](../../../evaluation/ragas-power-workflow.md)

## Related Problems

- [Task State File Replace Lock](../../problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md)

## Notes

- Baseline selection intentionally保持显式 run id，不自动使用 latest successful run，避免评估回归解释变得含糊。
- JSON/CSV export 只用于已存储 run 的证据留存，不会补回未持久化的 hidden full text。
