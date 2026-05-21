# Document Intake Pipeline Parity Design

- Date: `2026-05-21`
- Topic slug: `document-intake-pipeline-parity`
- Status: `Ready for review`
- Scope: `Server document intake APIs + background processing pipeline + status tracking + basic Web operations`
- Tags: `lightrag-alignment`, `document-intake`, `pipeline`, `server-api`, `sqlite`, `tdd`

## Purpose

LightRAGNet 已经在 query、cache、retrieval context、rerank 等链路上连续补齐 Python LightRAG 的核心语义。下一块最高价值差距不在单点检索算法，而在“文档接入到索引完成”的产品级闭环。

Python LightRAG Server 已经把文档接入做成了可追踪的后台流水线：提交文档后立即返回，后台处理，用户可以按 `track_id` 查看批次状态，失败后重试，必要时取消。这个能力决定了系统是否能稳定承载真实导入任务。当前 .NET 侧如果继续只把“上传/插入”当同步动作，后续多格式解析、批量导入、失败恢复、UI 状态展示都会缺少可靠底座。

本阶段目标是先做一个小而硬的 document intake pipeline parity：不追求首版多模态满配，不追求复杂并发吞吐，而是把提交、排队、状态、后台处理、重试、取消、分页查询这些主合同定下来。后续 PDF/Office/image/table/formula 等解析能力可以挂到同一条流水线上。

## Python Reference Semantics

Python LightRAG 的相关语义分布在 Server API 与 Core pipeline 中：

- Server 提供 Web UI/API，用于 document indexing、KG exploration 和 RAG query。
- Core 暴露 `apipeline_enqueue_documents` 与 `apipeline_process_enqueue_documents`，把提交和处理拆开。
- Document routes 提供状态追踪能力，包括 track status、paginated documents、failed reprocess、pipeline cancel 等。
- 多格式文档处理可以通过 RAG-Anything 扩展，但它不是首版底座的必要条件。

这轮不要求 .NET 路由逐字照搬 Python。我们对齐的是产品语义：异步提交、批次追踪、文档级状态、失败隔离、重试和取消。

## Current .NET Gap

当前 .NET 已有这些基础：

- `LightRAGNet.Server` 已有 ASP.NET Core API、SignalR、EF Core migration 和 SQLite-backed document metadata service。
- `LightRAGNet.Web` 已有 Blazor Server UI，可以承接基础文档状态操作。
- Core 中已有 document processing、task queue、storage/provider 等模块边界。
- 项目已经形成了 TDD + spec/plan/archive 的工作流。

但与 Python document intake pipeline 相比仍有明显缺口：

- 文档提交缺少稳定的 `track_id` 批次语义。
- 文档状态还没有形成可查询、可分页、可重试、可取消的主合同。
- 上传/文本输入与后台索引处理之间缺少明确队列边界。
- 失败恢复语义不够产品化：失败原因、失败阶段、可重试状态和重试入口需要统一。
- UI 如果直接围绕同步上传构建，后续会被后台处理状态反复推翻。

## Product Decision

首版做“Pipeline 底座优先”：

- 支持文件上传和直接文本输入。
- 支持单个提交和批量提交。
- 每次提交生成一个 `track_id`。
- 每个文档生成独立 `doc_id`，独立状态、错误和处理阶段。
- 提交接口只表示 accepted/enqueued，不表示 indexing complete。
- 后台 worker 自动处理队列。
- 首版 worker 使用单 worker 顺序处理，避免过早引入并发图合并、分布式锁和复杂竞态。
- 用户可见状态主源使用现有 SQLite 文档表扩展字段。
- API 能力对齐 Python 语义，但路由命名采用 .NET Server 现有风格。
- Web UI 首版只做基础状态列表、错误查看、失败重试、取消，不做复杂上传工作台。

## Architecture

目标结构：

```text
Server API
  -> DocumentIntakeService
       - validate request
       - create track_id
       - persist document rows in SQLite
       - enqueue doc ids

Background Worker
  -> DocumentPipelineQueue
  -> DocumentPipelineProcessor
       - load pending document metadata
       - mark stage/status transitions
       - call existing LightRAG document insertion/indexing pipeline
       - persist success/failure/cancel state

Web UI
  -> document list/status APIs
  -> retry/cancel APIs
```

