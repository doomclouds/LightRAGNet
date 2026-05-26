# Offline Retrieval JSON Dataset and Oracle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the existing offline retrieval evaluation fixture so corpus data and oracle cases are loaded from JSON files instead of hard-coded C# builders, while preserving the current raw-data retrieval checks plus the loader and corpus integrity checks.

**Architecture:** Add test-only JSON data under `tests/LightRAGNet.Tests/Evaluation/Data/`, copy it to test output, and introduce a test-only loader that joins Python-compatible dataset/oracle files with a LightRAGNet extended oracle. Existing production retrieval services remain in use through `RetrievalEvaluationFixture`; only test code and test data files change.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, System.Text.Json, existing LightRAGNet in-memory test doubles.

---

## File Structure

- Modify: `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj`
  - Copies `Evaluation/Data/**/*` to output.
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_dataset.json`
  - Python-compatible `test_cases[]`.
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_retrieval_oracle.json`
  - Python-compatible `oracle[]`.
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/lightragnet_retrieval_oracle.json`
  - LightRAGNet extended corpus and raw-data oracle cases.
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/01_lightrag_overview.md`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/02_rag_architecture.md`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/03_lightrag_improvements.md`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/04_supported_databases.md`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/05_evaluation_and_deployment.md`
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationJsonModels.cs`
  - JSON DTOs only.
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataSet.cs`
  - Validated runtime dataset records used by fixture/tests.
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoader.cs`
  - Loads JSON files, validates joins, and maps into runtime records.
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoaderTests.cs`
  - Loader validation tests.
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCase.cs`
  - Adds optional expected order and deterministic score hints.
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`
  - Seeds in-memory stores from loaded dataset instead of hard-coded static lists.
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
  - Accepts loaded dataset and applies per-case deterministic ranking hints.
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationRunner.cs`
  - Uses optional `ExpectedChunkOrder`.
- Modify: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`
  - Enumerates JSON cases and keeps focused corpus integrity tests.

Do not modify:

- `src/**`
- `tests/LightRAGNet.Server.Tests/**`
- `tests/LightRAGNet.Web.Tests/**`
- `src/LightRAGNet.React/**`
- generated frontend assets
- database migrations

## Task 1: Add JSON Data Files and Copy Rules

**Files:**

- Modify: `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_dataset.json`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_retrieval_oracle.json`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/lightragnet_retrieval_oracle.json`
- Create: `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/*.md`
- Test: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoaderTests.cs`

- [ ] **Step 1: Add data copy rule**

Modify `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj` by adding this `ItemGroup` before `</Project>`:

```xml
  <ItemGroup>
    <None Include="Evaluation\Data\**\*.*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Add Python-compatible dataset JSON**

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_dataset.json`:

```json
{
  "test_cases": [
    {
      "question": "How does LightRAG solve the hallucination problem in large language models?",
      "ground_truth": "LightRAG solves the hallucination problem by combining large language models with external knowledge retrieval. The framework ensures accurate responses by grounding LLM outputs in actual documents. LightRAG provides contextual responses that reduce hallucinations significantly.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "What are the three main components required in a RAG system?",
      "ground_truth": "A RAG system requires three main components: a retrieval system (vector database or search engine) to find relevant documents, an embedding model to convert text into vector representations for similarity search, and a large language model (LLM) to generate responses based on retrieved context.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "How does LightRAG's retrieval performance compare to traditional RAG approaches?",
      "ground_truth": "LightRAG delivers faster retrieval performance than traditional RAG approaches. The framework optimizes document retrieval operations for speed. Traditional RAG systems often suffer from slow query response times. LightRAG achieves high quality results with improved performance. The framework combines speed with accuracy in retrieval operations, prioritizing ease of use without sacrificing quality.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "What vector databases does LightRAG support and what are their key characteristics?",
      "ground_truth": "LightRAG supports multiple vector databases including ChromaDB for simple deployment and efficient similarity search, Neo4j for graph-based knowledge representation with vector capabilities, Milvus for high-performance vector search at scale, Qdrant for fast similarity search with filtering and production-ready infrastructure, MongoDB Atlas for combined document storage and vector search, Redis for in-memory low-latency vector search, and a built-in nano-vectordb that eliminates external dependencies for small projects. This multi-database support enables developers to choose appropriate backends based on scale, performance, and infrastructure requirements.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "What are the four key metrics for evaluating RAG system quality and what does each metric measure?",
      "ground_truth": "RAG system quality is measured through four key metrics: Faithfulness measures whether answers are factually grounded in retrieved context and detects hallucinations. Answer Relevance measures how well answers address the user question and evaluates response appropriateness. Context Recall measures completeness of retrieval and whether all relevant information was retrieved from documents. Context Precision measures quality and relevance of retrieved documents without noise or irrelevant content.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "What are the core benefits of LightRAG and how does it improve upon traditional RAG systems?",
      "ground_truth": "LightRAG offers five core benefits: accuracy through document-grounded responses, up-to-date information without model retraining, domain expertise through specialized document collections, cost-effectiveness by avoiding expensive fine-tuning, and transparency by showing source documents. Compared to traditional RAG systems, LightRAG provides a simpler API with intuitive interfaces, faster retrieval performance with optimized operations, better integration with multiple vector database backends for flexible selection, and optimized prompting strategies with refined templates. LightRAG prioritizes ease of use while maintaining quality and combines speed with accuracy.",
      "project": "lightrag_evaluation_sample"
    },
    {
      "question": "How does the retrieval system work?",
      "ground_truth": "The retrieval system finds relevant documents from large document collections. It works with embedding models and vector databases to match queries with documents.",
      "project": "lightragnet_evaluation_extended"
    },
    {
      "question": "How do retrieval and embedding work together in RAG architecture?",
      "ground_truth": "Retrieval and embedding work together because embedding models convert documents and queries into vectors, and the retrieval system uses those vector representations to find relevant documents.",
      "project": "lightragnet_evaluation_extended"
    },
    {
      "question": "Which operational workflow covers cache and health checks?",
      "ground_truth": "Operational workflows cover health checks, cache management, deployment readiness, and safe maintenance.",
      "project": "lightragnet_evaluation_extended"
    }
  ]
}
```

- [ ] **Step 3: Add Python-compatible retrieval oracle JSON**

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_retrieval_oracle.json`:

