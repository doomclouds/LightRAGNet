# Offline Retrieval Evaluation Fixture

- Date: `2026-05-25`
- Topic slug: `offline-retrieval-evaluation-fixture`
- Status: `Archived`
- Scope: `Test`
- Tags: `evaluation`, `retrieval-oracle`, `python-parity`, `testability`, `raw-data`

## Summary

本轮交付为 LightRAGNet 增加了 .NET 原生的离线检索评估 fixture，用一个固定的小语料和七个 oracle case 守住 Naive、Local、Global、Mix 与 rerank 的 raw retrieval data 合同。它不依赖 Docker、真实向量库、LLM 或前端页面，专门用于在常规 `dotnet test` 中捕获 chunk、reference、entity、relationship、keyword routing 和 rerank 顺序回归。

## Delivered Scope

- 新增 `tests/LightRAGNet.Tests/Evaluation/`，包含 evaluation case model、deterministic corpus、fixture、runner 和七个 offline oracle tests。
- 语料覆盖 overview、architecture、operations、storage、evaluation 五个固定 chunk，并为 KG retrieval seed 了实体、关系、向量索引和 text chunk 数据。
- fixture 复用生产 `NaiveQueryService` 与 `RetrievalContextService`，并用 fake embedding、deterministic rerank 和 in-memory stores 保持测试可重复。
- runner 断言 raw data 的 chunks、references、entities、relationships、metadata、forbidden chunks、strict empty KG sections、方向无关关系匹配和显式 top-k 顺序。
- `InMemoryVectorStore` 增加 opt-in score override，用于在不改变默认插入顺序行为的前提下表达 rerank 排序 oracle。

## Out of Scope

- 未接入 RAGAS、LLM-judged answer quality 或外部 evaluator；本轮只覆盖 offline retrieval oracle 层。
- 未新增产品 UI、Server API、React/Blazor 页面或浏览器验证。
- 未引入 JSON evaluation dataset 文件；当前 case 与 corpus 仍以 C# fixture builders 表达。
- 未改变生产 retrieval 参数语义，例如未支持“候选数”和“最终 rerank topN”分离。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal` passed (`7/7`).
- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~RerankCoordinator|FullyQualifiedName~ReferenceListBuilder" --verbosity minimal` passed (`50/50`).
- `dotnet test .\LightRAGNet.slnx --logger "console;verbosity=minimal"` passed: `LightRAGNet.Tests` (`436/436`), `LightRAGNet.Web.Tests` (`36/36`), `LightRAGNet.Server.Tests` (`222/222`).
- Scope check from `7db2257..HEAD` showed changes limited to `tests/LightRAGNet.Tests/Evaluation/...` and `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`.
- Per-task spec and code-quality subagent reviews passed after follow-up fixes for corpus contracts, raw-data diagnostics, default KG behavior, relationship direction, keyword routing, vector topK, and rerank candidate semantics.

## Source Documents

- Spec: [Offline Retrieval Evaluation Fixture Design](../../specs/2026-05-25-offline-retrieval-evaluation-fixture-design.md)
- Visual: None found for this topic.
- Plan: [Offline Retrieval Evaluation Fixture Implementation Plan](../../plans/2026-05-25-offline-retrieval-evaluation-fixture-implementation-plan.md)

## Related Problems

- None.

## Notes

- `KgChunkPickMethod` is intentionally explicit as `WEIGHT` in the evaluation fixture because the implementation plan chose deterministic related chunk selection for this offline oracle slice.
- Relationship matching in the runner is direction-insensitive so logical graph pairs survive current Local/Global endpoint ordering differences.
- Rerank survival is verified only within production-requested vector `ChunkTopK`; no test-double candidate-count backdoor remains.
