---
title: LightRAG.NET
version: 1.1.0
lastUpdated: 2026-01-11
powerBy: Cursor AI
reviewer: PALINK
---

**[EN](README.md) | [中文](README.CN.md)**

# LightRAG.NET

A .NET implementation of LightRAG, fully referencing the architecture and implementation logic of the Python version. LightRAG is a knowledge graph-based Retrieval-Augmented Generation (RAG) system that combines vector retrieval and graph database technologies to achieve more precise and structured document retrieval and knowledge Q&A capabilities.

## Project Structure

```
LightRAGNet/
├── LightRAGNet.Core/          # Core interfaces and models
├── LightRAGNet.LLM/           # LLM service (Deepseek OpenAI compatible)
├── LightRAGNet.Embedding/     # Embedding service (Alibaba Cloud)
├── LightRAGNet.Rerank/        # Rerank service (Alibaba Cloud)
├── LightRAGNet.Storage/       # Storage implementations (Qdrant + Neo4j + JSON files)
├── LightRAGNet/               # Core LightRAG class
├── LightRAGNet.Hosting/       # Dependency injection extensions
└── LightRAGNet.Example/       # Usage examples
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
- ❌ **Naive Mode**: Vector retrieval only (currently not implemented)
- ❌ **Bypass Mode**: Bypasses retrieval and generates directly (currently not implemented)

### Infrastructure

- ✅ **LLM Service**: OpenAI-compatible API (Deepseek) as LLM
- ✅ **Embedding Service**: Alibaba Cloud Embedding service
- ✅ **Rerank Service**: Alibaba Cloud Rerank service
- ✅ **Vector Storage**: Qdrant vector database
- ✅ **Graph Storage**: Neo4j graph database
- ✅ **KV Storage**: JSON file key-value storage

## Configuration

Edit `LightRAGNet.Example/appsettings.json`:

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