```json
{
  "oracle": [
    {
      "question": "How does LightRAG solve the hallucination problem in large language models?",
      "expected_documents": ["01_lightrag_overview.md"]
    },
    {
      "question": "What are the three main components required in a RAG system?",
      "expected_documents": ["02_rag_architecture.md"]
    },
    {
      "question": "How does LightRAG's retrieval performance compare to traditional RAG approaches?",
      "expected_documents": ["03_lightrag_improvements.md"]
    },
    {
      "question": "What vector databases does LightRAG support and what are their key characteristics?",
      "expected_documents": ["04_supported_databases.md"]
    },
    {
      "question": "What are the four key metrics for evaluating RAG system quality and what does each metric measure?",
      "expected_documents": ["05_evaluation_and_deployment.md"]
    },
    {
      "question": "What are the core benefits of LightRAG and how does it improve upon traditional RAG systems?",
      "expected_documents": [
        "01_lightrag_overview.md",
        "03_lightrag_improvements.md"
      ]
    },
    {
      "question": "How does the retrieval system work?",
      "expected_documents": ["02_rag_architecture.md"]
    },
    {
      "question": "How do retrieval and embedding work together in RAG architecture?",
      "expected_documents": ["02_rag_architecture.md"]
    },
    {
      "question": "Which operational workflow covers cache and health checks?",
      "expected_documents": ["03_lightrag_improvements.md"]
    }
  ]
}
```

- [ ] **Step 4: Add sample markdown documents**

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/01_lightrag_overview.md`:

```markdown
# LightRAG Framework Overview

## What is LightRAG?

LightRAG is a Simple and Fast Retrieval-Augmented Generation framework. LightRAG was developed by HKUDS. The framework provides developers with tools to build RAG applications efficiently.

## Problem Statement

Large language models face several limitations. LLMs have a knowledge cutoff date that prevents them from accessing recent information. Large language models generate hallucinations when providing responses without factual grounding. LLMs lack domain-specific expertise in specialized fields.

## How LightRAG Solves These Problems

LightRAG solves the hallucination problem by combining large language models with external knowledge retrieval. The framework ensures accurate responses by grounding LLM outputs in actual documents. LightRAG provides contextual responses that reduce hallucinations significantly. The system enables efficient retrieval from external knowledge bases to supplement LLM capabilities.

## Core Benefits

LightRAG offers accuracy through document-grounded responses. The framework provides up-to-date information without model retraining. LightRAG enables domain expertise through specialized document collections. The system delivers cost-effectiveness by avoiding expensive model fine-tuning. LightRAG ensures transparency by showing source documents for each response.
```

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/02_rag_architecture.md`:

```markdown
# RAG System Architecture

## Main Components of RAG Systems

A RAG system consists of three main components that work together to provide intelligent responses.

### Component 1: Retrieval System

The retrieval system is the first component of a RAG system. A retrieval system finds relevant documents from large document collections. Vector databases serve as the primary storage for the retrieval system. Search engines can also function as retrieval systems in RAG architectures.

### Component 2: Embedding Model

The embedding model is the second component of a RAG system. An embedding model converts text into vector representations for similarity search. The embedding model transforms documents and queries into numerical vectors. These vector representations enable semantic similarity matching between queries and documents.

### Component 3: Large Language Model

The large language model is the third component of a RAG system. An LLM generates responses based on retrieved context from documents. The large language model synthesizes information from multiple sources into coherent answers. LLMs provide natural language generation capabilities for the RAG system.

## How Components Work Together

The retrieval system fetches relevant documents for a user query. The embedding model enables similarity matching between query and documents. The LLM generates the final response using retrieved context. These three components collaborate to provide accurate, contextual responses.
```

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/03_lightrag_improvements.md`:

```markdown
# LightRAG Improvements

## Performance Improvements

LightRAG delivers faster retrieval performance than traditional RAG approaches. The framework optimizes document retrieval operations for speed. Traditional RAG systems often suffer from slow query response times. LightRAG achieves high quality results with improved performance.

## Operational Workflows

Operations include health checks, cache management, deployment readiness, and safe maintenance workflows. These workflows help teams verify system status, inspect cache behavior, and keep retrieval operations stable.

## Developer Experience

LightRAG provides a simpler API with intuitive interfaces. Developers can integrate the framework into applications with minimal setup. The framework prioritizes ease of use without sacrificing quality.
```

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/04_supported_databases.md`:

```markdown
# Supported Databases

## Vector Databases

LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure. Supported vector database options include ChromaDB, Milvus, Qdrant, MongoDB Atlas, Redis, and nano-vectordb.

## Graph and Document Stores

Graph-based knowledge representation can use Neo4j. Document and key value storage choices depend on project scale, latency needs, and infrastructure requirements.
```

Create `tests/LightRAGNet.Tests/Evaluation/Data/sample_documents/05_evaluation_and_deployment.md`:

```markdown
# Evaluation and Deployment

## RAG Quality Metrics

Evaluation tracks faithfulness, answer relevance, context recall, and context precision. Faithfulness measures whether answers are factually grounded in retrieved context. Answer relevance measures how well answers address the user question.

## Retrieval Metrics

Context recall measures completeness of retrieval and whether all relevant information was retrieved from documents. Context precision measures the quality and relevance of retrieved documents without noise or irrelevant content.

## Deployment Readiness

Deployment readiness requires health checks, configuration validation, and repeatable release checks.
```

- [ ] **Step 5: Add LightRAGNet extended oracle JSON**

Create `tests/LightRAGNet.Tests/Evaluation/Data/lightragnet_retrieval_oracle.json`:

