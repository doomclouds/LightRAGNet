<p align="right">
  <a href="./README.md">中文</a> | <a href="./README.EN.md">English</a>
</p>

<h1 align="center">LightRAGNet</h1>

<p align="center">
  面向 .NET 10 的 LightRAG 工程化实现：文档接入、知识图谱、向量检索、RAG Chat 与图谱工作台。
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4">
  <img alt="Blazor Server" src="https://img.shields.io/badge/UI-Blazor%20Server-5C2D91">
  <img alt="React Graph Workbench" src="https://img.shields.io/badge/Graph-React%20%2B%20Sigma-00A3FF">
  <img alt="Storage" src="https://img.shields.io/badge/Storage-Qdrant%20%2B%20Neo4j-19A974">
  <img alt="Built with OpenAI Codex" src="https://img.shields.io/badge/Built%20with-OpenAI%20Codex-111111?logo=openai&logoColor=white">
  <a href="./LICENSE"><img alt="License" src="https://img.shields.io/badge/License-MIT-blue"></a>
</p>

<p align="center">
  <img src="./docs/assets/readme/hero.png" alt="LightRAGNet product overview" width="960">
</p>

## 这是什么

LightRAGNet 是一个参考 Python LightRAG 架构与产品语义的 .NET 实现。它把知识图谱检索、向量检索、文档生命周期、后台任务队列、流式问答、引用追踪和图谱治理工作台收在同一个可运行工程里，目标不是只做一个算法 demo，而是做成可以继续扩展的 RAG 应用骨架。

适合这些场景：

- 在 .NET 技术栈里构建 LightRAG 风格的知识库问答系统。
- 把 PDF、DOCX、Markdown、text 文档接入到可追踪的 RAG pipeline。
- 同时使用 Qdrant 向量存储和 Neo4j 图存储，验证 KG + Vector 的混合检索效果。
- 需要一个带 Web UI、API、SignalR 任务状态和图谱工作台的完整工程作为二次开发底座。

## 核心能力

| 能力 | 当前状态 | 说明 |
| --- | --- | --- |
| 文档接入 | 已支持 | Markdown、text、PDF、DOCX 上传；原始文件 artifact 与 `converted.md` 持久化。 |
| 后台任务队列 | 已支持 | 文档入库走后台队列，支持状态追踪、重试、取消、删除与恢复。 |
| 检索模式 | 已支持 | `Local`、`Global`、`Mix`、`Hybrid`、`Naive`、`Bypass`。 |
| KG + Vector 查询 | 已支持 | Neo4j 图检索与 Qdrant chunk 向量检索组合，支持 rerank 与引用返回。 |
| RAG Chat | 已支持 | Blazor Chat 工作台，支持 streaming、cacheable、references、diagnostics 与 raw retrieval data。 |
| 图谱工作台 | 已支持 | Blazor 承载 React/Vite + Sigma 图谱工作台，支持节点/边展示、节点属性面板、搜索、设置、全屏与实体合并语义。 |
| 缓存与一致性 | 已支持 | 查询阶段与索引阶段 LLM cache，workspace revision 防止文档变更后命中旧答案。 |
| 测试安全边界 | 已固化 | 默认测试不允许触碰本机真实 Qdrant / Neo4j 数据。 |

## 架构概览

<p align="center">
  <img src="./docs/assets/readme/architecture.png" alt="LightRAGNet architecture overview" width="960">
</p>

这张图按当前代码边界画，看的时候抓三条主线就行：

- 文档入库线：`Document Upload -> ASP.NET Core API -> DocumentIntakeService -> DocumentConversionProcessor -> RagTaskQueueService -> RagTaskProcessorService -> LightRAG -> Qdrant / Neo4j / JSON KV / File Artifacts`。
- 查询回答线：`RAG Chat -> ASP.NET Core API -> LightRAG -> RetrievalContextService -> LLM Provider`，检索上下文会同时读取 Qdrant chunk 向量和 Neo4j 图谱。
- 图谱治理线：`React Graph Workbench -> ASP.NET Core API -> GraphCurationService -> Neo4j / Qdrant`，用于实体编辑、合并、删除以及相关索引更新。

