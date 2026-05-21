# Rerank Chunking Parity

- Date: `2026-05-21`
- Topic slug: `rerank-chunking-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `rerank`, `query-quality`, `chunking`, `tdd`

## Summary

本轮交付把 rerank 从“直接对原始长 chunk 打分”升级为 Python 风格的长文档子片段 rerank：超长候选 chunk 先按 tokenizer token window 切成重叠子片段，provider 返回的子片段分数再用 max 聚合回原始 chunk。Naive 和 KG Mix 的 vector chunks 现在共用同一个 `RerankCoordinator`，避免 provider 级 `topN` 提前截断子片段，也让后续接不同 rerank provider 时有稳定的内部边界。

## Delivered Scope

- 新增内部 `RerankDocumentChunker` 和 `RerankChunkingOptions`，默认启用 chunking，并使用 `ITokenizer.Encode/Decode` 做真实 token window 切片。
- 新增内部 `RerankCoordinator`，在 chunking 发生时以所有子片段数量作为 provider-level `topN`，再按原始 document index 做 max-score 聚合和 document-level `topN`。
- `NaiveQueryService` 改为强类型依赖 `RerankCoordinator`，长 chunk rerank 结果按聚合后的原始 chunk 分数排序。
- `RetrievalContextService` 的 KG Mix vector chunk rerank 改用同一个 coordinator，并保留 invalid/duplicate index 过滤。
- Hosting 注册了 `RerankChunkingOptions`、`RerankDocumentChunker`、`RerankCoordinator`，并用 factory 显式构造 internal `NaiveQueryService` / `RetrievalContextService`，避免 DI 选错构造路径。
- 补齐 chunker、coordinator、Naive、RetrievalContext 和 Hosting DI 解析测试；现有构造点默认 `EnableChunking=false`，避免旧用例被 incidental chunking 改写。

## Out of Scope

- 未新增或修改真实 rerank provider 协议、HTTP wrapper、API key 配置或 integration tests。
- 未修改 public `QueryParam`、Server request、Blazor UI、Chat settings、prompt 模板或 query cache key 合同。
- 未调整 KG entity/relation ranking、related chunk vector selection、context builder 内容形态、索引、删除或真实 Qdrant/Neo4j 存储行为。
- 未实现 `mean` / `first` 聚合配置；本阶段固定对齐 Python 默认的 max aggregation。

## Verification Snapshot

- Task 1 RED/GREEN：先用失败测试锁住短文档一对一、长文档 overlap、多文档映射、overlap clamp、空输入；复审后补齐无词边界和 subword tokenizer token-budget 回归，最终 `RerankDocumentChunkerTests` 通过 `7/7`。
- Task 2 RED/GREEN：新增 `RerankCoordinatorTests` 覆盖 disabled passthrough、实际 chunking fan-out、provider-level topN、max aggregation、document-level topN、invalid/duplicate indexes 和空输入不调用 provider。
- Task 3 Naive 接线：先暴露 direct `IRerankService` 构造依赖和弱类型 `object` fallback 的问题，再收口到唯一强类型 `RerankCoordinator` 构造面，并迁移所有旧构造点。
- Task 4 KG Mix 接线：`RetrievalContextService` vector chunk rerank 改用 coordinator；新增 Mix vector rerank aggregation 测试和 Hosting `AddLightRAG` ServiceProvider 解析测试。
- Focused 回归：`dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Rerank|FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~LightRAGQueryModeTests|FullyQualifiedName~QueryCache" --verbosity minimal` 通过：`118/118`。
- Solution 回归：`dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` 通过：`LightRAGNet.Tests 337/337`、`LightRAGNet.Server.Tests 32/32`、`LightRAGNet.Web.Tests 20/20`。
- Build：先因 worktree 缺少部分 `project.assets.json` 失败；执行 `dotnet restore .\LightRAGNet.slnx` 后，`dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal` 成功，`0` errors，剩余 `6` 个 `NU1900` warning 来自 nuget.org vulnerability service index 加载失败。
- Review：Task 1-4 均经过 spec review 与 code quality review；关键复审拦下了 whitespace/character pseudo-token 切片、public/internal API surface、弱类型 `object` 构造桥和 internal ctor + `AddSingleton<T>` 的 DI 解析风险。

## Source Documents

- Spec: [rerank chunking parity design](../../specs/2026-05-21-rerank-chunking-parity-design.md)
- Visual: None found for this topic.
- Plan: [rerank chunking parity implementation plan](../../plans/2026-05-21-rerank-chunking-parity-implementation-plan.md)

## Related Problems

- None.

## Notes

- 本轮最值得保留的工程边界是：内部 coordinator 不能靠 `object` 构造桥或默认 DI 反射来“绕过”可见性问题；需要强类型构造面、factory 注册和 ServiceProvider 解析测试一起锁住。
- `dotnet restore` / build 过程中出现的 `NU1900` 是包漏洞数据源访问失败 warning，不影响本次编译结果；如果后续 CI 将 NU1900 作为错误处理，需要单独治理 NuGet audit 源访问。
