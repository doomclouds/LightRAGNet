---
title: LightRAG.NET
version: 1.1.0
lastUpdated: 2026-05-20
powerBy: Cursor AI
reviewer: PALINK
---

**[EN](README.md) | [中文](README.CN.md)**

# LightRAG.NET

A .NET implementation of LightRAG, fully referencing the architecture and implementation logic of the Python version. LightRAG is a knowledge graph-based Retrieval-Augmented Generation (RAG) system that combines vector retrieval and graph database technologies to achieve more precise and structured document retrieval and knowledge Q&A capabilities.

## Project Structure

```
LightRAGNet/
└── src/
    ├── LightRAGNet.Core/          # Core interfaces and models
    ├── LightRAGNet.Share/         # Shared Web/Server DTOs and event contracts
    ├── LightRAGNet.LLM/           # LLM service (Deepseek OpenAI compatible)
    ├── LightRAGNet.Embedding/     # Embedding service (Alibaba Cloud)
    ├── LightRAGNet.Rerank/        # Rerank service (Alibaba Cloud)
    ├── LightRAGNet.Storage/       # Storage implementations (Qdrant + Neo4j + JSON files)
    ├── LightRAGNet/               # Core LightRAG class
    ├── LightRAGNet.Hosting/       # Dependency injection extensions
    ├── LightRAGNet.Server/        # ASP.NET Core API, SignalR, SQLite document metadata
    ├── LightRAGNet.Web/           # Blazor Server + MudBlazor frontend
    └── LightRAGNet.Example/       # Usage examples
└── tests/
    ├── LightRAGNet.Tests/         # Core services, storage adapters, query, and task queue tests
    ├── LightRAGNet.Server.Tests/  # API/Server host tests
    └── LightRAGNet.Web.Tests/     # Web client, chat UI model, and source guard tests
```

## Development Commands

```powershell
dotnet restore LightRAGNet.slnx
dotnet build LightRAGNet.slnx
dotnet test LightRAGNet.slnx
docker compose up -d
dotnet run --project src/LightRAGNet.Server
dotnet run --project src/LightRAGNet.Web
```

`docker compose up -d` starts local development Qdrant and Neo4j. Tests must not depend on or mutate those real services by default.

### React Graph Workbench

The graph workbench is a React/Vite island hosted by the Blazor web app.

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm install
npm run build
Set-Location ..\..\..
dotnet run --project .\src\LightRAGNet.Web
```

## Features

### Core Features

- ✅ **Knowledge Graph Construction**: Automatically extracts entities and relationships from documents to build structured knowledge graphs
- ✅ **Document Indexing and Querying**: Supports document insertion, vectorization, entity extraction, and intelligent querying
- ✅ **Task State Tracking**: Provides detailed task execution state updates for monitoring and debugging
- ✅ **Streaming Output**: Supports streaming response generation for better user experience
- ✅ **RAG Task Queue Management**: Background task queue system for processing RAG document insertion tasks with priority management, retry mechanism, and state persistence

### Retrieval Modes

- ✅ **Local Mode**: Focuses on directly related entities and relationships, suitable for precise queries
- ✅ **Global Mode**: Multi-hop graph traversal to discover indirect associations, suitable for exploratory queries
- ✅ **Mix Mode**: Combines knowledge graph retrieval and vector retrieval, integrating multiple information sources
- ✅ **Hybrid Mode**: Uses the same implementation as Mix mode, with consistent behavior
- ✅ **Naive Mode**: Uses chunk vector retrieval only, without knowledge graph retrieval
- ✅ **Bypass Mode**: Bypasses retrieval and sends the query directly to the LLM

### Web UI

- ✅ **Chat workspace**: Keeps the conversation on the left and centralizes query mode, response type, References, Rerank, TopK, ChunkTopK, keywords, and debug output in a right-side toolbar
- ✅ **Control explanations**: Key chat actions and query options provide tooltips describing what they do and why they may be disabled
- ✅ **Document management**: Supports Markdown upload, inbox status review, RAG promotion, deletion, and batch refresh
- ✅ **Real-time status**: Uses SignalR to show task status and connection state for background RAG processing

### Infrastructure

- ✅ **LLM Service**: OpenAI-compatible API (Deepseek) as LLM
- ✅ **Embedding Service**: Alibaba Cloud Embedding service
- ✅ **Rerank Service**: Alibaba Cloud Rerank service
- ✅ **Vector Storage**: Qdrant vector database
- ✅ **Graph Storage**: Neo4j graph database
- ✅ **KV Storage**: JSON file key-value storage

## Configuration

Edit `src/LightRAGNet.Example/appsettings.json`:

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

## Usage Examples

### Basic Usage

```csharp
// Insert document
var docId = await rag.InsertAsync("Document content...", filePath: "example.txt");