`TaskStatusHub` 负责把后台任务状态推回 Web UI，SQLite 只保存 Server 侧文档元数据、转换状态和 RAG 状态，不承担向量或图谱存储。

主要项目分层：

- `src/LightRAGNet.Core`：核心接口、模型与通用工具。
- `src/LightRAGNet.Share`：Web / Server 共享 DTO 与事件合同。
- `src/LightRAGNet`：LightRAG 主类与文档处理、图谱合并、检索上下文、查询缓存、任务队列等核心服务。
- `src/LightRAGNet.LLM`、`Embedding`、`Rerank`、`Storage`：模型 provider 与存储 provider 实现。
- `src/LightRAGNet.Hosting`：依赖注入入口。
- `src/LightRAGNet.Server`：ASP.NET Core API、SignalR hub、SQLite 文档元数据与 EF Core migrations。
- `src/LightRAGNet.Web`：Blazor Server UI，包含 React/Vite 图谱工作台 island。
- `tests/`：核心、Server 与 Web 测试。

## 图谱工作台

<p align="center">
  <img src="./docs/assets/readme/graph-view-functional-parity.png" alt="LightRAGNet graph workbench" width="960">
</p>

图谱工作台是这个项目现在比较能代表“做到哪一步”的地方。它不是一张静态图，而是 Web UI 里真实跑起来的 Knowledge Graph 页面：左侧还是 LightRAGNet 的主导航，中间是 Sigma 图谱画布，上方可以按 label、depth、nodes 取子图，画布工具栏支持布局、缩放、定位、颜色语义和全屏这类图谱操作。

我的目标不是把 Neo4j Browser 包一层，而是把 Python LightRAG WebUI 里那套更适合 RAG 用户的图谱治理体验搬到 .NET 项目里：查询时能看引用，入库后能看图谱，后续再逐步把实体合并、属性查看、关系编辑这些能力补完整。

## 快速开始

前置依赖：

- .NET 10 SDK
- Docker Desktop
- Node.js，用于构建 React 图谱工作台

1. 恢复并构建：

```powershell
dotnet restore LightRAGNet.slnx
dotnet build LightRAGNet.slnx
```

2. 启动本机 Qdrant 和 Neo4j：

```powershell
docker compose up -d
```

3. 配置 Server。建议复制到 `src/LightRAGNet.Server/appsettings.Development.json`，不要把真实 key 提交进仓库：

```json
{
  "LLM": {
    "BaseUrl": "https://api.deepseek.com",
    "ApiKey": "your-llm-api-key",
    "ModelName": "deepseek-v4-flash"
  },
  "Embedding": {
    "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode",
    "ApiKey": "your-embedding-api-key",
    "ModelName": "text-embedding-v4",
    "Dimension": "2048"
  },
  "Rerank": {
    "BaseUrl": "https://dashscope.aliyuncs.com/api/v1/services/rerank/text-rerank/text-rerank",
    "ApiKey": "your-rerank-api-key",
    "ModelName": "qwen3-rerank"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": "6334",
    "EmbeddingDimension": "2048"
  },
  "Neo4j": {
    "Uri": "neo4j://localhost:7687",
    "User": "neo4j",
    "Password": "your-neo4j-password"
  }
}
```

4. 启动 API Server 和 React 前端：

```powershell
.\scripts\dev-start.ps1
```

默认地址：

- API Server: `http://localhost:5261`
- React UI: `http://127.0.0.1:5173/documents`
- Qdrant REST: `http://localhost:6333`
- Neo4j Browser: `http://localhost:7474`

停止开发服务：

```powershell
.\scripts\dev-stop.ps1
```

单独启动某一侧：

```powershell
.\scripts\dev-start.ps1 -Target Server
.\scripts\dev-start.ps1 -Target React
```

如果在 Git Bash 中运行，可以使用 `.sh` 包装脚本：

```bash
./scripts/dev-start.sh
./scripts/dev-start.sh -Target React
./scripts/dev-stop.sh
```

