# Offline Retrieval JSON Dataset and Oracle

- Date: `2026-05-26`
- Topic slug: `offline-retrieval-json-dataset-oracle`
- Status: `Archived`
- Scope: `Test`
- Tags: `evaluation`, `retrieval-oracle`, `json-dataset`, `python-parity`, `testability`

## Summary

本轮交付把 2026-05-25 的离线检索评估 fixture 从 C# 硬编码 case/corpus 迁移为 JSON 数据驱动：保留 Python LightRAG 的 `sample_dataset.json` / `sample_retrieval_oracle.json` 形状，同时新增 LightRAGNet extended oracle 承载 mode、keywords、chunks、references、KG entities、relationships、forbidden chunks、rerank 顺序和 deterministic score hints。它继续保持纯本地、确定性、不接真实 LLM/向量库/Server/Web 的测试边界。

## Delivered Scope

- 新增 `tests/LightRAGNet.Tests/Evaluation/Data/`，包含 10 个 dataset/document-oracle question、5 个 sample markdown documents，以及 `lightragnet_retrieval_oracle.json` 的 corpus 与 5 个 raw retrieval cases。
- 新增 `RetrievalEvaluationDataLoader`、JSON DTO 和 runtime dataset records，校验 dataset/oracle join、文档存在、chunk/entity/relationship 引用、required fields、duplicate relationship pair、rerank/vector hint 引用和 exact-order 合同。
- `RetrievalEvaluationCorpus` 与 `RetrievalEvaluationFixture` 改为从 loaded dataset seed chunks、text chunks、graph nodes/edges、KG vector documents，并保留旧 fixture factory 的清晰入口。
- `OfflineRetrievalEvaluationTests` 改为枚举 JSON cases 执行 retrieval oracle，并保留 Local/Global/rerank 的 query-call routing 断言，防止 JSON 化后丢掉关键词路由与 rerank candidate count 覆盖。
- `RetrievalEvaluationRunner` 支持仅在 `ExpectedChunkOrder` 非空时做 exact chunk order 断言，避免所有 case 被迫维护顺序 oracle。

## Out of Scope

- 未接入真实 LLM、RAGAS、answer quality evaluator、Qdrant、Neo4j、Server API、Web/React UI 或浏览器验证。
- 未执行 Python LightRAG runtime 对比；本轮只复用 Python-compatible 数据文件形状和 sample document/oracle 思路。
- 未扩展大型 benchmark dataset、HTML/CSV 报告或 CI dashboard。
- 未把 query-call routing 断言扩为 JSON schema 字段；当前用测试 helper 按既有 case name 保留原覆盖。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal` passed (`14/14`).
- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~RerankCoordinator|FullyQualifiedName~ReferenceListBuilder" --verbosity minimal` passed (`50/50`).
- `dotnet test .\LightRAGNet.slnx --verbosity minimal` passed on rerun: `LightRAGNet.Tests` (`443/443`), `LightRAGNet.Web.Tests` (`36/36`), `LightRAGNet.Server.Tests` (`222/222`).
- One first full-solution run hit a transient `DocumentConversionProcessorTests.ProcessNextBatchAsync_WhenConcurrentProcessorsRace_ClaimsDocumentOnce` failure; the failed test passed when rerun alone, and the full solution passed on the next sequential run.
- Scope check from `5848497..HEAD` showed changes limited to `docs/superpowers/plans/...`, `tests/LightRAGNet.Tests/Evaluation/...`, and `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj`.
- Per-task implementer, spec-review, and code-quality subagent gates passed after fixes for oracle consistency, required-field validation, duplicate relationship pairs, graph/KG vector seeding assertions, query-call coverage regression, and per-case routing-call isolation.

## Source Documents

- Spec: [Offline Retrieval JSON Dataset and Oracle Design](../../specs/2026-05-26-offline-retrieval-json-dataset-oracle-design.md)
- Visual: None found for this topic.
- Plan: [Offline Retrieval JSON Dataset and Oracle Implementation Plan](../../plans/2026-05-26-offline-retrieval-json-dataset-oracle-implementation-plan.md)

## Related Problems

- None.

## Notes

- Dataset/document oracle 最终为 10 个 question：6 个 Python-compatible LightRAG sample，加 4 个 LightRAGNet extended questions；额外的 Global 独立 question 用于保留既有 raw-data retrieval case 语义。
- 测试命令不要并发跑同一个 test project；本轮两次 reviewer 并行执行同一 csproj 时触发过 DLL 写入锁，顺序执行通过。
