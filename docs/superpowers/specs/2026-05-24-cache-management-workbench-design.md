# Cache Management Workbench Design

- Date: `2026-05-24`
- Topic slug: `cache-management-workbench`
- Status: `Ready for review`
- Scope: `Evidence-driven cache metrics API + React cache management workbench`
- Tags: `cache-management`, `operations`, `react-island`, `query-cache`, `indexing-cache`, `metrics`, `tdd`

## Purpose

LightRAGNet 已经对齐了 Python LightRAG 的查询缓存、关键词缓存、索引阶段 entity extract cache、summary cache 和 workspace query revision。当前缺口不是“有没有缓存”，而是用户无法判断缓存系统到底好不好用：

- 缓存命中率是多少？
- 哪类缓存最有价值？
- 缓存省掉了多少 provider 调用和等待时间？
- 哪些缓存已经陈旧、低价值或有污染风险？
- 清理某类缓存会影响哪些能力？

本阶段要做一个 Web 可见、证据驱动的 `Cache Management` 工作台：

```text
Open Web Cache Management
  -> request cache overview API
  -> backend returns metrics, entry summary, efficiency evidence, and clear plan
  -> React page renders hit rate, saved calls, risks, trends, and safe clear actions
  -> destructive operations require explicit review/confirmation
```

这个页面不是 KV 文件浏览器，也不是几个清理按钮。它的价值必须体现在：

- 一眼看到 overall cache hit rate。
- 能分辨 `query`、`keywords`、`extract`、`summary` 的收益和风险。
- 能看到 provider calls avoided 和 estimated latency saved。
- 能知道哪些 cache 可以清、清了会损失什么。
- 前端不自行估算命中率；所有效率指标来自后端记录的 cache attempt / hit / miss 证据。

## Product Decisions

- 首版必须包含 Web UI；只做后端 API 不算完成。
- 页面主体使用 React island，保持与 Graph Workbench、System Status 的产品方向一致。
- Blazor 只负责薄 host 页面和左侧导航入口。
- 后端负责 cache 统计、命中率计算、风险判断、清理计划和脱敏。
- React 只展示后端 DTO，不重算 hit rate、saved calls、risk 或 clear plan。
- 首版聚焦 `llm_cache` 系列：`query`、`keywords`、`extract`、`summary` 和 query revision metadata。
- 首版记录 rolling metrics，用于回答“缓存效率高不高”；不能只扫描 KV entry count。
- 首版允许清理缓存，但每个清理动作必须展示影响面。
- 首版不展示完整 prompt、return value、provider response 或任何 secret。
- 首版不做自动清理调度，不做成本金额估算，不接入外部计费 API。

## Visual Reference

设计讨论阶段产出一个深色高保真 HTML 原型：

- Visual: [cache-management-ui-concept.html](../visuals/cache-management-ui-concept.html)

正式实现采用原型中的 `运维工作台` 方向，并保留这些关键结构：

- 顶部 summary：overall hit rate、provider calls avoided、estimated latency saved、stale / risky entries。
- `Cache families`：按 query / keywords / extract / summary 展示命中率、hits / attempts、entry count、value 和 risk。
- `What should I do?`：后端生成维护建议，不由前端猜。
- `Efficiency trend`：按时间窗口展示命中率和 saved calls。
- `Clear plan`：展示可执行清理动作和影响面。
- `Measurement contract`：明确页面数据来自后端 metrics，而不是从 entry count 推断。
- `Entry drilldown`：只展示安全摘要，不展示完整 prompt 或 response。

## Current State

当前系统已有：

- `LightRagLlmCacheService` 负责 keyword cache、query response cache、extract cache、summary cache 和 workspace query revision。
- `LightRagCacheKeyBuilder` 已定义 `query`、`keywords`、`extract`、`summary`、`metadata` 类型。
- `llm_cache` 存储使用 keyed `IKVStore`，生产默认 JSON KV store。
- 文档删除和 clear-all 已能按请求删除 linked extract cache，并 bump workspace query revision。
- Chat 查询 metadata 能展示部分 cache policy，但不是系统级缓存效率指标。
- Web 已有 Blazor shell、导航、Graph React island 和 System Status React island 设计方向。

当前缺口：

