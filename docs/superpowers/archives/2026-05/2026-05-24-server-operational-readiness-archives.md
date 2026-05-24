# Server Operational Readiness System Status

- Date: `2026-05-24`
- Topic slug: `server-operational-readiness`
- Status: `Archived`
- Scope: `Feature`
- Tags: `server-health`, `operations`, `react-island`, `diagnostics`, `configuration-audit`

## Summary

本需求把 LightRAGNet 的运维排障能力从日志和人工猜测推进到 Web 可见的 `System Status`：后端并发运行证据驱动的健康检查，返回结构化 evidence、字符串 remediation、`fixFirst` 和 feature impact；前端用 React island 只展示后端 DTO，不编造健康分数、不重算状态，也不提供破坏性操作。

## Delivered Scope

- 新增 `GET /api/system/health`、插件式 `ISystemHealthCheck`、`SystemHealthService` 聚合器和十个 v1 checks：Server API、SQLite、WorkingDir、Qdrant、Neo4j、LLM config、Embedding config、Rerank config、RAG task queue、Conversion queue。
- 聚合器负责并发执行、单项 timeout、异常兜底、整体状态、summary、`fixFirst`、feature impact 和最终 evidence 脱敏，避免 API key、password、token、authorization 或 connection string 泄漏。
- 新增 `/system-status` React island 页面和 Blazor host/nav 入口，支持 Refresh、Copy JSON、evidence 展开、feature impact links，并提交 Vite 多入口生成的 system-status、graph-workbench 和 shared chunk assets。
- Server tests 默认隔离真实 Qdrant/Neo4j，SQLite/conversion checks 使用独立 `IDbContextFactory<AppDbContext>`，避免健康检查并发共享 scoped DbContext。

## Out of Scope

- 不真实调用 LLM、Embedding 或 Rerank provider，不探测 Docker 容器、Kubernetes liveness/readiness、SignalR 服务端、OpenAPI/Scalar、auth/security 或 cache management。
- 不提供 clear all、clear cache、retry worker、修改配置、自动修复或任何破坏性/写入式 UI 操作。
- 不把视觉原型里的无源健康分数、分数圆环或场景切换带入正式页面。

## Verification Snapshot

- Targeted Server verification: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealth" --no-restore --verbosity minimal` passed `27/27`.
- Targeted Web verification: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~SystemStatus" --no-restore --verbosity minimal` passed `4/4`.
- ClientApp verification: `npm test -- --run src/api/systemStatusApi.test.ts` passed `6/6`; `npm run typecheck` passed; `npm run build` passed and emitted `system-status`, `graph-workbench`, and shared `assets/client.js` bundles.
- Diff hygiene: `git diff --check` passed with only LF/CRLF warnings for generated assets.
- Full solution verification was attempted with `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`; it still fails in the pre-existing `Neo4jGraphStoreSourceTests.GetPopularLabelsAsync_FiltersUnwoundLabelsAfterWithClause` source-string assertion, outside this feature path and with no diff to `Neo4jGraphStore.cs` or that test.

## Source Documents

- Spec: [Server Operational Readiness Design](../../specs/2026-05-24-server-operational-readiness-design.md)
- Visual: [System Status UI Concepts HTML](../../visuals/system-status-ui-concepts.html)
- Visual: [System Status UI Concepts Screenshot](../../visuals/system-status-ui-concepts.png)
- Plan: [Server Operational Readiness Implementation Plan](../../plans/2026-05-24-server-operational-readiness-implementation-plan.md)

## Related Problems

- None discovered for this requirement thread.

## Notes

- Final integrated review found and fixed a conversion queue false healthy gap: failed conversion rows now degrade `document-conversion-queue`, and rerank impact wording is aligned to `Rerank Quality`.
- Vite multi-entry build emits a required shared `wwwroot/assets/client.js` chunk; both graph and system status entry bundles reference it.
