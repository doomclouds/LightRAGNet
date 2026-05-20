# Server Tests Real RAG Storage Isolation

- Date: `2026-05-20`
- Topic slug: `server-tests-real-rag-storage-isolation`
- Status: `Captured`
- Scope: `Repo`
- Tags: `server-tests`, `qdrant`, `neo4j`, `test-isolation`, `clear-all`, `data-loss`

## Symptom

执行一次全量测试后，本机开发用 Qdrant / Neo4j 数据被清空或明显减少。用户观察到“测试跑完数据库没内容了”。

## Trigger / Context

`LightRAGNet.Server.Tests` 通过 `LightRagServerFactory` 启动真实 Server DI。测试会调用 `/api/MarkdownDocuments/clear-all`，而控制器里的 clear-all 逻辑会：

- 删除所有 `lightrag_vdb_dotnet_` 前缀的 Qdrant collections。
- 对 Neo4j 执行 `MATCH (n) DETACH DELETE n`。

如果测试宿主继承了本机真实 Qdrant / Neo4j 连接，这些清理操作会直接作用到开发数据库。

## Root Cause

Server 测试没有把外部 RAG 存储边界隔离掉。`LightRagServerFactory` 只隔离了 EF Core SQLite 和 `LightRAG:WorkingDir`，但仍让真实 Qdrant / Neo4j 服务注册保留在测试 DI 中；同时 `clear-all` 的外部存储清理逻辑直接从 `IServiceProvider` 解析 `QdrantClient` 和 `IDriver`，缺少可替换的测试 seam。

## Fix

- 新增 `IRagExternalStorageCleaner` 抽象。
- 生产注册 `RagExternalStorageCleaner`，保留真实 clear-all 行为。
- `MarkdownDocumentsController` 改为通过 `IRagExternalStorageCleaner` 清理外部存储，不再直接解析 `QdrantClient` / `IDriver`。
- `LightRagServerFactory` 默认替换为 no-op cleaner。
- `LightRagServerFactory` 移除 Server 测试中的 `QdrantClient`、`IDriver` 和后台 `IHostedService`。
- `LightRagServerFactory` 将 `IVectorStore`、`IGraphStore` 替换成 throwing stubs；测试误触外部 RAG 存储时应立即失败，而不是连接真实库。
- 新增 `ClearAllData_UsesInjectedExternalStorageCleaner` 回归测试，固定 clear-all 使用注入式外部清理器。

## Why This Fix

测试环境隔离必须默认安全，不能依赖“开发者刚好没开真实服务”或“测试账号密码刚好不对”。no-op cleaner 阻断已知危险操作，throwing stubs 阻断未来误入真实 RAG 流程，移除 hosted service 避免后台任务在测试中消费队列并触达外部存储。

## Recognition Clues

- 全量测试后真实 Qdrant collections 数量或点数发生变化。
- 测试里存在 `/clear-all`、bulk delete、`DeleteCollectionAsync`、`MATCH (n) DETACH DELETE n`。
- 测试 factory 只隔离了关系型测试库或临时目录，却保留了 Qdrant / Neo4j / 外部向量库 client。
- 测试通过 `WebApplicationFactory` 启动完整 Server DI，并且没有移除后台 worker。

## Applicability / Non-Applicability

### Applies When

- Server/API 测试会启动真实应用 DI。
- 被测接口包含清库、批量删除、后台任务消费、重建索引或外部存储写入。
- 本机开发服务与测试默认配置共用同一端口、账号或集合前缀。

### Does Not Apply When

- 测试明确是手动 opt-in 的外部存储集成测试，且使用随机 workspace / collection，并且清理范围被限制在该随机资源内。
- 测试只使用 in-memory/testcontainer/临时数据库，生命周期完全由测试拥有。

## Related Artifacts

- Related Problems:
  - [server filesystem test parallelism](./2026-05-19-server-filesystem-test-parallelism-problem.md)
  - [document deletion review gaps](./2026-05-18-document-deletion-review-gaps-problem.md)
- Code or Test:
  - [MarkdownDocumentsController.cs](../../../../src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs)
  - [RagExternalStorageCleaner.cs](../../../../src/LightRAGNet.Server/Services/RagExternalStorageCleaner.cs)
  - [LightRagServerFactory.cs](../../../../tests/LightRAGNet.Server.Tests/LightRagServerFactory.cs)
  - [MarkdownDocumentsControllerTests.cs](../../../../tests/LightRAGNet.Server.Tests/MarkdownDocumentsControllerTests.cs)