`DocumentIntakeService` 只负责接入和排队，不直接做 heavy indexing。`DocumentPipelineProcessor` 负责状态机推进和调用现有 Core 能力。SQLite document table 是 Server/UI 查询主状态源；Core 内部 KV/doc status 可以同步，但不作为首版用户可见查询主入口。

## Input Contract

首版支持四种输入形态：

- single file upload
- batch file upload
- single text input
- batch text input

提交结果：

```text
track_id: one id per submission request
documents: doc_id + initial status for each accepted document
```

同一批次中的文档相互独立：

- 一个文档失败不阻塞同批次其他文档继续处理。
- 一个文档取消不影响已经完成的文档。
- 批次状态由文档状态聚合得到，不额外引入一张必须维护一致性的 batch state 表，除非实现时现有 schema 已有天然承载点。

首版格式边界：

- 文件上传先支持现有系统能稳定处理的文本类输入。
- 非文本、多模态、Office/PDF 深解析不进入首版。
- 解析器边界要预留：后续可以把 file -> text/chunks 的解析能力挂入 pipeline stage。

## Status Model

用户可见状态至少包含：

```text
Queued
Processing
Completed
Failed
Cancelled
```

推荐内部阶段至少包含：

```text
Accepted
Parsing
Indexing
Persisting
Completed
Failed
Cancelled
```

SQLite 文档元数据建议扩展字段：

```text
TrackId
DocumentId
Status
Stage
ErrorMessage
CreatedAt
UpdatedAt
StartedAt
CompletedAt
CancelledAt
RetryCount
```

字段命名可以按现有 EF entity 风格调整，但语义必须稳定。`Status` 面向用户/API，`Stage` 面向诊断和 UI 细节展示。

## Processing Contract

提交接口必须快速返回：

- 校验输入。
- 持久化 document metadata。
- 入队 document id。
- 返回 `track_id` 和文档初始状态。

后台处理规则：

- 单 worker 顺序处理，一次只处理一个文档。
- worker 从 SQLite/queue 中获取可处理文档。
- 状态转换要先落库，再进入对应 heavy stage。
- 每个文档失败只标记该文档 `Failed`，记录错误和阶段，然后继续处理下一个文档。
- worker 重启后的恢复策略应能重新发现 `Queued` 文档；对于中断在 `Processing` 的文档，首版启动恢复时统一标记为 `Failed`，记录恢复错误摘要，交给用户或 API 显式重试，避免自动重入造成重复索引。

## Retry Contract

重试入口只允许这些状态：

- `Failed`
- `Cancelled`

重试行为：

- 保留原 `doc_id` 和 `track_id`，增加 `RetryCount`。
- 清空或覆盖上一轮错误信息。
- 将状态放回 `Queued`。
- 后台 worker 自动重新处理。

首版不做“复制出一个新文档版本”的重试模型。那会让 UI 和 reference 语义变复杂，收益不够。

## Cancel Contract

取消是协作式取消，不承诺中断任意底层 provider 调用：

- `Queued` 文档：直接标记为 `Cancelled`，不再处理。
- `Processing` 文档：记录 cancel requested，在阶段边界检查并停止。
- `Completed` 文档：不允许取消。
- `Failed` / `Cancelled` 文档：取消请求应返回幂等结果或明确 no-op。

批次取消可以作为 API 便利入口：按 `track_id` 批量取消未完成文档。本质仍是文档级取消。

## API Boundary

首版 API 对齐 Python 语义，采用 .NET 路由风格。推荐能力：

```text
POST   /api/documents/upload
POST   /api/documents/text
GET    /api/documents/tracks/{trackId}
GET    /api/documents
POST   /api/documents/{documentId}/retry
POST   /api/documents/{documentId}/cancel
POST   /api/documents/tracks/{trackId}/cancel
```

查询能力：

- `GET /api/documents/tracks/{trackId}` 返回该批次所有文档状态和聚合摘要。
- `GET /api/documents` 支持分页。
- `GET /api/documents` 支持按状态、track id、创建时间过滤。

响应模型必须区分：

- submission accepted
- document queued
- document processing
- document completed
- document failed with error
- document cancelled

## Web UI Boundary

首版 Web UI 只做基础可操作闭环：

- 文档状态列表。
- 按状态筛选。
- 查看错误信息和当前阶段。
- 对失败/取消文档触发重试。
- 对排队/处理中/批次触发取消。

