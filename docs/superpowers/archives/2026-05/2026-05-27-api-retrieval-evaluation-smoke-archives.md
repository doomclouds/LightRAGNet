# API Retrieval Evaluation Smoke

- Date: `2026-05-27`
- Topic slug: `api-retrieval-evaluation-smoke`
- Status: `Archived`
- Scope: `Test`
- Tags: `evaluation`, `api-smoke`, `retrieval-oracle`, `python-parity`, `testability`

## Summary

本轮交付把昨天的离线 JSON retrieval oracle 向上推进到 Server API 边界：`LightRAGNet.Server.Tests` 现在复用同一批 sample dataset/oracle 文件，通过真实 ASP.NET `/api/RagQuery/data` endpoint 验证 Naive 与 KG retrieval raw data，同时继续隔离真实 LLM、embedding、rerank、Qdrant、Neo4j 和 Docker。

## Delivered Scope

- Server 测试项目链接 `tests/LightRAGNet.Tests/Evaluation/Data/**/*` 到测试输出目录，避免复制 JSON 数据集。
- 新增 Server-only evaluation loader，读取 corpus、cases、expected chunks、references、entities 和 relationships，并做基础引用校验。
- 新增显式内存测试双，替换 vector store、graph store、keyed KV stores、LLM、embedding、rerank 和 tokenizer，并将 KG chunk pick 固定为 `WEIGHT`。
- 新增 API smoke tests，POST `/api/RagQuery/data` 覆盖 `Naive_ReturnsExpectedArchitectureChunk` 与 `Local_UsesLowLevelEntityFocus`，断言 `status`、`message`、`metadata.query_mode`、chunks、references、entities 和 relationships。

## Out of Scope

- 未接入 RAGAS、answer-quality evaluator、真实 API model key、Qdrant、Neo4j、Docker、Web/React 或浏览器验证。
- 未改变生产 API、controller、retrieval service、storage adapter 或公共 DTO。
- 未新增报告导出、CI dashboard 或大规模 benchmark 数据集。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagQueryControllerTests" --no-restore --verbosity minimal` passed (`8/8`) after linking shared evaluation data.
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests" --no-restore --verbosity minimal` passed (`2/2`).
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ApiRetrievalEvaluationSmokeTests|FullyQualifiedName~RagQueryControllerTests|FullyQualifiedName~RagQueryRequestMapperTests" --no-restore --verbosity minimal` passed (`16/16`).
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal` passed (`224/224`).
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` passed with `LightRAGNet.Tests` (`446/446`), `LightRAGNet.Web.Tests` (`36/36`), and `LightRAGNet.Server.Tests` (`224/224`).

## Source Documents

- Spec: [API Retrieval Evaluation Smoke Design](../../specs/2026-05-27-api-retrieval-evaluation-smoke-design.md)
- Visual: None found for this topic.
- Plan: [API Retrieval Evaluation Smoke Implementation Plan](../../plans/2026-05-27-api-retrieval-evaluation-smoke-implementation-plan.md)

## Related Problems

- None.

## Notes

- 这层 smoke 是 RAGAS 前的中间保险：它验证 Server/API 映射和 raw retrieval data 合同，但仍保持默认测试无成本、无外部依赖。