```json
{
  "corpus": {
    "chunks": [
      {
        "id": "chunk-overview-hallucination",
        "documentName": "01_lightrag_overview.md",
        "filePath": "docs/eval/01_lightrag_overview.md",
        "content": "LightRAG reduces hallucinations by grounding generated answers in retrieved documents and references."
      },
      {
        "id": "chunk-architecture-rag-components",
        "documentName": "02_rag_architecture.md",
        "filePath": "docs/eval/02_rag_architecture.md",
        "content": "A RAG system requires a retrieval system, an embedding model, and a generation model."
      },
      {
        "id": "chunk-operations-health-cache",
        "documentName": "03_lightrag_improvements.md",
        "filePath": "docs/eval/03_lightrag_improvements.md",
        "content": "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows."
      },
      {
        "id": "chunk-storage-vector-databases",
        "documentName": "04_supported_databases.md",
        "filePath": "docs/eval/04_supported_databases.md",
        "content": "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure."
      },
      {
        "id": "chunk-evaluation-quality-metrics",
        "documentName": "05_evaluation_and_deployment.md",
        "filePath": "docs/eval/05_evaluation_and_deployment.md",
        "content": "Evaluation tracks faithfulness, answer relevance, context recall, and context precision."
      }
    ],
    "entities": [
      {
        "id": "RETRIEVAL_SYSTEM",
        "type": "Component",
        "description": "Retrieves relevant documents for a query.",
        "sourceId": "chunk-architecture-rag-components",
        "filePath": "docs/eval/02_rag_architecture.md"
      },
      {
        "id": "EMBEDDING_MODEL",
        "type": "Component",
        "description": "Converts text into vectors for similarity retrieval.",
        "sourceId": "chunk-architecture-rag-components",
        "filePath": "docs/eval/02_rag_architecture.md"
      },
      {
        "id": "CACHE_MANAGEMENT",
        "type": "Operation",
        "description": "Manages cache visibility and safe maintenance.",
        "sourceId": "chunk-operations-health-cache",
        "filePath": "docs/eval/03_lightrag_improvements.md"
      }
    ],
    "relationships": [
      {
        "sourceId": "RETRIEVAL_SYSTEM",
        "targetId": "EMBEDDING_MODEL",
        "keywords": "rag architecture",
        "description": "Retrieval systems depend on embedding models for vector search.",
        "weight": 3.0,
        "sourceIdList": "chunk-architecture-rag-components"
      },
      {
        "sourceId": "CACHE_MANAGEMENT",
        "targetId": "RETRIEVAL_SYSTEM",
        "keywords": "operations retrieval",
        "description": "Cache management protects retrieval operations during maintenance.",
        "weight": 2.0,
        "sourceIdList": "chunk-operations-health-cache"
      }
    ]
  },
  "cases": [
    {
      "name": "Naive_ReturnsExpectedArchitectureChunk",
      "question": "What are the three main components required in a RAG system?",
      "mode": "Naive",
      "highLevelKeywords": [],
      "lowLevelKeywords": [],
      "topK": 3,
      "chunkTopK": 2,
      "enableRerank": false,
      "expectedDocumentNames": ["02_rag_architecture.md"],
      "expectedChunkIds": ["chunk-architecture-rag-components"],
      "expectedReferenceFilePaths": ["docs/eval/02_rag_architecture.md"],
      "expectedEntityIds": [],
      "expectedRelationshipPairs": [],
      "forbiddenChunkIds": ["chunk-operations-health-cache"],
      "expectedChunkOrder": ["chunk-overview-hallucination", "chunk-architecture-rag-components"]
    },
    {
      "name": "Local_UsesLowLevelEntityFocus",
      "question": "How does the retrieval system work?",
      "mode": "Local",
      "highLevelKeywords": [],
      "lowLevelKeywords": ["RETRIEVAL_SYSTEM"],
      "topK": 3,
      "chunkTopK": 2,
      "enableRerank": false,
      "expectedDocumentNames": ["02_rag_architecture.md"],
      "expectedChunkIds": ["chunk-architecture-rag-components"],
      "expectedReferenceFilePaths": ["docs/eval/02_rag_architecture.md"],
      "expectedEntityIds": ["RETRIEVAL_SYSTEM"],
      "expectedRelationshipPairs": [
        { "sourceId": "RETRIEVAL_SYSTEM", "targetId": "EMBEDDING_MODEL" }
      ],
      "forbiddenChunkIds": []
    },
    {
      "name": "Global_UsesHighLevelRelationshipFocus",
      "question": "Which architecture relationship connects retrieval and embedding?",
      "mode": "Global",
      "highLevelKeywords": ["rag architecture"],
      "lowLevelKeywords": [],
      "topK": 3,
      "chunkTopK": 2,
      "enableRerank": false,
      "expectedDocumentNames": ["02_rag_architecture.md"],
      "expectedChunkIds": ["chunk-architecture-rag-components"],
      "expectedReferenceFilePaths": ["docs/eval/02_rag_architecture.md"],
      "expectedEntityIds": ["RETRIEVAL_SYSTEM", "EMBEDDING_MODEL"],
      "expectedRelationshipPairs": [
        { "sourceId": "RETRIEVAL_SYSTEM", "targetId": "EMBEDDING_MODEL" }
      ],
      "forbiddenChunkIds": []
    },
    {
      "name": "Mix_ReturnsKgEntityRelationshipAndRelatedChunk",
      "question": "How do retrieval and embedding work together in RAG architecture?",
      "mode": "Mix",
      "highLevelKeywords": ["rag architecture"],
      "lowLevelKeywords": ["RETRIEVAL_SYSTEM"],
      "topK": 3,
      "chunkTopK": 2,
      "enableRerank": false,
      "expectedDocumentNames": ["02_rag_architecture.md"],
      "expectedChunkIds": ["chunk-architecture-rag-components"],
      "expectedReferenceFilePaths": ["docs/eval/02_rag_architecture.md"],
      "expectedEntityIds": ["RETRIEVAL_SYSTEM"],
      "expectedRelationshipPairs": [
        { "sourceId": "RETRIEVAL_SYSTEM", "targetId": "EMBEDDING_MODEL" }
      ],
      "forbiddenChunkIds": []
    },
    {
      "name": "Rerank_KeepsRelevantChunkInFinalContext",
      "question": "Which operational workflow covers cache and health checks?",
      "mode": "Naive",
      "highLevelKeywords": [],
      "lowLevelKeywords": [],
      "topK": 5,
      "chunkTopK": 3,
      "enableRerank": true,
      "expectedDocumentNames": ["03_lightrag_improvements.md"],
      "expectedChunkIds": ["chunk-operations-health-cache"],
      "expectedReferenceFilePaths": ["docs/eval/03_lightrag_improvements.md"],
      "expectedEntityIds": [],
      "expectedRelationshipPairs": [],
      "forbiddenChunkIds": ["chunk-overview-hallucination"],
      "expectedChunkOrder": [
        "chunk-operations-health-cache",
        "chunk-storage-vector-databases",
        "chunk-evaluation-quality-metrics"
      ],
      "vectorScoresByChunkId": {
        "chunk-storage-vector-databases": 0.9,
        "chunk-evaluation-quality-metrics": 0.8,
        "chunk-operations-health-cache": 0.7,
        "chunk-architecture-rag-components": 0.2,
        "chunk-overview-hallucination": 0.1
      },
      "rerankScoresByContent": {
        "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.": 0.99,
        "LightRAG can use vector databases, graph stores, and key value stores for retrieval infrastructure.": 0.1,
        "Evaluation tracks faithfulness, answer relevance, context recall, and context precision.": 0.05
      }
    }
  ]
}
```

- [ ] **Step 6: Write failing data copy smoke test**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoaderTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationDataLoaderTests
{
    [Fact]
    public void DefaultDataDirectory_IsCopiedToTestOutput()
    {
        var dataDirectory = RetrievalEvaluationDataLoader.GetDefaultDataDirectory();

        Directory.Exists(dataDirectory).Should().BeTrue();
        File.Exists(Path.Combine(dataDirectory, "sample_dataset.json")).Should().BeTrue();
        File.Exists(Path.Combine(dataDirectory, "sample_retrieval_oracle.json")).Should().BeTrue();
        File.Exists(Path.Combine(dataDirectory, "lightragnet_retrieval_oracle.json")).Should().BeTrue();
    }
}
```

- [ ] **Step 7: Run the smoke test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DefaultDataDirectory_IsCopiedToTestOutput" --no-restore --verbosity minimal
```