- 没有 cache management API。
- 没有 cache metrics store；命中率无法从现有 entry count 准确推断。
- `LightRagLlmCacheService` 命中/未命中只散落在代码路径和日志中，没有结构化记录。
- 没有 workspace/type/time-window 维度的 hit rate、saved calls 或 latency saved。
- 没有清理计划 API，用户不知道哪些缓存可清、清理会影响什么。
- 没有 Web 页面让用户验证缓存功能是否真的有用。

## Scope

首版包含：

| Area | Scope |
| --- | --- |
| Metrics recording | 在 cache read/write 路径记录 attempt、hit、miss、save、duration 和 cache type |
| Metrics store | 增加轻量持久化 store，支持按 workspace、cache type、time window 聚合 |
| Entry summary | 扫描 `llm_cache` entries，按 cache type、workspace、revision state、last observed state 聚合 |
| Overview API | 返回 summary、family metrics、trend、insights、clear plan、entry samples |
| Clear API | 支持清理 stale query cache、unused summary cache、all cache；危险动作要求 confirm |
| Web UI | React Cache Management workbench + Blazor host + nav entry |
| Safety | 响应脱敏，不返回完整 prompt、return value、secret、authorization header |
| Tests | 后端 metrics、clear plan、API 合同、React 渲染和安全边界测试 |

首版不做：

- 外部 provider 成本金额统计。
- 自动定时清理策略。
- cache entry 全文浏览。
- query prompt / response diff 查看。
- Redis/Postgres/Mongo 等非 JSON KV 专属优化。
- 多实例跨进程 metrics 同步。
- 与 System Status 页面强绑定；只保留后续链接集成空间。

## Metric Semantics

### Cache Families

首版 cache family：

```text
query      -> non-streaming KG / Naive / Bypass answer cache
keywords   -> high/low keyword extraction cache
extract    -> indexing entity extraction cache
summary    -> entity/relation description summary cache
metadata   -> workspace query revision metadata, counted but not treated as hit-rate cache
```

`metadata` 不进入 overall hit rate，因为它不是 provider 调用缓存。

### Hit Rate

Hit rate 必须由后端 metrics 记录计算：

```text
hitRate = hits / attempts
attempts = hits + misses
```

如果某个 family 没有 attempt 记录：

- `hitRate` 返回 `null`。
- UI 显示 `Not measured`。
- 不允许从 entry count 推断 hit rate。

### Provider Calls Avoided

首版用 cache hit count 表示 saved calls：

```text
providerCallsAvoided = queryHits + keywordHits + extractHits + summaryHits
```

后端应按 family 分开展示，避免总数误导。

### Estimated Latency Saved

这是估算值，不是精确 SLA：

```text
estimatedLatencySavedMs = sum(hitsByType * recentAverageMissDurationMsByType)
```

如果某类 cache 缺少 miss duration：

- 该 family 的 latency saved 标记为 `NotMeasured`。
- 不参与总估算。
- UI 必须显示 estimate 来源说明。

### Value Level

后端按命中率、attempt 数、saved calls 和 entry count 生成 value level：

| Level | Rule |
| --- | --- |
| `VeryHigh` | attempts 足够且 hit rate >= 80%，或 saved calls 很高 |
| `High` | attempts 足够且 hit rate >= 60% |
| `Medium` | 有稳定命中但收益有限 |
| `Low` | attempts 少、hit rate 低或 entry 多但少命中 |
| `NotMeasured` | 无可靠 metrics |

具体阈值集中在后端配置中，不写死到 React。

### Risk Level

后端识别这些风险：

- `OldRevision`：query cache entry 所属 workspace revision 不是当前 revision。
- `Unused`：超过配置天数未命中。
- `LargeButLowHit`：entry 多但 hit rate 低。
- `DocLinked`：extract cache 与 chunk `llm_cache_list` 关联，清理需要谨慎。
- `Current`：当前有效，无明显风险。

## Backend Architecture

新增服务放在 Server 层和 Core cache 服务之间，避免 UI 直接理解 KV 内部结构。

```text
src/LightRAGNet/
  Services/QueryCache/
    CacheMetricEvent.cs
    ICacheMetricsStore.cs
    JsonCacheMetricsStore.cs
    CacheMetricsRecorder.cs
    CacheMetricsModels.cs

src/LightRAGNet.Server/
  Controllers/
    CacheManagementController.cs
  Services/CacheManagement/
    CacheManagementService.cs
    CacheManagementModels.cs
    CacheEntryInspector.cs
    CacheClearPlanner.cs
```

