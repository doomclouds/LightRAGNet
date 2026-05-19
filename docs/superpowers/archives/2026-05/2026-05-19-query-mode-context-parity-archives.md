# Query Mode Context Parity

- Date: `2026-05-19`
- Topic slug: `query-mode-context-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `query-mode`, `naive`, `bypass`, `tdd`

## Summary

本轮交付把 `LightRAG.QueryAsync` 从单一 KG 查询路径扩展为显式模式路由：`Bypass` 现在是直接 LLM 通道，`Naive` 走已有 chunk vector-only context 和 naive prompt，KG 模式继续保留 Task2 的 keyword policy 与 `RetrievalContextService` 边界。

## Delivered Scope

- `QueryMode.Bypass` 在空查询检查后直接调用 `ILLMService.GenerateAsync` 或 `GenerateStreamAsync`，并跳过 keyword extraction、retrieval context、vector/rerank/tokenizer 依赖。
- `QueryMode.Naive` 接入 `NaiveQueryService.BuildContextAsync` 与 `NaiveQueryPromptBuilder`，支持 `OnlyNeedContext`、`OnlyNeedPrompt`、streaming 和 non-streaming。
- Bypass raw data 返回空 data 与 `query_mode=Bypass` metadata；Naive 复用 `NaiveQueryService` 返回的 chunks、references 和 metadata。
- 更新所有测试里的 `new LightRAG(...)` 构造点，确保显式传入 `NaiveQueryService`。
- 将 Task2 的临时 Naive 测试改为验证 Naive 绕过 KG keyword policy，而不是继续期待 `NotSupportedException`。

## Out of Scope

- 未实现 Task5 的 KG raw data parity、`IncludeReferences` 和 `QueryResult.ReferenceList` 扩展。
- 未处理 API/Web 默认 Mix streaming 的 Task6 关联影响检查。
- 未加入 query LLM cache；设计文档已明确该能力应在查询模式合同稳定后再做。

## Verification Snapshot

- RED：新增/更新 QueryMode 测试后，目标过滤测试失败 7 条，失败点均为 `LightRAG.QueryAsync` 在 Naive/Bypass 前调用 `ExtractKeywordsAsync`。
- GREEN：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~LightRAGKeywordPolicyIntegrationTests|FullyQualifiedName~NaiveQueryServiceTests|FullyQualifiedName~QueryKeywordPolicyTests"` 通过：`31/31`。
- 回归：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore` 通过：`136/136`。

## Source Documents

- Spec: [query mode context parity design](../../specs/2026-05-19-query-mode-context-parity-design.md)
- Visual: None found for this topic.
- Plan: [query mode context parity implementation plan](../../plans/2026-05-19-query-mode-context-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- 本归档只覆盖 Task 4 的查询路由落地；同一 topic 的后续 Task5/Task6 若继续交付，应更新本 archive 或新增更细分 archive，避免把未完成范围提前声称完成。
