# Server Operational Readiness Design

- Date: `2026-05-24`
- Topic slug: `server-operational-readiness`
- Status: `Ready for review`
- Scope: `Evidence-driven system status API + React system status workbench`
- Tags: `server-health`, `operations`, `react-island`, `diagnostics`, `configuration-audit`, `tdd`

## Purpose

LightRAGNet 已经把核心 RAG 主链、文档生命周期、PDF/DOCX intake、query cache、retrieval diagnostics 和 graph workbench 推进到可用状态。下一阶段的高价值缺口不是继续加一个算法点，而是让系统在真实开发和部署环境里更容易判断“现在能不能用、坏在哪里、会影响哪些功能、下一步怎么修”。

本阶段要做一个 Web 可见、证据驱动的 `System Status` 入口：

```text
Open Web System Status
  -> request Server health API
  -> backend runs evidence-based checks
  -> backend returns status, evidence, remediation, and feature impact
  -> React page renders diagnostics without inventing data
```

这个页面不是装饰性仪表盘。它的价值必须体现在：

- 一眼看到 overall status。
- 每个异常都有可追溯 evidence。
- remediation 能指导下一步排障。
- feature impact 能说明哪些功能受影响。
- 没有后端证据的数据不能伪装成正常或异常。

## Product Decisions

- 首版以开发排障为第一目标，同时保留部署探针需要的 overall status。
- 页面主体使用 React island，不再新增 Blazor 页面主体。
- Blazor 只负责薄 host 页面和左侧导航入口。
- 后端负责所有状态判断、证据采集、影响映射和修复建议。
- React 只展示后端 DTO，不重算 overall、`fixFirst` 或 `featureImpacts`。
- 首版只读，不提供 clear all、clear cache、retry worker、修改配置或自动修复。
- 外部模型 provider 不默认真实调用，避免慢、花费额度或产生副作用。
- 没有可靠证据的检查项显示 `NotMeasured`。

## Visual Reference

设计讨论阶段产出一个 HTML 原型：

- Visual: [system-status-ui-concepts.html](../visuals/system-status-ui-concepts.html)
- Screenshot: [system-status-ui-concepts.png](../visuals/system-status-ui-concepts.png)

正式实现采用原型中的 `方案 A · 诊断总览` 方向，但必须做这些修正：

- 移除分数圆环，不做无明确来源的健康分数。
- 用真实 summary 计数展示：`Healthy / Degraded / Unhealthy / NotMeasured`。
- `Fix First`、`Feature Impact`、checks、evidence 和 remediation 全部来自后端。
- 原型中的场景切换只用于设计预览，不进入产品。

## Current State

当前系统已有：

- `LightRAGNet.Server` ASP.NET Core API、SQLite metadata DB、SignalR hub 和 OpenAPI/Scalar。
- `LightRAGNet.Web` Blazor shell、MudBlazor layout、导航菜单和底部 SignalR 状态条。
- `GraphView.razor` 已作为 React island host，挂载 React/Vite graph workbench。
- `DocumentIntakeService`、RAG task queue、document conversion worker 和 SQLite 文档状态模型。
- Qdrant vector store、Neo4j graph store、JSON KV store、DeepSeek LLM、Aliyun Embedding、Aliyun Rerank provider。

当前缺口：

- 没有一个 Web 页面能集中说明 Server、存储、provider 配置、任务队列和 conversion queue 当前状态。
- 没有后端诊断 DTO 能返回结构化 evidence、remediation 和 feature impact。
- 配置缺失、存储断连、工作目录不可写、后台任务卡住等问题仍主要靠日志和人工猜测。
- 现有底部 SignalR 状态只表示 Web 客户端连接状态，不等价于系统健康状态。

## Scope

首版 checks：