Expected: build fails because `RetrievalEvaluationDataLoader` does not exist.

- [ ] **Step 8: Add minimal loader path helper**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoader.cs`:

```csharp
namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationDataLoader
{
    public static string GetDefaultDataDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Evaluation", "Data");
    }
}
```

- [ ] **Step 9: Run the smoke test to verify it passes**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DefaultDataDirectory_IsCopiedToTestOutput" --verbosity minimal
```

Expected: pass.

- [ ] **Step 10: Commit**

```powershell
git add tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj tests\LightRAGNet.Tests\Evaluation\Data tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationDataLoader.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationDataLoaderTests.cs
git commit -m "test: add retrieval evaluation JSON data files"
```

## Task 2: Add JSON Loader Models and Validation

**Files:**

- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationJsonModels.cs`
- Create: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataSet.cs`
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoader.cs`
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoaderTests.cs`

- [ ] **Step 1: Write failing loader tests**

Append these tests to `RetrievalEvaluationDataLoaderTests.cs`:

```csharp
[Fact]
public void LoadDefault_LoadsPythonCompatibleDatasetAndExtendedCases()
{
    var dataSet = RetrievalEvaluationDataLoader.LoadDefault();

    dataSet.TestCases.Should().HaveCount(9);
    dataSet.DocumentOracleByQuestion.Should().ContainKey("What are the three main components required in a RAG system?");
    dataSet.Cases.Select(testCase => testCase.Name).Should().BeEquivalentTo(
        [
            "Naive_ReturnsExpectedArchitectureChunk",
            "Local_UsesLowLevelEntityFocus",
            "Global_UsesHighLevelRelationshipFocus",
            "Mix_ReturnsKgEntityRelationshipAndRelatedChunk",
            "Rerank_KeepsRelevantChunkInFinalContext"
        ]);
    dataSet.Chunks.Should().HaveCount(5);
    dataSet.Entities.Should().HaveCount(3);
    dataSet.Relationships.Should().HaveCount(2);
}

[Fact]
public void LoadDefault_ValidatesExtendedCaseQuestionsAgainstDataset()
{
    var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
    var questions = dataSet.TestCases.Select(testCase => testCase.Question).ToHashSet(StringComparer.Ordinal);

    dataSet.Cases.Should().OnlyContain(testCase => questions.Contains(testCase.Query));
}

[Fact]
public void LoadFromDirectory_WhenExpectedDocumentIsMissing_ThrowsHelpfulMessage()
{
    using var temp = new TemporaryEvaluationDataDirectory();
    temp.CopyDefaultData();
    File.Delete(Path.Combine(temp.Path, "sample_documents", "02_rag_architecture.md"));

    var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

    act.Should()
        .Throw<InvalidOperationException>()
        .WithMessage("*Expected document '02_rag_architecture.md' was not found*");
}
```

Add this helper to the same test file:

```csharp
private sealed class TemporaryEvaluationDataDirectory : IDisposable
{
    public TemporaryEvaluationDataDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LightRAGNet.EvaluationData",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void CopyDefaultData()
    {
        CopyDirectory(RetrievalEvaluationDataLoader.GetDefaultDataDirectory(), Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(System.IO.Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, file);
            File.Copy(file, System.IO.Path.Combine(destination, relative), overwrite: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationDataLoaderTests" --verbosity minimal
```

Expected: build fails because loader model members do not exist.

- [ ] **Step 3: Add JSON DTOs**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationJsonModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace LightRAGNet.Tests.Evaluation;

internal sealed record EvaluationDatasetJson(
    [property: JsonPropertyName("test_cases")] IReadOnlyList<EvaluationTestCaseJson>? TestCases);

internal sealed record EvaluationTestCaseJson(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("ground_truth")] string? GroundTruth,
    [property: JsonPropertyName("project")] string? Project);

internal sealed record EvaluationDocumentOracleJson(
    [property: JsonPropertyName("oracle")] IReadOnlyList<EvaluationDocumentOracleEntryJson>? Oracle);

internal sealed record EvaluationDocumentOracleEntryJson(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("expected_documents")] IReadOnlyList<string>? ExpectedDocuments);

internal sealed record LightRagNetEvaluationOracleJson(
    [property: JsonPropertyName("corpus")] EvaluationCorpusJson? Corpus,
    [property: JsonPropertyName("cases")] IReadOnlyList<RetrievalEvaluationCaseJson>? Cases);

internal sealed record EvaluationCorpusJson(
    [property: JsonPropertyName("chunks")] IReadOnlyList<RetrievalEvaluationChunkJson>? Chunks,
    [property: JsonPropertyName("entities")] IReadOnlyList<RetrievalEvaluationEntityJson>? Entities,
    [property: JsonPropertyName("relationships")] IReadOnlyList<RetrievalEvaluationRelationshipJson>? Relationships);

internal sealed record RetrievalEvaluationChunkJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("documentName")] string? DocumentName,
    [property: JsonPropertyName("filePath")] string? FilePath,
    [property: JsonPropertyName("content")] string? Content);

internal sealed record RetrievalEvaluationEntityJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("sourceId")] string? SourceId,
    [property: JsonPropertyName("filePath")] string? FilePath);

internal sealed record RetrievalEvaluationRelationshipJson(
    [property: JsonPropertyName("sourceId")] string? SourceId,
    [property: JsonPropertyName("targetId")] string? TargetId,
    [property: JsonPropertyName("keywords")] string? Keywords,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("weight")] double? Weight,
    [property: JsonPropertyName("sourceIdList")] string? SourceIdList);

internal sealed record RetrievalEvaluationCaseJson(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("highLevelKeywords")] IReadOnlyList<string>? HighLevelKeywords,
    [property: JsonPropertyName("lowLevelKeywords")] IReadOnlyList<string>? LowLevelKeywords,
    [property: JsonPropertyName("topK")] int? TopK,
    [property: JsonPropertyName("chunkTopK")] int? ChunkTopK,
    [property: JsonPropertyName("enableRerank")] bool? EnableRerank,
    [property: JsonPropertyName("expectedDocumentNames")] IReadOnlyList<string>? ExpectedDocumentNames,
    [property: JsonPropertyName("expectedChunkIds")] IReadOnlyList<string>? ExpectedChunkIds,
    [property: JsonPropertyName("expectedReferenceFilePaths")] IReadOnlyList<string>? ExpectedReferenceFilePaths,
    [property: JsonPropertyName("expectedEntityIds")] IReadOnlyList<string>? ExpectedEntityIds,
    [property: JsonPropertyName("expectedRelationshipPairs")] IReadOnlyList<ExpectedRelationshipPair>? ExpectedRelationshipPairs,
    [property: JsonPropertyName("forbiddenChunkIds")] IReadOnlyList<string>? ForbiddenChunkIds,
    [property: JsonPropertyName("expectedChunkOrder")] IReadOnlyList<string>? ExpectedChunkOrder,
    [property: JsonPropertyName("vectorScoresByChunkId")] IReadOnlyDictionary<string, float>? VectorScoresByChunkId,
    [property: JsonPropertyName("rerankScoresByContent")] IReadOnlyDictionary<string, float>? RerankScoresByContent);
