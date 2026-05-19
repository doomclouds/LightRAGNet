# Query Mode Context Parity

- Date: `2026-05-19`
- Topic slug: `query-mode-context-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `query-mode`, `naive`, `bypass`, `tdd`

## Summary

本轮交付把 `LightRAG.QueryAsync` 从单一 KG 查询路径扩展为显式模式路由：`Bypass` 现在是直接 LLM 通道，`Naive` 走 chunk vector-only context 和 naive prompt，KG 模式继续保留 keyword fallback policy 与 `RetrievalContextService` 边界。查询结果 raw data 也补齐为可测试合同，便于 UI/debug 读取 chunks、references、keywords 和处理统计。

## Delivered Scope

- `QueryMode.Bypass` 在空查询检查后直接调用 `ILLMService.GenerateAsync` 或 `GenerateStreamAsync`，并跳过 keyword extraction、retrieval context、vector/rerank/tokenizer 依赖。
- `QueryMode.Naive` 接入 `NaiveQueryService.BuildContextAsync` 与 `NaiveQueryPromptBuilder`，支持 `OnlyNeedContext`、`OnlyNeedPrompt`、streaming 和 non-streaming。
- Bypass raw data 返回空 data 与 `query_mode=Bypass` metadata；Naive 复用 `NaiveQueryService` 返回的 chunks、references 和 metadata。
- KG raw data 输出 `entities`、`relationships`、`chunks`、`references`，并保留 flat keywords，同时新增 nested `metadata.keywords` 与 `metadata.processing_info`。
- `QueryResult.ReferenceList` 同时兼容 `List<Dictionary<string, object>>` 和旧的 `List<object>` references shape，避免 Example 或后续 UI 读到空引用。
- 更新所有测试里的 `new LightRAG(...)` 构造点，确保显式传入 `NaiveQueryService`。
- 将 Task2 的临时 Naive 测试改为验证 Naive 绕过 KG keyword policy，而不是继续期待 `NotSupportedException`。
- 修复 Server 测试共享 `Uploads` 目录导致的 solution 级并行失败，将相关文件系统测试类放入同一个 xUnit collection。

## Out of Scope

- 未加入 API/Web 查询模式选择器；`RagQueryController` 和 Blazor chat 仍保持默认 Mix streaming 行为。
- 未改变 `IncludeReferences` 语义。
- 未加入 query LLM cache；设计文档已明确该能力应在查询模式合同稳定后再做。

## Verification Snapshot

- RED：新增/更新 QueryMode 测试后，目标过滤测试失败 7 条，失败点均为 `LightRAG.QueryAsync` 在 Naive/Bypass 前调用 `ExtractKeywordsAsync`。
- GREEN：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~LightRAGKeywordPolicyIntegrationTests|FullyQualifiedName~NaiveQueryServiceTests|FullyQualifiedName~QueryKeywordPolicyTests"` 通过：`31/31`。
- Raw data 合同：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalContextServiceRawDataTests|FullyQualifiedName~QueryResultReferenceListTests|FullyQualifiedName~NaiveQueryServiceTests"` 通过：`13/13`。
- 关联影响：API/Web grep 确认默认 Mix streaming 未扩面，`IncludeReferences` 未被重新解释，Example 继续通过 `QueryResult.ReferenceList` 读取引用。
- 回归：`dotnet test .\LightRAGNet.slnx --no-restore` 通过：`LightRAGNet.Tests 142/142`、`LightRAGNet.Server.Tests 23/23`。
- Build：`dotnet restore .\LightRAGNet.slnx` 后 `dotnet build .\LightRAGNet.slnx --no-restore` 成功，`0` warning / `0` error。

## Source Documents

- Spec: [query mode context parity design](../../specs/2026-05-19-query-mode-context-parity-design.md)
- Visual: None found for this topic.
- Plan: [query mode context parity implementation plan](../../plans/2026-05-19-query-mode-context-parity-implementation-plan.md)

## Related Problems

- [server filesystem test parallelism](../../problems/2026-05/2026-05-19-server-filesystem-test-parallelism-problem.md)

## Notes

- `dotnet build --no-restore` 首次失败是 worktree 中 `LightRAGNet.Example` 和 `LightRAGNet.Web` 尚未生成 `project.assets.json`；执行 solution restore 后 build 通过。