| Check | Evidence source | Status rule |
| --- | --- | --- |
| Server API | Health endpoint successfully returns JSON | Endpoint can return means API layer is alive |
| SQLite | EF Core connect + lightweight query | Failure is `Unhealthy` |
| WorkingDir | Create and delete a probe file under configured working directory | Failure is `Unhealthy` |
| Qdrant | Lightweight collection or health-style request through configured client | Failure is `Unhealthy` |
| Neo4j | Open session and run `RETURN 1` | Failure is `Degraded` |
| LLM config | Configuration and API key presence, masked source info | Missing required config is `Unhealthy` |
| Embedding config | Configuration, API key and dimension presence | Missing required config is `Unhealthy` |
| Rerank config | Configuration and API key presence | Missing required config is `Degraded` |
| RAG task queue | Task state store statistics for active, failed and stale tasks | Stale or suspicious active tasks are `Degraded` |
| Conversion queue | SQLite conversion status statistics for queued, processing, failed and stale conversions | Stale or suspicious conversions are `Degraded` |

首版不做：

- 真实调用 LLM、Embedding 或 Rerank provider。
- Docker 容器状态探测。
- SignalR 服务端健康探测。
- OpenAPI/Scalar 健康状态。
- Auth/security 审计。
- Cache management 统计或清理。
- 自动修复、重试、清理或配置修改。
- Kubernetes liveness/readiness endpoints。

## Status Semantics

状态枚举：

```text
Healthy
Degraded
Unhealthy
NotMeasured
```

语义：

- `Healthy`：检查项正常，相关功能可用。
- `Degraded`：部分功能受影响，但系统仍可部分工作。
- `Unhealthy`：核心链路不可用，相关主流程大概率无法正常工作。
- `NotMeasured`：后端没有可靠证据，UI 不能推断状态。

Overall aggregation：

- 任意 check 为 `Unhealthy`，overall 为 `Unhealthy`。
- 否则任意 check 为 `Degraded`，overall 为 `Degraded`。
- 否则存在 measured checks 且全部 `Healthy`，overall 为 `Healthy`。
- 如果没有任何 measured check，overall 为 `NotMeasured`。
- `NotMeasured` 不主动把整体降级为 `Unhealthy`，但会在 summary 中明确计数。

`fixFirst`：

- 只包含 `Unhealthy` 和 `Degraded` checks。
- 先排 `Unhealthy`，再排 `Degraded`。
- 同级按固定 check order 排序，避免页面跳动。

## Backend Architecture

采用插件式 health check 单元，而不是一个巨大的硬编码服务。

```text
src/LightRAGNet.Server/
  Controllers/
    SystemHealthController.cs
  Services/SystemHealth/
    ISystemHealthCheck.cs
    SystemHealthService.cs
    SystemHealthModels.cs
    Checks/
      ServerApiHealthCheck.cs
      SqliteHealthCheck.cs
      WorkingDirHealthCheck.cs
      QdrantHealthCheck.cs
      Neo4jHealthCheck.cs
      LlmConfigHealthCheck.cs
      EmbeddingConfigHealthCheck.cs
      RerankConfigHealthCheck.cs
      RagTaskQueueHealthCheck.cs
      DocumentConversionQueueHealthCheck.cs
```

`ISystemHealthCheck`：

```csharp
public interface ISystemHealthCheck
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}
```

每个 check 自己负责取证和返回初步 result。聚合、超时、异常兜底、summary、overall、`fixFirst` 和 `featureImpacts` 由 `SystemHealthService` 统一处理。

`SystemHealthService` responsibilities：

- 并发执行所有 `ISystemHealthCheck`。
- 给每个 check 套默认 `1500ms` timeout。
- 捕获单项异常，转换为该项对应的 `Degraded` 或 `Unhealthy` result。
- 记录每项 `durationMs` 和总 `durationMs`。
- 生成 summary、overall status、`fixFirst` 和 `featureImpacts`。
- 做最终脱敏兜底，避免 API key、password、token 泄露。
- 单项失败不能导致整个 endpoint 返回 500。

`SystemHealthController`：

```text
GET /api/system/health
```

Controller 只负责调用 service 和返回 DTO，不写检查逻辑。

## API Contract

Response：