`LightRagLlmCacheService` 在这些路径记录 metrics：

- `TryGetKeywordsAsync`：attempt + hit/miss + duration。
- `SaveKeywordsAsync`：save + duration。
- `TryGetQueryResponseAsync`：attempt + hit/miss + duration。
- `SaveQueryResponseAsync`：save + duration。
- `TryGetExtractAsync`：attempt + hit/miss + duration。
- `SaveExtractAsync`：save + duration。
- `TryGetSummaryAsync`：attempt + hit/miss + duration。
- `SaveSummaryAsync`：save + duration。

记录失败不能影响原业务路径：

- metrics 写入失败只记录 warning。
- cache hit/miss 原结果照常返回。
- cancellation 继续传递，不吞掉 `OperationCanceledException`。

## Metrics Store

首版使用 JSON metrics store，跟现有本地开发模型一致。

建议文件：

```text
{WorkingDir}/cache_metrics.json
```

记录结构按 append-friendly event model 设计，服务层读取时聚合：

```json
{
  "id": "01HY...",
  "timestamp": "2026-05-24T12:00:00Z",
  "workspace": "_",
  "cacheType": "query",
  "operation": "hit",
  "mode": "Mix",
  "durationMs": 4,
  "providerDurationMs": null,
  "cacheKeyPrefix": "Mix:query:af31",
  "revision": 12
}
```

Allowed operations：

```text
attempt
hit
miss
save
delete
clear
```

Store requirements：

- atomic write。
- bounded retention，默认保留最近 30 天或最近 N 条。
- 读写串行化，避免并发 JSON 损坏。
- metrics event 不包含 prompt、return value、raw provider payload 或 secret。

## API Contract

### Overview

```text
GET /api/cache-management/overview?workspace=_&window=24h
```

Response：

```json
{
  "workspace": "_",
  "window": "24h",
  "generatedAt": "2026-05-24T12:00:00Z",
  "summary": {
    "overallHitRate": 0.784,
    "providerCallsAvoided": 1248,
    "estimatedLatencySavedMs": 2520000,
    "staleOrRiskyEntries": 37,
    "measured": true
  },
  "families": [],
  "trend": [],
  "insights": [],
  "clearPlan": [],
  "entrySamples": []
}
```

Family item：

```json
{
  "cacheType": "extract",
  "displayName": "Entity extract",
  "hitRate": 0.911,
  "hits": 512,
  "misses": 50,
  "attempts": 562,
  "entryCount": 1926,
  "valueLevel": "VeryHigh",
  "riskLevel": "DocLinked",
  "providerCallsAvoided": 512,
  "estimatedLatencySavedMs": 1560000,
  "message": "Extract cache is strongly helping repeated indexing."
}
```

Clear plan item：

```json
{
  "id": "stale-query-cache",
  "title": "Clear stale query cache",
  "cacheTypes": ["query"],
  "entryCount": 19,
  "risk": "Low",
  "impact": "Deletes old workspace revision query answers only.",
  "requiresConfirmation": false
}
```

### Clear

```text
POST /api/cache-management/clear
```

Request：

```json
{
  "workspace": "_",
  "planId": "stale-query-cache",
  "confirm": false
}
```

Response：

```json
{
  "succeeded": true,
  "deletedEntries": 19,
  "cacheTypes": ["query"],
  "message": "Cleared stale query cache entries for workspace _.",
  "revisionAfter": 12
}
```

Dangerous actions：

- `all-cache` must require `confirm = true`。
- Response must include what was deleted, not deleted values。
- Clear action records metrics operation `clear`。

## Clear Plan Rules

首版支持三类操作：

| Plan | Behavior | Confirmation |
| --- | --- | --- |
| `stale-query-cache` | 删除旧 workspace revision 的 query answer cache | No |
| `unused-summary-cache` | 删除超过阈值未命中的 summary cache | Review first |
| `all-llm-cache` | 调用 `llm_cache.DropAsync()` 清空 LLM cache，并保留或重建 revision metadata | Yes |

`all-llm-cache` 的 revision 行为：

- 清空后必须重新写入当前 workspace query revision metadata。
- 不允许因为清空 cache 让 revision 读数回到旧状态。
- UI 要提示重复查询和重复索引效率会下降。