不做复杂上传工作台、不做多格式解析预览、不做导入向导、不做实时图形化 pipeline timeline。UI 可以轮询 API；是否接 SignalR 实时推送由实现计划根据现有成本决定，不作为首版必须项。

## Data Consistency Boundary

SQLite 是首版用户可见状态的 source of truth：

- 分页查询、track 查询、重试和取消都以 SQLite document metadata 为准。
- 内部 KV/doc status 如果存在，只作为 Core 处理过程中的辅助状态或同步目标。
- 不引入两个并列主状态源。

状态更新必须满足：

- 单文档状态转换原子化。
- 失败时错误信息可追踪。
- 重试后不会误显示上一轮错误为当前错误。
- 取消请求不会让已完成文档回退。

## Error Handling

首版错误策略：

- 输入校验失败：提交接口返回 4xx，不创建文档记录。
- 单个 batch 中部分文档无效：整批拒绝，避免同一 `track_id` 下 accepted/rejected 语义复杂化。
- 处理失败：文档标记 `Failed`，保留错误摘要和失败阶段。
- worker 异常：捕获到文档级失败；无法归属到文档的 worker 级异常写日志，worker 继续运行或由 host 重启。
- 重试失败：递增 `RetryCount`，覆盖最新错误摘要，保留更新时间。

错误摘要不应泄露 API key、connection string 或完整 provider request payload。

## Out of Scope

- 不实现完整 RAG-Anything 多模态解析。
- 不支持 PDF/Office/image/table/formula 的深解析语义。
- 不做多 worker 并发处理。
- 不做分布式队列、跨进程锁或集群调度。
- 不改变 query pipeline、rerank、context builder 或 prompt 模板。
- 不要求和 Python API 路由逐字一致。
- 不做复杂上传向导或实时 pipeline 可视化。
- 不引入外部数据库作为状态主源。

## Testing Strategy

Use strict TDD. No production code before a failing test.

Server/API tests:

- submitting single text creates one `track_id`, one `doc_id`, and `Queued` status
- submitting batch text creates one `track_id` and multiple independent document rows
- uploading files follows the same track/document status contract
- track status returns all documents for the batch
- document list supports pagination and status filtering
- retry is allowed only for `Failed` and `Cancelled`
- cancel is allowed for `Queued` and cooperatively requested for `Processing`
- completed documents cannot be cancelled

Service/state tests:

- enqueue persists document metadata before queue processing
- single worker processes documents sequentially
- one failed document does not stop the next document
- processing failure records `Failed`, `Stage`, and `ErrorMessage`
- startup recovery marks interrupted `Processing` documents as `Failed` instead of automatically requeueing them
- retry keeps `doc_id` and `track_id`, increments `RetryCount`, and resets status to `Queued`
- queued cancellation prevents processing
- processing cancellation is observed at a stage boundary

Web tests:

- document status page renders queued/processing/completed/failed/cancelled states
- retry action is only available for retryable states
- cancel action is only available for cancellable states
- error details are visible for failed documents

Test isolation:

- API tests must not touch real developer Qdrant/Neo4j data.
- Background worker tests should use fakes/no-op processors unless explicitly marked integration.
- Full `dotnet test LightRAGNet.slnx` must not require external RAG storage.

## Acceptance Criteria

The requirement is complete when:

- Document submission returns `track_id` immediately without waiting for indexing completion.
- Every accepted document has an independent `doc_id` and user-visible status.
- Background processing runs automatically through the queued documents.
- Track status and paginated document status APIs work from SQLite.
- Failed and cancelled documents can be retried.
- Queued and processing documents can be cancelled according to cooperative semantics.
- Basic Web UI can inspect status, error, retry, and cancel.
- Tests cover API contracts, status transitions, retry/cancel semantics, and worker failure isolation.

## Implementation Planning Notes

Recommended implementation slices:

1. Data model and API contract tests.
2. Intake service and SQLite status persistence.
3. Single-worker queue and processor orchestration.
4. Retry/cancel semantics.
5. Basic Web UI status operations.
6. Integration cleanup, migration review, and asset closeout.

This order keeps the state contract stable before attaching heavy document processing. The boring foundation is the point here; once this is solid, later multimodal support has somewhere sane to land.
