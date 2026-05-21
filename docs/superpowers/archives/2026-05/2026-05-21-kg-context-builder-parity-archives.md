# KG Context Builder Parity

- Date: `2026-05-21`
- Topic slug: `kg-context-builder-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `kg-query`, `context-builder`, `token-budget`, `tdd`

## Summary

本轮交付把 KG query 最终喂给 LLM 的 context 构造从 `RetrievalContextService` 中拆到 `KgQueryContextBuilder`，并把 entity、relationship、chunk 与 reference list 改成可测试的结构化合同。KG context 现在使用稳定 JSON section 和 `reference_id` 锚点，token budget 也在 builder 边界按最终输出形态计算，给后续 prompt parity、raw data 校验和检索调试留出更干净的基线。

## Delivered Scope

- 新增 `KgQueryContextBuilder`，集中负责 KG entity、relationship、document chunks 和 reference list 的最终 context 构造。
- KG context 输出改为结构化 JSON section：entities 使用 `entity` / `type` / `description`，relationships 使用 `entity1` / `entity2` / `keywords` / `description`，chunks 使用 `reference_id` / `content`。
- KG chunks、raw data chunks 和 raw data references 使用一致的 `reference_id`，reference list 输出为 `[reference_id] file_path`。
- Entity、relationship、chunk token 截断按 builder 实际输出的 JSON/context section 计数；chunk 预算包含最终 chunk section 与 reference list 影响。
- `RetrievalContextService` 继续负责检索 orchestration 和 related chunk selection，但不再保留旧的 KG context 文本拼接与 service 侧 chunk 预裁剪。
- 空检索结果的 no-context 行为保持不变，不生成空壳 context。

## Out of Scope

- 未实现 prompt text perfect parity，也未调整 Python prompt 模板逐字一致性。
- 未修改 rerank algorithm、provider fallback、query cache key、cache 管理 UI/API、Server/Web UI 或真实 Qdrant/Neo4j integration tests。
- 未改变 public query API、`QueryContextResult` 外形、`QueryParam` 默认值或数据库结构。
- 未把 `RetrievalContextService` 中用于 raw data 的临时 reference 构建进一步收口；该清理可留给后续更小切片。

## Verification Snapshot

- Task 1 RED/GREEN：先新增缺失 `KgQueryContextBuilder` 的失败测试，再实现结构化 JSON section 与 reference ids，`KgQueryContextBuilderTests` 通过。
- Task 2 预算回归：补齐 entity/relation JSON 形态计数、chunk section 预算、reference list 预算和零/负 chunk budget 回归；修正过一次测试 tokenizer 过度拟合，最终锁定 reference list overhead。
- Task 3 service 接线：`RetrievalContextServiceRawDataTests` 从旧文本 context 失败，改为通过 builder 输出后验证 context/raw data/reference ids 一致。
- Task 4 final chunk budget：移除 service 侧旧 `_tokenBudgetPlanner` / `_chunkTokenLimiter` 预裁剪，把 KG chunks 的最终预算门收口到 builder，并保留 `SafetyBufferTokens = 200`。
- Focused 回归：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~KgQueryContextBuilder|FullyQualifiedName~RetrievalContext|FullyQualifiedName~QueryCache|FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~NaiveQueryServiceTests" --verbosity minimal` 通过：`98/98`。
- Solution 回归：`dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` 通过：`LightRAGNet.Tests 317/317`、`LightRAGNet.Server.Tests 32/32`、`LightRAGNet.Web.Tests 20/20`。
- Build：`dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal` 成功，`0` warning / `0` error。
- Review：Task 1-4 均经过 spec review 与 code quality review；最终 Task 4 复审确认没有 stale budget fields/usings、没有双重预算门残留。

## Source Documents

- Spec: [kg context builder parity design](../../specs/2026-05-21-kg-context-builder-parity-design.md)
- Visual: None found for this topic.
- Plan: [kg context builder parity implementation plan](../../plans/2026-05-21-kg-context-builder-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- 本轮最有价值的测试经验是：预算回归不能只证明 payload 超预算，还要隔离 section wrapper 与 reference list overhead；否则测试可能“看起来过了”，实际没锁住错的那一层。
- `RetrievalContextService` 当前仍会先为 raw search result 构建一次临时 references，再由 builder 对最终 accepted chunks 重建 references；复审确认不会造成 id 错配，但未来可以作为小切片继续收口。