// Query (Mix mode)
var result = await rag.QueryAsync(
    "What is artificial intelligence?",
    new QueryParam
    {
        Mode = QueryMode.Mix,
        TopK = 10,
        EnableRerank = true
    });

Console.WriteLine(result.Content);
```

### Different Retrieval Modes

```csharp
// Local mode: Precise query
var localResult = await rag.QueryAsync(
    "Query content",
    new QueryParam { Mode = QueryMode.Local, TopK = 20 });

// Global mode: Exploratory query
var globalResult = await rag.QueryAsync(
    "Query content",
    new QueryParam { Mode = QueryMode.Global, TopK = 20 });

// Mix mode: Hybrid retrieval (recommended)
var mixResult = await rag.QueryAsync(
    "Query content",
    new QueryParam { Mode = QueryMode.Mix, TopK = 20, EnableRerank = true });
```

### Task State Tracking

```csharp
// Subscribe to task state change events
rag.TaskStateChanged += (sender, state) =>
{
    Console.WriteLine($"[{state.Stage}] {state.Description} ({state.Current}/{state.Total})");
};

// Inserting documents will trigger state updates
var docId = await rag.InsertAsync("Document content...");
```

## Dependencies

- **Qdrant.Client**: Qdrant vector database client
- **Neo4j.Driver**: Neo4j graph database driver
- **Microsoft.Extensions.Logging**: Logging
- **Microsoft.Extensions.DependencyInjection**: Dependency injection
- **System.Text.Json**: JSON serialization

## Test Safety Boundary

`dotnet test LightRAGNet.slnx` must be safe to run and must never delete or mutate local development Qdrant / Neo4j data.

- Server/API tests use in-memory SQLite, temporary working directories, no-op external storage cleaners, and test doubles by default.
- `LightRagServerFactory` removes real `QdrantClient`, `IDriver`, and hosted background services, then installs throwing `IVectorStore` / `IGraphStore` implementations to catch accidental external RAG storage access.
- Integration tests that need real Qdrant / Neo4j must be explicit opt-in, use uniquely owned workspaces / collections, and clean up only resources they created.
- Tests around `clear-all`, bulk delete, collection deletion, graph database clearing, or background queue processing must prove environment isolation before touching storage.

## Architecture Overview

LightRAGNet uses a layered architecture design:

- **Application Layer**: LightRAG core class, coordinating various service components
- **Service Layer**: Document processing, knowledge graph merging, retrieval context construction
- **Infrastructure Layer**: LLM, Embedding, Rerank, storage interfaces
- **Storage Layer**: Qdrant, Neo4j, JSON files

For detailed architecture documentation, please refer to [LightRAGNet System Introduction](./LightRAGNet-System-Introduction.md).

## Reference Implementation

This implementation fully references the Python version of LightRAG:

- `lightrag.py` - Main class implementation
- `operate.py` - Core operation functions
- `prompt.py` - Prompt templates
- `kg/` - Storage implementations

## RAG Task Processing

LightRAGNet implements a robust background task queue system for processing RAG document insertion tasks. This system provides:

- **Task Queue Management**: Queue tasks with priority-based ordering and automatic processing
- **Background Processing**: Continuous background service that processes tasks from the queue
- **State Persistence**: Task states are persisted to disk, allowing recovery after service restarts
- **Progress Tracking**: Real-time progress updates with detailed stage information
- **Retry Mechanism**: Automatic retry for failed tasks with configurable retry limits
- **File Deduplication**: Prevents duplicate file uploads based on file content hash

The task processing system includes:

- `RagTaskQueueService`: Manages task queuing, ordering, deletion, and retry operations
- `RagTaskProcessorService`: Background service that continuously processes tasks from the queue
- `RagTaskStateStore`: Persists task states to temporary JSON files for recovery

For detailed implementation documentation, please refer to [RAG Task Queue Processing Solution](./RAG-Task-Queue-Processing-Solution.md).

## Related Documentation

- [LightRAGNet System Introduction](./LightRAGNet-System-Introduction.md): Detailed system architecture, implementation principles, and usage scenarios
- [RAG Task Queue Processing Solution](./RAG-Task-Queue-Processing-Solution.md): Complete design and implementation guide for the RAG task queue system, including task management, state persistence, and progress tracking
