# Cache Management Workbench

- Date: `2026-05-24`
- Topic slug: `cache-management-workbench`
- Status: `Archived`
- Scope: `Feature`
- Tags: `cache-management`, `metrics`, `react-island`, `operations`, `query-cache`

## Summary

本轮交付把 LightRAGNet 的缓存能力从“后端有缓存但不可观察”推进到可运维的 Cache Management workbench：运行时统一走 `GetOrCreate...` 缓存边界，后端记录真实 read hit/miss、factory duration 和库存状态，Web UI 直接展示命中率、节省调用、延迟收益、风险条目和清理计划。

## Delivered Scope

- 将 query、keywords、extract、summary 缓存调用迁移到 `GetOrCreate...` API，并移除旧 `TryGet...` / `Save...` 运行时 API。
- 增加缓存指标模型、JSON 指标存储和 recorder，保留 read outcome、factory duration、save/clear 事件等后续可观测数据。
- 增加 Cache Management API，返回 overview、family hit rate、trend、insights、clear plan 和只含安全字段的 entry samples。
- 实现安全清理执行：`stale-query-cache` 只清当前 workspace 的 old revision query entries，`summary-cache-review` 和 `all-llm-cache` 需要确认，`JsonKVStore` 删除后显式 flush。
- 增加 React/Vite 缓存管理工作台和 Blazor `/cache-management` host，深色模式下展示缓存效率、风险、趋势、安全样本和清理结果。
- 前端对 clear 后 refresh、错误响应解析和 Copy JSON 做了安全边界：旧 workspace 请求不能覆盖新视图，非 JSON/空响应给明确错误，导出 JSON 只复制 DTO 白名单字段。
- 2026-05-28 追加完成 React 独立前端 `/cache-management` 的 compact light workbench 重构：按 `04-system-cache-table-pages.png` / `06-cache-management-table-pages-react-prototype.html` 对齐 System Status 的浅色运营台语言，落地 5 指标卡、Cache Families 表、右侧 Cache Insights + Hit Rate Trend、底部 Clear Plan + Clear Policy。

## Out of Scope

- 未实现跨 provider / embedding / vector cache 的统一管理；本轮范围聚焦 LLM cache。
- 未提供按单条 cache key 手动删除或原始 prompt/response 查看；UI 只展示安全摘要字段。
- 未引入真实 Qdrant/Neo4j 集成测试或外部服务依赖；验证以单元、source、API host 和前端 build 为主。
- 未修复既有 `Neo4jGraphStoreSourceTests.GetPopularLabelsAsync_FiltersUnwoundLabelsAfterWithClause` 全解失败；该问题属于既有图谱 source-string 回归测试边界。
- 2026-05-28 的视觉重构未扩展后端时间窗口语义；前端补齐 `1H/6H/24H/7D/30D` 分段壳，真实数据仍走现有 Cache Management overview API。

## Verification Snapshot

- `rg -n "TryGetKeywordsAsync|SaveKeywordsAsync|TryGetQueryResponseAsync|SaveQueryResponseAsync|TryGetExtractAsync|SaveExtractAsync|TryGetSummaryAsync|SaveSummaryAsync" src tests` returned no matches.
- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessingServiceTests|FullyQualifiedName~DescriptionMergerTests" --no-restore --verbosity minimal` passed (`93/93`).
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~CacheManagement|FullyQualifiedName~MarkdownDocumentsControllerTests" --no-restore --verbosity minimal` passed (`34/34`).
- `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~CacheManagementHostSourceTests|FullyQualifiedName~GraphWorkbenchHostSourceTests" --no-restore --verbosity minimal` passed (`9/9`).
- `Push-Location src\LightRAGNet.Web\ClientApp; npm test; npm run build; Pop-Location` passed (`48/48` Vitest tests, Vite build produced cache-management and graph-workbench assets).
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` completed with the known unrelated `Neo4jGraphStoreSourceTests.GetPopularLabelsAsync_FiltersUnwoundLabelsAfterWithClause` failure; `LightRAGNet.Server.Tests` passed (`175/175`), `LightRAGNet.Web.Tests` passed (`34/34`), and `LightRAGNet.Tests` had `426/427` passed.
- `git diff --check` exited `0`; only LF/CRLF warnings were printed for generated frontend assets.
- 2026-05-28 React redesign verification: `npm test -- tests/integration/features/cache-management/CacheManagementWorkbench.test.tsx` passed (`8/8`), `npm test` passed (`35/35` files, `271/271` tests), and `npm run build` passed.
- 2026-05-28 Playwright visual QA used mocked cache overview data on `http://127.0.0.1:5174/cache-management`; desktop and mobile had no page-level horizontal overflow, desktop Cache Families table fit without internal horizontal scroll, and screenshots were saved under `docs/superpowers/visuals/anthropic-light-workbench/cache-management-real-*.png`. SignalR hub connection errors were expected because the backend hub was not started for the mocked visual run.

## Source Documents

- Spec: [Cache Management Workbench Design](../../specs/2026-05-24-cache-management-workbench-design.md)
- Visual: [Cache Management UI Concept](../../visuals/cache-management-ui-concept.html)
- Visual: [System Cache Table Pages Reference](../../visuals/anthropic-light-workbench/04-system-cache-table-pages.png)
- Visual: [Cache Management Table Pages React Prototype](../../visuals/anthropic-light-workbench/06-cache-management-table-pages-react-prototype.html)
- Visual QA: [Cache Management Real Desktop](../../visuals/anthropic-light-workbench/cache-management-real-desktop.png)
- Visual QA: [Cache Management Real Mobile](../../visuals/anthropic-light-workbench/cache-management-real-mobile.png)
- Plan: [Cache Management Workbench Implementation Plan](../../plans/2026-05-24-cache-management-workbench-implementation-plan.md)

## Related Problems

- [Json KV Delete Flush Problem](../../problems/2026-05/2026-05-24-json-kv-delete-flush-problem.md)
- [Neo4j Labels Unwind Filter Problem](../../problems/2026-05/2026-05-22-neo4j-labels-unwind-filter-problem.md)

## Notes

- `IKVStore.DeleteAsync` 对 `JsonKVStore` 只改内存，执行缓存清理这类用户可见删除后必须跟随 `IndexDoneCallbackAsync` 才能跨重启持久化。
- React island 的请求落状态必须校验请求参数仍是当前 workspace/window；clear 后刷新尤其容易把旧 workspace 结果写回新视图。
- Cache Management 已在独立 React 前端中按 compact light workbench 方向重构；继续扩展时优先复用本轮 `CacheClearPolicy`、表格密度、指标卡和右侧洞察/趋势布局，而不是回退到旧 dark concept。