```json
{
  "status": "Degraded",
  "generatedAt": "2026-05-24T14:41:16Z",
  "durationMs": 58,
  "summary": {
    "healthy": 6,
    "degraded": 2,
    "unhealthy": 0,
    "notMeasured": 0
  },
  "checks": [],
  "fixFirst": [],
  "featureImpacts": []
}
```

Check result：

```json
{
  "id": "neo4j",
  "name": "Neo4j",
  "category": "Storage",
  "status": "Degraded",
  "message": "Neo4j is not reachable; KG query modes are affected.",
  "evidence": {
    "uri": "neo4j://localhost:7477",
    "probe": "RETURN 1",
    "errorType": "ServiceUnavailable"
  },
  "remediation": "Start Neo4j or update Neo4j:Uri.",
  "affects": ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"],
  "durationMs": 16
}
```

`evidence` requirements：

- `evidence` is a structured object.
- It may contain uri, host, port, model, dimension, working directory, probe name, error type, timeout value and queue counts.
- It must not contain raw API keys, passwords, tokens, connection-string secrets or authorization headers.

`remediation` requirements：

- `remediation` is a single string in v1.
- Do not implement runbook step arrays in v1.

`fixFirst` item：

```json
{
  "checkId": "neo4j",
  "title": "Neo4j",
  "status": "Degraded",
  "remediation": "Start Neo4j or update Neo4j:Uri.",
  "affects": ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"]
}
```

`featureImpacts` item：

```json
{
  "feature": "KG Query Modes",
  "status": "Degraded",
  "reason": "Neo4j is not reachable.",
  "affectedBy": ["neo4j"],
  "links": [
    { "label": "Open Graph", "href": "/graph-view" }
  ]
}
```

Do not add a separate `severity` field in v1. `status` is the single status source of truth.

## Feature Impact Mapping

Feature impact is generated by the backend from checks. React must not infer it locally.

Initial mapping:

| Problem | Feature impact |
| --- | --- |
| SQLite `Unhealthy` | Document list, upload records, conversion status and Web management pages are unreliable |
| WorkingDir `Unhealthy` | KV store, `converted.md`, artifacts and RAG working directory cannot be trusted |
| Qdrant `Unhealthy` | Document indexing, Naive query, Mix vector retrieval and vector recall are unavailable |
| Neo4j `Degraded` | Local, Global, Hybrid, Mix and Graph Workbench are affected; Naive and Bypass can still work |
| LLM config `Unhealthy` | Query generation, entity extraction and summary generation are unavailable |
| Embedding config `Unhealthy` | Document indexing and vector recall are unavailable |
| Rerank config `Degraded` | Query can still run, but rerank quality is reduced |
| RAG task queue `Degraded` | New document indexing may be delayed or stuck |
| Conversion queue `Degraded` | PDF/DOCX conversion may be delayed or stuck |

## Timeout and Reliability

- Default per-check timeout: `1500ms`.
- Target endpoint duration: usually under `2s`.
- Checks run concurrently.
- Timeout on one check becomes that check's result, not endpoint failure.
- A timeout result must include structured evidence such as `timeoutMs`.
- Single-check exceptions are captured into check results.
- The endpoint returns JSON unless the health endpoint itself crashes.
- External model provider calls are not made by default.

## Security and Redaction

Evidence may show:

- URI without password.
- host and port.
- model name.
- embedding dimension.
- configured source name such as `appsettings` or `environment`.
- working directory path.
- queue counts and stale thresholds.
- error type and sanitized error message.

Evidence must not show:

- API key values.
- password values.
- bearer tokens.
- authorization headers.
- full connection strings containing secrets.
- raw provider request/response payloads.

Provider config checks should report:

```json
{
  "configured": true,
  "source": "environment",
  "model": "deepseek-chat",
  "baseUrl": "https://api.deepseek.com"
}
```

They must not report the actual key.

## React System Status Workbench

React files:

```text
src/LightRAGNet.Web/ClientApp/src/
  api/
    systemStatusApi.ts
  system-status/
    main.tsx
    SystemStatusWorkbench.tsx
    SystemStatusSummary.tsx
    SystemStatusChecks.tsx
    SystemStatusFixFirst.tsx
    SystemStatusFeatureImpact.tsx
    SystemStatusEvidence.tsx
```

Blazor host:

```text
src/LightRAGNet.Web/Components/Pages/SystemStatus.razor
```

Route:

```razor
@page "/system-status"
```

Navigation entry:

```razor
<MudNavLink Href="system-status" Icon="@Icons.Material.Filled.MonitorHeart">
    System Status
</MudNavLink>
```

Layout:

- Header: `System Status`, last checked, duration, `Refresh`, `Copy JSON`.
- Summary section:
  - Overall status.
  - Summary counts.
  - Core storage summary.
  - Model provider summary.
  - Workers summary.
- Main left: checks list.
  - Name, status, message, duration.
  - Expandable structured evidence key/value table.
- Main right:
  - `Fix First`.
  - `Feature Impact`.

Allowed interactions:

- `Refresh` calls `GET /api/system/health` again.
- `Copy JSON` copies the full response.
- Feature impact links navigate to related existing pages.
- Evidence sections expand and collapse.

Not allowed in v1:

- Clear all data.
- Clear cache.
- Retry worker.
- Modify config.
- Automatically fix anything.
- Frontend-only health calculation.

Frontend error semantics:

- If the health API cannot be reached, React shows a frontend network error state such as `Server API unavailable`.
- This state is not a backend check and must not be mixed into backend `checks`.
- The page can offer `Refresh`, but cannot invent backend status.

## Testing Strategy

Backend unit tests:

- Each check status rule:
  - SQLite success/failure.
  - WorkingDir writable/not writable.
  - Qdrant success/exception/timeout.
  - Neo4j success/exception/timeout.
  - LLM, Embedding and Rerank config present/missing.
  - RAG task queue active/failed/stale stats.
  - Conversion queue queued/processing/failed/stale stats.
- `SystemHealthService` aggregation:
  - any `Unhealthy` => overall `Unhealthy`.
  - any `Degraded` and no `Unhealthy` => overall `Degraded`.
  - all measured checks `Healthy` => overall `Healthy`.
  - all `NotMeasured` => overall `NotMeasured`.
  - single check exception does not make endpoint fail.
  - `fixFirst` sorting is deterministic.
  - feature impacts are generated by backend rules.

Server API tests:

- `GET /api/system/health` returns JSON.
- Response contains `status`, `generatedAt`, `durationMs`, `summary`, `checks`, `fixFirst` and `featureImpacts`.
- API key, password and token values are not leaked.
- External model providers are not called.
- Qdrant/Neo4j tests use fake checks or test doubles by default; tests must not touch local development Qdrant/Neo4j data.

React/Web tests:

- `/system-status` host page exists.
- React workbench calls `getSystemHealth`.
- Overall, checks, `fixFirst` and `featureImpacts` render from API data.
- Evidence can expand.
- `Copy JSON` action exists.
- API network error shows frontend error state.
- React does not calculate overall, `fixFirst` or feature impact locally.

Verification command:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

If full solution verification fails because of a pre-existing unrelated test failure, final reporting must distinguish pre-existing failure from this feature's targeted verification.

## Acceptance Criteria

- Web navigation includes `System Status`.
- `/system-status` renders a React island status page.
- `GET /api/system/health` returns evidence-driven JSON.
- All v1 checks are represented in `checks`.
- Status, evidence, remediation and affects are generated by backend checks.
- UI displays `NotMeasured` instead of guessing when data is absent.
- UI removes any no-source health score.
- No API key, password or token appears in response JSON.
- Per-check timeout prevents slow storage probes from blocking the whole endpoint.
- Health API does not return 500 because Qdrant, Neo4j or provider config is bad.
- The page provides `Refresh`, `Copy JSON`, evidence expansion and feature impact links.
- The page provides no destructive or mutating operations in v1.