## React Cache Management Workbench

React files：

```text
src/LightRAGNet.Web/ClientApp/src/
  api/
    cacheManagementApi.ts
  cache-management/
    main.tsx
    CacheManagementWorkbench.tsx
    CacheSummaryCards.tsx
    CacheFamilyTable.tsx
    CacheInsights.tsx
    CacheEfficiencyTrend.tsx
    CacheClearPlan.tsx
    CacheEntryDrilldown.tsx
    CacheMeasurementContract.tsx
```

Blazor host：

```text
src/LightRAGNet.Web/Components/Pages/CacheManagement.razor
```

Route：

```razor
@page "/cache-management"
```

Navigation entry：

```razor
<MudNavLink Href="cache-management" Icon="@Icons.Material.Filled.Storage">
    Cache Management
</MudNavLink>
```

Layout：

- Header：`Cache Management`、workspace selector、time window selector、Refresh、Copy JSON。
- Summary cards：overall hit rate、provider calls avoided、estimated latency saved、stale/risky entries。
- Main left：cache family table + efficiency trend。
- Main right：insights + clear plan。
- Bottom：measurement contract + safe entry drilldown。

Allowed interactions：

- `Refresh` reloads overview。
- workspace/time window filters reload overview。
- `Copy JSON` copies the full overview response。
- clear plan action opens confirmation/review state。
- dangerous clear requires explicit confirmation text or checkbox。

Not allowed in v1：

- editing cache entries。
- viewing full prompt or full response。
- clearing external vector/graph/KV stores。
- modifying `EnableLlmCache` options。
- frontend-only hit rate calculation。

Frontend error semantics：

- Overview API unreachable：show frontend network error state and Refresh。
- Family `hitRate = null`：show `Not measured`。
- Clear API failure：show non-destructive error state and keep overview visible。
- Empty metrics store：show entry counts if available, but mark efficiency metrics as `Not measured`。

## Security and Redaction

API may expose：

- cache type。
- hashed key prefix。
- workspace。
- query mode。
- revision。
- entry count。
- hit/miss/save counters。
- duration aggregates。
- stale/risk category。

API must not expose：

- raw prompt。
- raw query text unless explicitly redacted and truncated。
- return value。
- provider response。
- API key、password、token、authorization header。
- full local file paths unless already safe and intended for UI。

Entry drilldown uses `cacheKeyPrefix` and metadata summaries only.

## Testing Strategy

Core tests：

- `LightRagLlmCacheService` records hit/miss/save metrics for query, keywords, extract and summary。
- Metrics recorder failure does not break cache reads or writes。
- `OperationCanceledException` is not swallowed。
- JSON metrics store persists events with atomic write and retention。
- Hit rate is computed from metrics attempts, not entry count。

Server tests：

- `GET /api/cache-management/overview` returns summary, families, trend, insights, clear plan and entry samples。
- Empty metrics store returns `NotMeasured` efficiency but still returns entry summary。
- Query cache old revision entries are counted as stale。
- Clear stale query cache deletes only old revision query entries。
- Clear all requires confirmation。
- Clear all rebuilds or preserves workspace revision metadata。
- API response does not include prompt、return_value、API key、password or token。

Web tests：

- `/cache-management` host page exists and mounts React workbench。
- Navigation contains `Cache Management`。
- Workbench renders summary cards from API data。
- Family table displays `Not measured` when hit rate is null。
- Clear plan opens review/confirmation state。
- React does not calculate hit rate locally from entry count。

Verification command：

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

If full solution verification fails because of pre-existing unrelated tests, final reporting must separate targeted verification from known unrelated failures.

## Acceptance Criteria

- Web navigation includes `Cache Management`。
- `/cache-management` renders a React island workbench。
- `GET /api/cache-management/overview` returns evidence-driven cache efficiency data。
- Summary cards show overall hit rate, saved calls, estimated latency saved and risky entries。
- Cache family table separates query、keywords、extract、summary。
- Hit rate and latency saved are computed from recorded metrics, not from entry count。
- Empty or missing metrics show `Not measured` instead of invented values。
- Clear plan explains impact before deleting cache。
- Dangerous clear-all requires explicit confirmation。
- API and UI never expose full prompt、return value、provider response or secrets。
- Tests cover metrics recording, API contracts, clear safety, and Web rendering。
