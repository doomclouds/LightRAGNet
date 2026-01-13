---
title: LightRAG.NET
version: 1.1.0
lastUpdated: 2026-01-11
powerBy: Cursor AI
reviewer: PALINK
---

**[EN](README.md) | [中文](README.CN.md)**

# LightRAG.NET

LightRAG 的 .NET 实现，完全参考 Python 版本的架构和实现逻辑。LightRAG 是一种基于知识图谱的检索增强生成（RAG）系统，通过结合向量检索和图数据库技术，实现了更精准、更结构化的文档检索和知识问答能力。

## 项目结构

```
LightRAGNet/
├── LightRAGNet.Core/          # 核心接口和模型
├── LightRAGNet.LLM/           # LLM服务（Deepseek OpenAI兼容）
├── LightRAGNet.Embedding/     # Embedding服务（阿里云）
├── LightRAGNet.Rerank/        # Rerank服务（阿里云）
├── LightRAGNet.Storage/       # 存储实现（Qdrant + Neo4j + JSON文件）
├── LightRAGNet/               # 核心LightRAG类
├── LightRAGNet.Hosting/       # 依赖注入扩展
└── LightRAGNet.Example/       # 使用示例
```

## 功能特性

### 核心功能

- ✅ **知识图谱构建**：自动从文档中提取实体和关系，构建结构化的知识图谱
- ✅ **文档索引和查询**：支持文档插入、向量化、实体提取和智能查询
- ✅ **任务状态追踪**：提供详细的任务执行状态更新，便于监控和调试
- ✅ **流式输出**：支持流式生成响应，提升用户体验
- ✅ **RAG任务队列管理**：后台任务队列系统，支持优先级管理、重试机制和状态持久化的RAG文档插入任务处理

### 检索模式

- ✅ **Local 模式**：聚焦直接相关的实体和关系，适合精确查询
- ✅ **Global 模式**：多跳图遍历，发现间接关联，适合探索性查询
- ✅ **Mix 模式**：混合知识图谱检索和向量检索，综合多种信息源
- ✅ **Hybrid 模式**：与 Mix 模式使用相同的实现，行为一致
- ❌ **Naive 模式**：仅使用向量检索（当前未实现）
- ❌ **Bypass 模式**：绕过检索直接生成（当前未实现）

### 基础设施

- ✅ **LLM 服务**：OpenAI 兼容 API（Deepseek）作为 LLM
- ✅ **Embedding 服务**：阿里云 Embedding 服务
- ✅ **Rerank 服务**：阿里云 Rerank 服务
- ✅ **向量存储**：Qdrant 向量数据库
- ✅ **图存储**：Neo4j 图数据库
- ✅ **KV 存储**：JSON 文件键值存储

## 配置

编辑 `LightRAGNet.Example/appsettings.json`：

```json
{
  "LLM": {
    "BaseUrl": "https://api.deepseek.com/v1",
    "ApiKey": "your-deepseek-api-key",
    "ModelName": "deepseek-chat"
  },
  "Embedding": {
    "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode",
    "ApiKey": "your-aliyun-embedding-api-key",
    "ModelName": "text-embedding-v2"
  },
  "Rerank": {
    "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode",
    "ApiKey": "your-aliyun-rerank-api-key",
    "ModelName": "gte-rerank-v2"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": "6333"
  },
  "Neo4j": {
    "Uri": "bolt://localhost:7687",
    "User": "neo4j",
    "Password": "password"
  }
}
```

## 使用示例

### 基本使用

```csharp
// 插入文档
var docId = await rag.InsertAsync("文档内容...", filePath: "example.txt");

// 查询（Mix 模式）
var result = await rag.QueryAsync(
    "什么是人工智能？",
    new QueryParam
    {
        Mode = QueryMode.Mix,
        TopK = 10,
        EnableRerank = true
    });

Console.WriteLine(result.Content);
```

### 不同检索模式

```csharp
// Local 模式：精确查询
var localResult = await rag.QueryAsync(
    "查询内容",
    new QueryParam { Mode = QueryMode.Local, TopK = 20 });

// Global 模式：探索性查询
var globalResult = await rag.QueryAsync(
    "查询内容",
    new QueryParam { Mode = QueryMode.Global, TopK = 20 });

// Mix 模式：混合检索（推荐）
var mixResult = await rag.QueryAsync(
    "查询内容",
    new QueryParam { Mode = QueryMode.Mix, TopK = 20, EnableRerank = true });
```

### 任务状态追踪

```csharp
// 订阅任务状态变更事件
rag.TaskStateChanged += (sender, state) =>
{
    Console.WriteLine($"[{state.Stage}] {state.Description} ({state.Current}/{state.Total})");
};

// 插入文档时会触发状态更新
var docId = await rag.InsertAsync("文档内容...");
```

## 依赖项

- **Qdrant.Client**：Qdrant 向量数据库客户端
- **Neo4j.Driver**：Neo4j 图数据库驱动
- **Microsoft.Extensions.Logging**：日志记录
- **Microsoft.Extensions.DependencyInjection**：依赖注入
- **System.Text.Json**：JSON 序列化

## 架构说明

LightRAGNet 采用分层架构设计：

- **应用层**：LightRAG 核心类，协调各个服务组件
- **服务层**：文档处理、知识图谱合并、检索上下文构建
- **基础设施层**：LLM、Embedding、Rerank、存储接口
- **存储层**：Qdrant、Neo4j、JSON 文件

详细架构说明请参考 [LightRAGNet系统介绍](./LightRAGNet-System-Introduction.CN.md)。

## 参考实现

本实现完全参考 Python 版本的 LightRAG：
- `lightrag.py` - 主类实现
- `operate.py` - 核心操作函数
- `prompt.py` - 提示词模板
- `kg/` - 存储实现

## RAG 任务处理

LightRAGNet 实现了一个健壮的后台任务队列系统，用于处理 RAG 文档插入任务。该系统提供：

- **任务队列管理**：支持优先级排序和自动处理的任务排队机制
- **后台处理**：持续运行的后台服务，从队列中取出任务并处理
- **状态持久化**：任务状态保存到磁盘，支持服务重启后恢复
- **进度追踪**：实时进度更新，包含详细的阶段信息
- **重试机制**：失败任务自动重试，支持可配置的重试次数限制
- **文件去重**：基于文件内容哈希防止重复上传

任务处理系统包括：
- `RagTaskQueueService`：管理任务的排队、排序、删除和重试操作
- `RagTaskProcessorService`：后台服务，持续从队列中处理任务
- `RagTaskStateStore`：将任务状态持久化到临时 JSON 文件，支持恢复

详细的实现文档请参考 [RAG任务队列处理方案](./RAG-Task-Queue-Processing-Solution.CN.md)。

## 相关文档

- [LightRAGNet 系统介绍](./LightRAGNet-System-Introduction.CN.md)：详细的系统架构、实现原理和使用场景说明
- [RAG任务队列处理方案](./RAG-Task-Queue-Processing-Solution.CN.md)：RAG任务队列系统的完整设计和实现指南，包括任务管理、状态持久化和进度追踪