```

- [ ] **Step 4: Add runtime dataset records**

Create `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataSet.cs`:

```csharp
namespace LightRAGNet.Tests.Evaluation;

public sealed record RetrievalEvaluationDataSet(
    IReadOnlyList<RetrievalEvaluationTestCase> TestCases,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DocumentOracleByQuestion,
    IReadOnlyList<RetrievalEvaluationChunkSpec> Chunks,
    IReadOnlyList<RetrievalEvaluationEntitySpec> Entities,
    IReadOnlyList<RetrievalEvaluationRelationshipSpec> Relationships,
    IReadOnlyList<RetrievalEvaluationCase> Cases);

public sealed record RetrievalEvaluationTestCase(
    string Question,
    string GroundTruth,
    string Project);

public sealed record RetrievalEvaluationChunkSpec(
    string Id,
    string DocumentName,
    string FilePath,
    string Content);

public sealed record RetrievalEvaluationEntitySpec(
    string Id,
    string Type,
    string Description,
    string SourceId,
    string FilePath);

public sealed record RetrievalEvaluationRelationshipSpec(
    string SourceId,
    string TargetId,
    string Keywords,
    string Description,
    double Weight,
    string SourceIdList);
```

- [ ] **Step 5: Extend case record**

Modify `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCase.cs`:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed record RetrievalEvaluationCase(
    string Name,
    string Query,
    QueryMode Mode,
    IReadOnlyList<string> HighLevelKeywords,
    IReadOnlyList<string> LowLevelKeywords,
    int TopK,
    int ChunkTopK,
    IReadOnlyList<string> ExpectedDocumentNames,
    IReadOnlyList<string> ExpectedChunkIds,
    IReadOnlyList<string> ExpectedReferenceFilePaths,
    IReadOnlyList<string> ExpectedEntityIds,
    IReadOnlyList<ExpectedRelationshipPair> ExpectedRelationshipPairs,
    IReadOnlyList<string> ForbiddenChunkIds,
    bool EnableRerank,
    IReadOnlyList<string> ExpectedChunkOrder,
    IReadOnlyDictionary<string, float> VectorScoresByChunkId,
    IReadOnlyDictionary<string, float> RerankScoresByContent);

public sealed record ExpectedRelationshipPair(string SourceId, string TargetId);
```

- [ ] **Step 6: Implement loader**