也可以分步启动：

```powershell
dotnet run --project src/LightRAGNet.Server
npm run dev --prefix src/LightRAGNet.React
```

## Web 使用路径

1. 打开 Web UI。
2. 在 Documents 页面上传 Markdown、text、PDF 或 DOCX。
3. 对文档执行 `Add to RAG`，观察后台任务状态。
4. 在 Chat 页面选择 query mode、response type、TopK、ChunkTopK、Rerank 与 References。
5. 查看 assistant 回复中的引用、诊断信息和 raw retrieval data。
6. 在 Knowledge Graph 页面检查实体、关系和图谱结构。

## C# 使用示例

```csharp
using LightRAGNet;
using LightRAGNet.Core.Models;
using LightRAGNet.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection();
services.AddLightRAG(configuration);

await using var provider = services.BuildServiceProvider();
var rag = provider.GetRequiredService<LightRAG>();

var docId = await rag.InsertAsync(
    "LightRAGNet connects documents, vector retrieval, and knowledge graphs.",
    filePath: "intro.md");

var result = await rag.QueryAsync(
    "LightRAGNet 适合解决什么问题？",
    new QueryParam
    {
        Mode = QueryMode.Mix,
        TopK = 10,
        ChunkTopK = 5,
        EnableRerank = true,
        IncludeReferences = true,
        Stream = false
    });

Console.WriteLine(result.Content);
```

## 开发命令

```powershell
dotnet restore LightRAGNet.slnx
dotnet build LightRAGNet.slnx
dotnet test LightRAGNet.slnx
dotnet run --project src/LightRAGNet.Server
dotnet run --project src/LightRAGNet.Web
```

## 测试安全边界

`dotnet test LightRAGNet.slnx` 必须可以安全运行，默认不允许删除或修改本机开发用 Qdrant / Neo4j 数据。

- Server/API 测试默认使用内存 SQLite、临时工作目录、no-op 外部存储清理器和测试 double。
- `LightRagServerFactory` 会移除真实 `QdrantClient`、`IDriver` 和后台 `IHostedService`，并用 throwing `IVectorStore` / `IGraphStore` 阻止误触真实 RAG 存储。
- 需要真实 Qdrant / Neo4j 的集成测试必须显式 opt-in，使用唯一 workspace / collection，并且只清理测试自己创建的资源。
- 涉及 `clear-all`、批量删除、集合删除、图数据库清空或后台任务消费的测试，必须先证明环境隔离。

## 当前边界

- 这是开发阶段项目，不建议直接把默认配置暴露到公网。
- Provider 默认配置偏向 DeepSeek、DashScope、Qdrant、Neo4j；其他 provider 可以按现有接口继续扩展。
- 图谱工作台对齐 Python LightRAG WebUI 的产品语义，后续仍可继续补齐更完整的图谱治理能力。
- Docker Compose 中的本机路径、端口和 Neo4j 默认账号需要按团队环境复核。

## 路线图

- 更完整的 provider 抽象与配置模板。
- 更细的文档 intake 可观测性与失败恢复体验。
- 图谱编辑、合并、过滤、布局与大图性能持续增强。
- 面向真实部署环境的初始化向导、健康检查和权限边界。
- RAG 评估、Tracing 与更多数据集验证。

## 与 Python LightRAG 的关系

LightRAGNet 参考并移植了 Python LightRAG 的核心架构、检索模式和 WebUI 图谱工作台语义。这个仓库的目标是在 .NET 生态里复现并工程化这些能力，而不是声称图谱治理设计完全原创。

参考来源：

- Python LightRAG: `https://github.com/HKUDS/LightRAG`
- 本仓库本地参考副本：`LightRAG/`
- 详细系统介绍：[LightRAGNet-System-Introduction.CN.md](./LightRAGNet-System-Introduction.CN.md)
- RAG 任务队列方案：[RAG-Task-Queue-Processing-Solution.CN.md](./RAG-Task-Queue-Processing-Solution.CN.md)

## License

[MIT](./LICENSE)