Replace `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationDataLoader.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string GetDefaultDataDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Evaluation", "Data");
    }

    public static RetrievalEvaluationDataSet LoadDefault()
    {
        return LoadFromDirectory(GetDefaultDataDirectory());
    }

    public static RetrievalEvaluationDataSet LoadFromDirectory(string dataDirectory)
    {
        var dataset = ReadJson<EvaluationDatasetJson>(Path.Combine(dataDirectory, "sample_dataset.json"));
        var documentOracle = ReadJson<EvaluationDocumentOracleJson>(Path.Combine(dataDirectory, "sample_retrieval_oracle.json"));
        var extendedOracle = ReadJson<LightRagNetEvaluationOracleJson>(Path.Combine(dataDirectory, "lightragnet_retrieval_oracle.json"));
        var documentsDirectory = Path.Combine(dataDirectory, "sample_documents");

        var testCases = ConvertTestCases(dataset);
        var documentOracleByQuestion = ConvertDocumentOracle(documentOracle);
        var chunks = ConvertChunks(extendedOracle.Corpus?.Chunks, documentsDirectory);
        var entities = ConvertEntities(extendedOracle.Corpus?.Entities);
        var relationships = ConvertRelationships(extendedOracle.Corpus?.Relationships);
        var cases = ConvertCases(extendedOracle.Cases);

        Validate(testCases, documentOracleByQuestion, chunks, entities, relationships, cases, documentsDirectory);

        return new RetrievalEvaluationDataSet(
            testCases,
            documentOracleByQuestion,
            chunks,
            entities,
            relationships,
            cases);
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Evaluation data file was not found: {path}");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Evaluation data file is empty or invalid: {path}");
    }

    private static IReadOnlyList<RetrievalEvaluationTestCase> ConvertTestCases(EvaluationDatasetJson dataset)
    {
        return RequiredList(dataset.TestCases, "sample_dataset.json:test_cases")
            .Select(testCase => new RetrievalEvaluationTestCase(
                Required(testCase.Question, "test case question"),
                Required(testCase.GroundTruth, $"ground_truth for '{testCase.Question}'"),
                Required(testCase.Project, $"project for '{testCase.Question}'")))
            .ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ConvertDocumentOracle(EvaluationDocumentOracleJson oracle)
    {
        return RequiredList(oracle.Oracle, "sample_retrieval_oracle.json:oracle")
            .ToDictionary(
                entry => Required(entry.Question, "oracle question"),
                entry => (IReadOnlyList<string>)RequiredList(entry.ExpectedDocuments, $"expected_documents for '{entry.Question}'"),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<RetrievalEvaluationChunkSpec> ConvertChunks(
        IReadOnlyList<RetrievalEvaluationChunkJson>? chunks,
        string documentsDirectory)
    {
        return RequiredList(chunks, "lightragnet_retrieval_oracle.json:corpus.chunks")
            .Select(chunk =>
            {
                var documentName = Required(chunk.DocumentName, $"documentName for chunk '{chunk.Id}'");
                var documentPath = Path.Combine(documentsDirectory, documentName);
                if (!File.Exists(documentPath))
                {
                    throw new InvalidOperationException($"Expected document '{documentName}' was not found in '{documentsDirectory}'.");
                }

                return new RetrievalEvaluationChunkSpec(
                    Required(chunk.Id, "chunk id"),
                    documentName,
                    Required(chunk.FilePath, $"filePath for chunk '{chunk.Id}'"),
                    Required(chunk.Content, $"content for chunk '{chunk.Id}'"));
            })
            .ToList();
    }

    private static IReadOnlyList<RetrievalEvaluationEntitySpec> ConvertEntities(
        IReadOnlyList<RetrievalEvaluationEntityJson>? entities)
    {
        return RequiredList(entities, "lightragnet_retrieval_oracle.json:corpus.entities")
            .Select(entity => new RetrievalEvaluationEntitySpec(
                Required(entity.Id, "entity id"),
                Required(entity.Type, $"type for entity '{entity.Id}'"),
                Required(entity.Description, $"description for entity '{entity.Id}'"),
                Required(entity.SourceId, $"sourceId for entity '{entity.Id}'"),
                Required(entity.FilePath, $"filePath for entity '{entity.Id}'")))
            .ToList();
    }

    private static IReadOnlyList<RetrievalEvaluationRelationshipSpec> ConvertRelationships(
        IReadOnlyList<RetrievalEvaluationRelationshipJson>? relationships)
    {
        return RequiredList(relationships, "lightragnet_retrieval_oracle.json:corpus.relationships")
            .Select(relationship => new RetrievalEvaluationRelationshipSpec(
                Required(relationship.SourceId, "relationship sourceId"),
                Required(relationship.TargetId, $"targetId for relationship '{relationship.SourceId}'"),
                Required(relationship.Keywords, $"keywords for relationship '{relationship.SourceId}->{relationship.TargetId}'"),
                Required(relationship.Description, $"description for relationship '{relationship.SourceId}->{relationship.TargetId}'"),
                relationship.Weight ?? throw new InvalidOperationException($"weight is required for relationship '{relationship.SourceId}->{relationship.TargetId}'."),
                Required(relationship.SourceIdList, $"sourceIdList for relationship '{relationship.SourceId}->{relationship.TargetId}'")))
            .ToList();
    }

    private static IReadOnlyList<RetrievalEvaluationCase> ConvertCases(IReadOnlyList<RetrievalEvaluationCaseJson>? cases)
    {
        return RequiredList(cases, "lightragnet_retrieval_oracle.json:cases")
            .Select(testCase =>
            {
                var name = Required(testCase.Name, "case name");
                if (!Enum.TryParse<QueryMode>(Required(testCase.Mode, $"mode for case '{name}'"), ignoreCase: true, out var mode))
                {
                    throw new InvalidOperationException($"Unknown query mode '{testCase.Mode}' for case '{name}'.");
                }

                return new RetrievalEvaluationCase(
                    name,
                    Required(testCase.Question, $"question for case '{name}'"),
                    mode,
                    testCase.HighLevelKeywords ?? [],
                    testCase.LowLevelKeywords ?? [],
                    testCase.TopK ?? throw new InvalidOperationException($"topK is required for case '{name}'."),
                    testCase.ChunkTopK ?? throw new InvalidOperationException($"chunkTopK is required for case '{name}'."),
                    testCase.ExpectedDocumentNames ?? [],
                    testCase.ExpectedChunkIds ?? [],
                    testCase.ExpectedReferenceFilePaths ?? [],
                    testCase.ExpectedEntityIds ?? [],
                    testCase.ExpectedRelationshipPairs ?? [],
                    testCase.ForbiddenChunkIds ?? [],
                    testCase.EnableRerank ?? false,
                    testCase.ExpectedChunkOrder ?? [],
                    testCase.VectorScoresByChunkId ?? new Dictionary<string, float>(),
                    testCase.RerankScoresByContent ?? new Dictionary<string, float>());
            })
            .ToList();
    }

    private static void Validate(
        IReadOnlyList<RetrievalEvaluationTestCase> testCases,
        IReadOnlyDictionary<string, IReadOnlyList<string>> documentOracleByQuestion,
        IReadOnlyList<RetrievalEvaluationChunkSpec> chunks,
        IReadOnlyList<RetrievalEvaluationEntitySpec> entities,
        IReadOnlyList<RetrievalEvaluationRelationshipSpec> relationships,
        IReadOnlyList<RetrievalEvaluationCase> cases,
        string documentsDirectory)
    {
        var questions = testCases.Select(testCase => testCase.Question).ToHashSet(StringComparer.Ordinal);
        var documentNames = Directory.GetFiles(documentsDirectory, "*.md").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var chunkIds = chunks.Select(chunk => chunk.Id).ToHashSet(StringComparer.Ordinal);
        var entityIds = entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var question in questions)
        {
            if (!documentOracleByQuestion.ContainsKey(question))
            {
                throw new InvalidOperationException($"No document oracle entry exists for dataset question '{question}'.");
            }
        }

        foreach (var (question, expectedDocuments) in documentOracleByQuestion)
        {
            if (!questions.Contains(question))
            {
                throw new InvalidOperationException($"Document oracle question '{question}' does not exist in sample_dataset.json.");
            }

            foreach (var documentName in expectedDocuments)
            {
                if (!documentNames.Contains(documentName))
                {
                    throw new InvalidOperationException($"Expected document '{documentName}' was not found in '{documentsDirectory}'.");
                }
            }
        }

        foreach (var chunk in chunks)
        {
            if (!documentNames.Contains(chunk.DocumentName))
            {
                throw new InvalidOperationException($"Chunk '{chunk.Id}' references unknown document '{chunk.DocumentName}'.");
            }
        }

        foreach (var entity in entities)
        {
            if (!chunkIds.Contains(entity.SourceId))
            {
                throw new InvalidOperationException($"Entity '{entity.Id}' references unknown source chunk '{entity.SourceId}'.");
            }
        }

        foreach (var relationship in relationships)
        {
            if (!entityIds.Contains(relationship.SourceId) || !entityIds.Contains(relationship.TargetId))
            {
                throw new InvalidOperationException($"Relationship '{relationship.SourceId}->{relationship.TargetId}' references unknown entities.");
            }
        }

        foreach (var testCase in cases)
        {
            if (!questions.Contains(testCase.Query))
            {
                throw new InvalidOperationException($"Extended oracle case '{testCase.Name}' references unknown question '{testCase.Query}'.");
            }

            foreach (var documentName in testCase.ExpectedDocumentNames)
            {
                if (!documentOracleByQuestion[testCase.Query].Contains(documentName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Case '{testCase.Name}' expected document '{documentName}' is not listed in sample_retrieval_oracle.json.");
                }
            }

            foreach (var chunkId in testCase.ExpectedChunkIds.Concat(testCase.ForbiddenChunkIds).Concat(testCase.ExpectedChunkOrder))
            {
                if (!chunkIds.Contains(chunkId))
                {
                    throw new InvalidOperationException($"Case '{testCase.Name}' references unknown chunk '{chunkId}'.");
                }
            }
        }
    }

    private static IReadOnlyList<T> RequiredList<T>(IReadOnlyList<T>? value, string name)
    {
        return value is { Count: > 0 }
            ? value
            : throw new InvalidOperationException($"{name} must contain at least one item.");
    }

    private static string Required(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is required.")
            : value.Trim();
    }
}
```

- [ ] **Step 7: Run loader tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationDataLoaderTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationJsonModels.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationDataSet.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationDataLoader.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationDataLoaderTests.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationCase.cs
git commit -m "test: load retrieval evaluation data from JSON"
```

## Task 3: Seed Evaluation Fixture From Loaded Dataset

**Files:**

- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`
- Modify: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`

- [ ] **Step 1: Write failing corpus-from-json test**

Replace `RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks` in `OfflineRetrievalEvaluationTests.cs` with:

```csharp
[Fact]
public async Task RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks()
{
    var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
    var fixture = await RetrievalEvaluationFixture.CreateAsync(dataSet);

    fixture.VectorStore.Get(RetrievalEvaluationCorpus.ChunksCollection, "chunk-architecture-rag-components")
        .Should()
        .NotBeNull();
    fixture.GraphStore.GetSeededNode("RETRIEVAL_SYSTEM")
        .Should()
        .NotBeNull();
    fixture.GraphStore.GetSeededEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")
        .Should()
        .NotBeNull();

    fixture.VectorStore.Collections[RetrievalEvaluationCorpus.ChunksCollection]
        .Keys
        .Should()
        .BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
    fixture.TextChunks.Items
        .Keys
        .Should()
        .BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks" --verbosity minimal
```

Expected: build fails because `RetrievalEvaluationFixture.CreateAsync(RetrievalEvaluationDataSet)` does not exist.

- [ ] **Step 3: Replace corpus seeding implementation**

Replace `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationCorpus.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationCorpus
{
    public const string ChunksCollection = "chunks";
    public const string FilePathKey = "file_path";
    public const string ChunkIdKey = "chunk_id";
    public const string ContentKey = "content";

    public static async Task SeedAsync(
        RetrievalEvaluationDataSet dataSet,
        InMemoryVectorStore vectorStore,
        InMemoryGraphStore graphStore,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken = default)
    {
        SeedChunks(dataSet.Chunks, vectorStore);
        SeedGraph(dataSet.Entities, dataSet.Relationships, graphStore);
        await SeedTextChunksAsync(dataSet.Chunks, textChunks, cancellationToken);
    }

    private static void SeedChunks(
        IReadOnlyList<RetrievalEvaluationChunkSpec> chunks,
        InMemoryVectorStore vectorStore)
    {
        foreach (var chunk in chunks)
        {
            vectorStore.Seed(ChunksCollection, new VectorDocument
            {
                Id = chunk.Id,
                Content = chunk.Content,
                Metadata = new Dictionary<string, object>
                {
                    [FilePathKey] = chunk.FilePath,
                    [ChunkIdKey] = chunk.Id
                }
            });
        }
    }

    private static void SeedGraph(
        IReadOnlyList<RetrievalEvaluationEntitySpec> entities,
        IReadOnlyList<RetrievalEvaluationRelationshipSpec> relationships,
        InMemoryGraphStore graphStore)
    {
        foreach (var entity in entities)
        {
            graphStore.SeedNode(entity.Id, new Dictionary<string, object>
            {
                ["entity_id"] = entity.Id,
                ["entity_type"] = entity.Type,
                ["description"] = entity.Description,
                ["source_id"] = entity.SourceId,
                [FilePathKey] = entity.FilePath
            });
        }

        foreach (var relationship in relationships)
        {
            graphStore.SeedEdge(relationship.SourceId, relationship.TargetId, new Dictionary<string, object>
            {
                ["keywords"] = relationship.Keywords,
                ["description"] = relationship.Description,
                ["weight"] = relationship.Weight,
                ["source_id"] = relationship.SourceIdList
            });
        }
    }

    private static Task SeedTextChunksAsync(
        IReadOnlyList<RetrievalEvaluationChunkSpec> chunks,
        InMemoryKvStore textChunks,
        CancellationToken cancellationToken)
    {
        var data = chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => new Dictionary<string, object>
            {
                [ContentKey] = chunk.Content,
                [FilePathKey] = chunk.FilePath
            },
            StringComparer.Ordinal);

        return textChunks.UpsertAsync(data, cancellationToken);
    }
}
```

- [ ] **Step 4: Update fixture to accept dataset**

Modify `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationFixture.cs`:

```csharp
public static async Task<RetrievalEvaluationFixture> CreateAsync(
    RetrievalEvaluationDataSet? dataSet = null,
    IRerankService? rerankService = null)
{
    dataSet ??= RetrievalEvaluationDataLoader.LoadDefault();
    var vectorStore = new InMemoryVectorStore();
    var graphStore = new InMemoryGraphStore();
    var textChunks = new InMemoryKvStore();
    var tokenizer = new FakeTokenizer();
    var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
    var rerankCoordinator = new RerankCoordinator(
        rerankService ?? new DeterministicEvaluationRerankService(),
        new RerankDocumentChunker(tokenizer, rerankOptions),
        rerankOptions);
    var embeddingService = Substitute.For<IEmbeddingService>();
    embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns([0.1f, 0.2f, 0.3f]);

    await RetrievalEvaluationCorpus.SeedAsync(dataSet, vectorStore, graphStore, textChunks);
    SeedKnowledgeGraphVectors(dataSet, vectorStore);

    var retrievalContextService = new RetrievalContextService(
        embeddingService,
        vectorStore,
        graphStore,
        rerankCoordinator,
        tokenizer,
        textChunks,
        Options.Create(new LightRAGOptions { KgChunkPickMethod = EvaluationKgChunkPickMethod }),
        NullLoggerFactory.Instance);

    return new RetrievalEvaluationFixture(
        vectorStore,
        graphStore,
        textChunks,
        new NaiveQueryService(vectorStore, rerankCoordinator, tokenizer),
        retrievalContextService);
}
```

Replace `SeedKnowledgeGraphVectors` with:

```csharp
private static void SeedKnowledgeGraphVectors(
    RetrievalEvaluationDataSet dataSet,
    InMemoryVectorStore vectorStore)
{
    foreach (var entity in dataSet.Entities)
    {
        vectorStore.Seed("entities", new VectorDocument
        {
            Id = $"entity-{entity.Id}",
            Content = entity.Description,
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = entity.Id,
                ["entity_type"] = entity.Type,
                ["description"] = entity.Description,
                ["source_id"] = entity.SourceId,
                ["file_path"] = entity.FilePath
            }
        });
    }

    foreach (var relationship in dataSet.Relationships)
    {
        vectorStore.Seed("relationships", new VectorDocument
        {
            Id = $"relationship-{relationship.SourceId}-{relationship.TargetId}",
            Content = relationship.Description,
            Metadata = new Dictionary<string, object>
            {
                ["src_id"] = relationship.SourceId,
                ["tgt_id"] = relationship.TargetId,
                ["keywords"] = relationship.Keywords,
                ["description"] = relationship.Description,
                ["source_id"] = relationship.SourceIdList
            }
        });
    }
}
```

Add deterministic rerank service inside `RetrievalEvaluationFixture.cs`:

```csharp
private sealed class DeterministicEvaluationRerankService : IRerankService
{
    public Dictionary<string, float> ScoresByContent { get; } = new(StringComparer.Ordinal);

    public Task<List<RerankResult>> RerankAsync(
        string query,
        List<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        var results = documents
            .Select((document, index) => new RerankResult
            {
                Index = index,
                RelevanceScore = ScoresByContent.TryGetValue(document, out var score) ? score : 0.0f
            })
            .OrderByDescending(result => result.RelevanceScore)
            .Take(topN)
            .ToList();

        return Task.FromResult(results);
    }
}
```

Store the reranker as a fixture field:

```csharp
private readonly DeterministicEvaluationRerankService deterministicRerankService;
```

Update the constructor and return statement to pass this service.

Add this method:

```csharp
public void ApplyRankingHints(RetrievalEvaluationCase evaluationCase)
{
    VectorStore.QueryScoresByDocumentId.Clear();
    foreach (var (chunkId, score) in evaluationCase.VectorScoresByChunkId)
    {
        VectorStore.QueryScoresByDocumentId[chunkId] = score;
    }

    deterministicRerankService.ScoresByContent.Clear();
    foreach (var (content, score) in evaluationCase.RerankScoresByContent)
    {
        deterministicRerankService.ScoresByContent[content] = score;
    }
}
```

- [ ] **Step 5: Run corpus test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks" --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationCorpus.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationFixture.cs tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs
git commit -m "test: seed retrieval evaluation fixture from JSON"
```

## Task 4: Convert Offline Evaluation Tests to JSON Cases

**Files:**

- Modify: `tests/LightRAGNet.Tests/Evaluation/OfflineRetrievalEvaluationTests.cs`
- Modify: `tests/LightRAGNet.Tests/Evaluation/RetrievalEvaluationRunner.cs`

- [ ] **Step 1: Replace inline oracle tests with data-driven test**

Replace `OfflineRetrievalEvaluationTests.cs` with:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public sealed class OfflineRetrievalEvaluationTests
{
    [Fact]
    public async Task JsonOracleCases_MatchRawRetrievalData()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
        var fixture = await RetrievalEvaluationFixture.CreateAsync(dataSet);

        foreach (var evaluationCase in dataSet.Cases)
        {
            fixture.ApplyRankingHints(evaluationCase);

            var result = await fixture.RunAsync(evaluationCase);

            RetrievalEvaluationRunner.AssertCase(result, evaluationCase);
        }
    }

    [Fact]
    public async Task RetrievalEvaluationCorpus_SeedsExpectedDocumentsGraphAndChunks()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();
        var fixture = await RetrievalEvaluationFixture.CreateAsync(dataSet);

        fixture.VectorStore.Get(RetrievalEvaluationCorpus.ChunksCollection, "chunk-architecture-rag-components")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededNode("RETRIEVAL_SYSTEM")
            .Should()
            .NotBeNull();
        fixture.GraphStore.GetSeededEdge("RETRIEVAL_SYSTEM", "EMBEDDING_MODEL")
            .Should()
            .NotBeNull();

        fixture.VectorStore.Collections[RetrievalEvaluationCorpus.ChunksCollection]
            .Keys
            .Should()
            .BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
        fixture.TextChunks.Items
            .Keys
            .Should()
            .BeEquivalentTo(dataSet.Chunks.Select(chunk => chunk.Id));
    }
}
```

- [ ] **Step 2: Update runner to respect expected order from JSON**

Modify `RetrievalEvaluationRunner.AssertCase` after forbidden chunk assertions:

```csharp
if (evaluationCase.ExpectedChunkOrder.Count > 0)
{
    chunks
        .Select(chunk => chunk["chunk_id"].ToString())
        .Should()
        .Equal(evaluationCase.ExpectedChunkOrder, $"{evaluationCase.Name} should return expected chunks in order");
}
```

Keep `AssertChunkIds` only if another test still uses it. If no test uses it after this change, delete `AssertChunkIds` from `RetrievalEvaluationRunner`.

- [ ] **Step 3: Run evaluation tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Commit**

```powershell
git add tests\LightRAGNet.Tests\Evaluation\OfflineRetrievalEvaluationTests.cs tests\LightRAGNet.Tests\Evaluation\RetrievalEvaluationRunner.cs
git commit -m "test: drive retrieval evaluation from JSON oracle"
```

## Task 5: Verification and Scope Gate

**Files:**

- All changed test data and test code files.

- [ ] **Step 1: Verify changed file boundary**

Run:

```powershell
git diff --name-only origin/main..HEAD
```

Expected changed files are limited to:

```text
docs/superpowers/specs/2026-05-26-offline-retrieval-json-dataset-oracle-design.md
docs/superpowers/plans/2026-05-26-offline-retrieval-json-dataset-oracle-implementation-plan.md
tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj
tests/LightRAGNet.Tests/Evaluation/...
```

- [ ] **Step 2: Run focused evaluation tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~Evaluation" --verbosity minimal
```

Expected: pass.

- [ ] **Step 3: Run related retrieval tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~NaiveQueryService|FullyQualifiedName~RetrievalContext|FullyQualifiedName~RerankCoordinator|FullyQualifiedName~ReferenceListBuilder" --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Run plan self-review checks**

Run a local marker scan against the plan and changed test files before close-out. The scan must return no unfinished-work markers. Mentions of external systems such as real LLM services, Qdrant, Neo4j, Server, or React must appear only in design/plan boundary text, not in executable test code.

- [ ] **Step 6: Commit final verification fixes**

If verification required a small test-only fix, commit it:

```powershell
git add tests\LightRAGNet.Tests\Evaluation tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj
git commit -m "test: finalize JSON retrieval evaluation oracle"
```

If no changes were needed after verification, do not create an empty commit.

## Plan Self-Review

- Spec coverage:
  - JSON data files: Task 1.
  - Python-compatible dataset/oracle: Task 1 and Task 2.
  - LightRAGNet extended oracle for current cases: Task 1 and Task 4.
  - Loader validation: Task 2.
  - Fixture seeded from JSON: Task 3.
  - No real LLM/external services/frontend/API changes: File Structure and Task 5.
- Marker scan:
  - No unfinished-work marker instructions remain.
- Type consistency:
  - `RetrievalEvaluationDataSet`, JSON DTOs, and extended `RetrievalEvaluationCase` are introduced before use.
  - `RetrievalEvaluationFixture.CreateAsync(dataSet)` and `ApplyRankingHints(case)` are defined before the JSON-driven test calls them.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-26-offline-retrieval-json-dataset-oracle-implementation-plan.md`.

Two execution options:

1. Subagent-Driven (recommended) - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. Inline Execution - execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
