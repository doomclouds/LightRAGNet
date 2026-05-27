# RAGAS Evaluation API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, async, .NET-native RAGAS-compatible evaluation API for the current indexed workspace.

**Architecture:** Add Server-side evaluation DTOs, options, strict judge parsing, privacy-safe result snapshots, JSON run storage, built-in dataset loading, a query adapter around `LightRAG.QueryAsync`, a .NET-native OpenAI-compatible evaluator, an async run coordinator, and protected controller endpoints. Default tests use fake query and evaluator services, so `dotnet test` never requires evaluator keys, Qdrant, or Neo4j.

**Tech Stack:** .NET 10, ASP.NET Core controllers, `System.Text.Json`, `IOptions<T>`, `HttpClient`, xUnit, FluentAssertions, existing `LightRagServerFactory`.

---

## Locked Decisions

- API only; no UI.
- Real RAGAS-compatible LLM-as-judge scoring; not retrieval-only smoke.
- .NET-native evaluator; no Python worker.
- Current indexed workspace is evaluated; no automatic data seeding in this phase.
- Built-in dataset only; no path/upload/url request inputs.
- One active run at a time.
- Run persistence is `{LightRAG:WorkingDir}/evaluation/ragas_runs.json`.
- Secrets are accepted from config/header only and are never returned or persisted.

## File Structure

- Create: `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`
  - Public request and response DTOs used by API clients.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationOptions.cs`
  - `Evaluation:Ragas` config model.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationModels.cs`
  - Internal run, case, metric, diagnostics, dataset, and operation result models.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasJudgeResponseParser.cs`
  - Strict parser for four-metric judge JSON.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationTextSnapshotter.cs`
  - Preview/hash/full-text policy for answer, context, prompt, and response snapshots.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunStore.cs`
  - UTF-8 JSON persistence with an atomic temp-file write.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationDataLoader.cs`
  - Loads packaged `Evaluation/Data/sample_dataset.json`.
- Create: `src/LightRAGNet.Server/Services/Evaluation/IRagasRagQueryClient.cs`
  - Testable adapter interface for querying the current workspace.
- Create: `src/LightRAGNet.Server/Services/Evaluation/LightRagRagasQueryClient.cs`
  - Adapter over `LightRAG.QueryAsync`.
- Create: `src/LightRAGNet.Server/Services/Evaluation/IRagasEvaluator.cs`
  - Evaluator abstraction.
- Create: `src/LightRAGNet.Server/Services/Evaluation/OpenAiCompatibleRagasEvaluator.cs`
  - OpenAI-compatible chat completions evaluator.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunner.cs`
  - Executes selected cases, extracts contexts, calls evaluator, aggregates metrics.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunCoordinator.cs`
  - Creates async runs, enforces single-active-run guard, handles cancel/get.
- Create: `src/LightRAGNet.Server/Controllers/RagasEvaluationController.cs`
  - Protected create/get/cancel endpoints.
- Create directory and copied data files:
  - `src/LightRAGNet.Server/Evaluation/Data/sample_dataset.json`
  - `src/LightRAGNet.Server/Evaluation/Data/sample_retrieval_oracle.json`
  - `src/LightRAGNet.Server/Evaluation/Data/sample_documents/*`
- Modify: `src/LightRAGNet.Server/Program.cs`
  - Register options and services.
- Modify: `src/LightRAGNet.Server/LightRAGNet.Server.csproj`
  - Copy Server-owned evaluation data to output.
- Create tests under `tests/LightRAGNet.Server.Tests/Evaluation/`
  - `RagasJudgeResponseParserTests.cs`
  - `RagasEvaluationTextSnapshotterTests.cs`
  - `RagasEvaluationRunStoreTests.cs`
  - `RagasEvaluationDataLoaderTests.cs`
  - `OpenAiCompatibleRagasEvaluatorTests.cs`
  - `RagasEvaluationRunnerTests.cs`
  - `RagasEvaluationRunCoordinatorTests.cs`
  - `RagasEvaluationControllerTests.cs`

## Shared Type Contracts

These contracts keep names stable across tasks.

### Public DTOs

Create `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Share.Models;

public sealed class CreateRagasEvaluationRunRequest
{
    public List<string> CaseNames { get; set; } = [];
    public int? MaxCases { get; set; }
    public bool IncludeFullText { get; set; }
    public RagasEvaluationQueryOptions Query { get; set; } = new();
}

public sealed class RagasEvaluationQueryOptions
{
    public QueryMode Mode { get; set; } = QueryMode.Mix;
    public int TopK { get; set; } = 40;
    public int ChunkTopK { get; set; } = 20;
    public bool EnableRerank { get; set; } = true;
}

public sealed class CreateRagasEvaluationRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class RagasEvaluationRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EvaluationType { get; set; } = "ragas-compatible";
    public string EvaluatorBackend { get; set; } = "dotnet-native";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RagasEvaluationRequestSnapshot Request { get; set; } = new();
    public RagasEvaluationSummaryDto Summary { get; set; } = new();
    public List<RagasEvaluationCaseResultDto> Cases { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class RagasEvaluationRequestSnapshot
{
    public List<string> CaseNames { get; set; } = [];
    public int MaxCases { get; set; }
    public bool IncludeFullText { get; set; }
    public int PreviewMaxChars { get; set; }
    public RagasEvaluationQueryOptions Query { get; set; } = new();
}

public sealed class RagasEvaluationSummaryDto
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public RagasEvaluationMetricsDto AverageMetrics { get; set; } = new();
}

public sealed class RagasEvaluationMetricsDto
{
    public double? Faithfulness { get; set; }
    public double? AnswerRelevance { get; set; }
    public double? ContextRecall { get; set; }
    public double? ContextPrecision { get; set; }
    public double? RagasScore { get; set; }
}

public sealed class RagasEvaluationCaseResultDto
{
    public string CaseName { get; set; } = string.Empty;
    public string QuestionPreview { get; set; } = string.Empty;
    public string GroundTruthPreview { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public RagasEvaluationMetricsDto Metrics { get; set; } = new();
    public List<RagasEvaluationMetricReasonDto> Reasons { get; set; } = [];
    public string AnswerPreview { get; set; } = string.Empty;
    public string AnswerHash { get; set; } = string.Empty;
    public string? AnswerText { get; set; }
    public List<RagasEvaluationContextSnapshotDto> Contexts { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
}

public sealed class RagasEvaluationMetricReasonDto
{
    public string Metric { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RagasEvaluationContextSnapshotDto
{
    public string Preview { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
}

public sealed class RagasEvaluationDiagnosticDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Details { get; set; } = [];
}
```

### Server Options

Create `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationOptions.cs`:

```csharp
namespace LightRAGNet.Server.Services.Evaluation;

public sealed class RagasEvaluationOptions
{
    public bool Enabled { get; set; }
    public string AdminToken { get; set; } = string.Empty;
    public string EvaluatorModel { get; set; } = "deepseek-v4-flash";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxConcurrentCases { get; set; } = 1;
    public int MaxCasesPerRun { get; set; } = 5;
    public bool AllowPersistFullText { get; set; }
    public int PreviewMaxChars { get; set; } = 500;
    public bool PersistJudgePrompts { get; set; } = true;
    public bool PersistJudgeResponses { get; set; } = true;
}
```

### Internal Models

Create `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationModels.cs`:

```csharp
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal enum RagasEvaluationRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

internal enum RagasEvaluationCaseStatus
{
    Succeeded,
    Failed,
    Cancelled
}

internal sealed record RagasDatasetCase(
    string CaseName,
    string Question,
    string GroundTruth,
    string Project);

internal sealed record RagasMetricScore(double Score, string Reason);

internal sealed record RagasMetricSet(
    RagasMetricScore Faithfulness,
    RagasMetricScore AnswerRelevance,
    RagasMetricScore ContextRecall,
    RagasMetricScore ContextPrecision)
{
    public double RagasScore =>
        (Faithfulness.Score + AnswerRelevance.Score + ContextRecall.Score + ContextPrecision.Score) / 4.0;
}

internal sealed record RagasJudgeParseResult(
    bool Success,
    RagasMetricSet? Metrics,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static RagasJudgeParseResult Succeeded(RagasMetricSet metrics) =>
        new(true, metrics, null, null);

    public static RagasJudgeParseResult Failed(string code, string message) =>
        new(false, null, code, message);
}

internal sealed record RagasTextSnapshot(
    string Preview,
    string Hash,
    string? Text);

internal sealed record RagasContextSnapshot(
    string Preview,
    string Hash,
    string? Text,
    string ChunkId,
    string FilePath,
    string ReferenceId);

internal sealed class RagasEvaluationRunRecord
{
    public string RunId { get; set; } = string.Empty;
    public RagasEvaluationRunStatus Status { get; set; } = RagasEvaluationRunStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RagasEvaluationRequestSnapshot Request { get; set; } = new();
    public RagasEvaluationSummaryDto Summary { get; set; } = new();
    public List<RagasEvaluationCaseResultDto> Cases { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
    public string? Error { get; set; }
}

internal sealed record RagasEvaluationOperationResult<T>(
    bool Success,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    int StatusCode)
{
    public static RagasEvaluationOperationResult<T> Ok(T value) =>
        new(true, value, null, null, StatusCodes.Status200OK);

    public static RagasEvaluationOperationResult<T> Fail(string code, string message, int statusCode) =>
        new(false, default, code, message, statusCode);
}

internal sealed record RagasRetrievedContext(
    string Content,
    string ChunkId,
    string FilePath,
    string ReferenceId);

internal sealed record RagasQueryExecutionResult(
    string Answer,
    IReadOnlyList<RagasRetrievedContext> Contexts,
    QueryMode Mode);

internal sealed record RagasEvaluationCaseInput(
    string CaseName,
    string Question,
    string GroundTruth,
    IReadOnlyList<RagasRetrievedContext> Contexts,
    string Answer);

internal sealed record RagasEvaluatorResult(
    string RawResponse,
    RagasJudgeParseResult ParseResult,
    string Prompt);
```

## Task 1: Add Contracts and Registration Shell

**Files:**

- Create: `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationOptions.cs`
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationModels.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`

- [ ] **Step 1: Add the three contract files**

Use the code blocks from `Public DTOs`, `Server Options`, and `Internal Models`.

- [ ] **Step 2: Register options in `Program.cs`**

Add:

```csharp
using LightRAGNet.Server.Services.Evaluation;
```

Then add after `builder.Services.AddLightRAG(builder.Configuration);`:

```csharp
builder.Services.Configure<RagasEvaluationOptions>(
    builder.Configuration.GetSection("Evaluation:Ragas"));
```

- [ ] **Step 3: Run compile check**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal
```

Expected: build succeeds and reports no matching tests, or build succeeds with zero RAGAS tests discovered.

## Task 2: Add Strict Judge Response Parser

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasJudgeResponseParser.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasJudgeResponseParserTests.cs`

- [ ] **Step 1: Write parser tests first**

Create tests covering:

```csharp
[Fact]
public void Parse_ValidFourMetricJson_ReturnsMetricSet()
{
    const string json = """
    {
      "faithfulness": { "score": 0.8, "reason": "supported" },
      "answer_relevance": { "score": 0.9, "reason": "direct" },
      "context_recall": { "score": 0.7, "reason": "facts present" },
      "context_precision": { "score": 0.6, "reason": "limited noise" }
    }
    """;

    var result = new RagasJudgeResponseParser().Parse(json);

    result.Success.Should().BeTrue();
    result.Metrics!.Faithfulness.Score.Should().Be(0.8);
    result.Metrics.RagasScore.Should().BeApproximately(0.75, 0.0001);
}

[Theory]
[InlineData("{ not-json", "invalid_json")]
[InlineData("""{"faithfulness":{"score":0.5,"reason":"x"}}""", "missing_metric")]
[InlineData("""
{
  "faithfulness": { "score": 2, "reason": "x" },
  "answer_relevance": { "score": 0.9, "reason": "x" },
  "context_recall": { "score": 0.7, "reason": "x" },
  "context_precision": { "score": 0.6, "reason": "x" }
}
""", "invalid_score")]
[InlineData("""
{
  "faithfulness": { "score": 0.8, "reason": "" },
  "answer_relevance": { "score": 0.9, "reason": "x" },
  "context_recall": { "score": 0.7, "reason": "x" },
  "context_precision": { "score": 0.6, "reason": "x" }
}
""", "missing_reason")]
public void Parse_InvalidJudgeJson_ReturnsTypedFailure(string json, string expectedCode)
{
    var result = new RagasJudgeResponseParser().Parse(json);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(expectedCode);
    result.Metrics.Should().BeNull();
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasJudgeResponseParserTests" --no-restore --verbosity minimal
```

Expected: build fails because `RagasJudgeResponseParser` does not exist.

- [ ] **Step 3: Implement parser**

Create parser with this public surface:

```csharp
internal sealed class RagasJudgeResponseParser
{
    public RagasJudgeParseResult Parse(string json)
    {
        // Parse with JsonDocument.
        // Require four metric properties:
        // faithfulness, answer_relevance, context_recall, context_precision.
        // Require score number between 0 and 1.
        // Require non-empty reason string.
        // Return typed failures instead of throwing for bad judge output.
    }
}
```

Use helper methods:

```csharp
private static bool TryReadMetric(
    JsonElement root,
    string jsonName,
    out RagasMetricScore? metric,
    out RagasJudgeParseResult? failure)
```

Failure codes:

- `invalid_json`
- `missing_metric`
- `missing_score`
- `invalid_score`
- `missing_reason`

- [ ] **Step 4: Verify GREEN**

Run the same parser test command.

Expected: all `RagasJudgeResponseParserTests` pass.

## Task 3: Add Text Snapshot Privacy Helper

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationTextSnapshotter.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationTextSnapshotterTests.cs`

- [ ] **Step 1: Write snapshotter tests**

Test default privacy:

```csharp
[Fact]
public void Snapshot_DefaultPolicy_StoresPreviewAndHashOnly()
{
    var snapshotter = new RagasEvaluationTextSnapshotter(
        Options.Create(new RagasEvaluationOptions { PreviewMaxChars = 5 }));

    var snapshot = snapshotter.Snapshot("abcdef", includeFullText: false);

    snapshot.Preview.Should().Be("abcde");
    snapshot.Hash.Should().HaveLength(64);
    snapshot.Text.Should().BeNull();
}
```

Test full text guard:

```csharp
[Fact]
public void ValidateFullTextRequest_WhenConfigDisallowsFullText_ReturnsFailure()
{
    var snapshotter = new RagasEvaluationTextSnapshotter(
        Options.Create(new RagasEvaluationOptions { AllowPersistFullText = false }));

    var result = snapshotter.ValidateFullTextRequest(includeFullText: true);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be("full_text_disabled");
}
```

- [ ] **Step 2: Implement snapshotter**

Required public surface:

```csharp
internal sealed class RagasEvaluationTextSnapshotter(IOptions<RagasEvaluationOptions> options)
{
    public RagasEvaluationOperationResult<object> ValidateFullTextRequest(bool includeFullText)
    {
        var value = options.Value;
        if (includeFullText && !value.AllowPersistFullText)
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "full_text_disabled",
                "Full-text persistence is disabled by Evaluation:Ragas:AllowPersistFullText.",
                StatusCodes.Status400BadRequest);
        }

        return RagasEvaluationOperationResult<object>.Ok(new object());
    }

    public RagasTextSnapshot Snapshot(string text, bool includeFullText)
    {
        var value = options.Value;
        var previewLength = Math.Max(0, value.PreviewMaxChars);
        var preview = text.Length <= previewLength ? text : text[..previewLength];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new RagasTextSnapshot(preview, hash, includeFullText && value.AllowPersistFullText ? text : null);
    }
}
```

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationTextSnapshotterTests" --no-restore --verbosity minimal
```

Expected: all snapshotter tests pass.

## Task 4: Add JSON Run Store

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunStore.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunStoreTests.cs`

- [ ] **Step 1: Write store tests**

Cover:

- save then reload by constructing a new store with the same temp WorkingDir,
- missing run returns null,
- active run query returns `Queued` or `Running`,
- completed run is not active.

Use configuration:

```csharp
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["LightRAG:WorkingDir"] = tempDirectory
    })
    .Build();
```

- [ ] **Step 2: Implement store surface**

Create:

```csharp
internal sealed class RagasEvaluationRunStore(IConfiguration configuration)
{
    public Task<IReadOnlyList<RagasEvaluationRunRecord>> LoadAllAsync(CancellationToken cancellationToken);
    public Task<RagasEvaluationRunRecord?> GetAsync(string runId, CancellationToken cancellationToken);
    public Task<RagasEvaluationRunRecord?> GetActiveAsync(CancellationToken cancellationToken);
    public Task UpsertAsync(RagasEvaluationRunRecord run, CancellationToken cancellationToken);
}
```

Path rule:

```csharp
var workingDir = configuration["LightRAG:WorkingDir"] ?? "rag_storage";
if (!Path.IsPathRooted(workingDir))
{
    workingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workingDir);
}

var filePath = Path.Combine(workingDir, "evaluation", "ragas_runs.json");
```

Write rule:

```csharp
var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
if (File.Exists(filePath))
{
    File.Delete(filePath);
}

File.Move(tempPath, filePath);
```

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunStoreTests" --no-restore --verbosity minimal
```

Expected: all store tests pass.

## Task 5: Add Built-In Dataset Loader

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationDataLoader.cs`
- Copy data into: `src/LightRAGNet.Server/Evaluation/Data/`
- Modify: `src/LightRAGNet.Server/LightRAGNet.Server.csproj`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationDataLoaderTests.cs`

- [ ] **Step 1: Copy dataset into Server project**

Run:

```powershell
New-Item -ItemType Directory -Force src\LightRAGNet.Server\Evaluation\Data | Out-Null
Copy-Item tests\LightRAGNet.Tests\Evaluation\Data\sample_dataset.json src\LightRAGNet.Server\Evaluation\Data\sample_dataset.json -Force
Copy-Item tests\LightRAGNet.Tests\Evaluation\Data\sample_retrieval_oracle.json src\LightRAGNet.Server\Evaluation\Data\sample_retrieval_oracle.json -Force
Copy-Item tests\LightRAGNet.Tests\Evaluation\Data\sample_documents src\LightRAGNet.Server\Evaluation\Data\sample_documents -Recurse -Force
```

- [ ] **Step 2: Add csproj packaging**

Add to `src/LightRAGNet.Server/LightRAGNet.Server.csproj`:

```xml
<ItemGroup>
  <None Include="Evaluation\Data\**\*.*"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 3: Write loader tests**

Cover:

- default load returns at least one case,
- `caseNames` filters to exact requested names,
- unknown case returns `unknown_case`,
- `maxCases` greater than configured max returns `max_cases_exceeded`.

- [ ] **Step 4: Implement loader surface**

Create:

```csharp
internal sealed class RagasEvaluationDataLoader(IOptions<RagasEvaluationOptions> options)
{
    public Task<RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>> LoadCasesAsync(
        IReadOnlyList<string> caseNames,
        int? maxCases,
        CancellationToken cancellationToken)
}
```

Case naming rule:

```csharp
private static string BuildCaseName(JsonElement item, int index)
{
    if (item.TryGetProperty("case_name", out var caseName) && caseName.ValueKind == JsonValueKind.String)
    {
        return caseName.GetString() ?? $"case-{index + 1}";
    }

    if (item.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.String)
    {
        return $"case-{index + 1}-{Slug(question.GetString() ?? string.Empty)}";
    }

    return $"case-{index + 1}";
}
```

Data path:

```csharp
var dataPath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "Evaluation",
    "Data",
    "sample_dataset.json");
```

- [ ] **Step 5: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationDataLoaderTests" --no-restore --verbosity minimal
```

Expected: all loader tests pass.

## Task 6: Add Query Adapter

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/IRagasRagQueryClient.cs`
- Create: `src/LightRAGNet.Server/Services/Evaluation/LightRagRagasQueryClient.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/LightRagRagasQueryClientTests.cs`

- [ ] **Step 1: Define adapter interface**

```csharp
internal interface IRagasRagQueryClient
{
    Task<RagasQueryExecutionResult> QueryAsync(
        RagasDatasetCase dataSetCase,
        RagasEvaluationQueryOptions options,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement adapter**

`LightRagRagasQueryClient` must build:

```csharp
var queryParam = new QueryParam
{
    Mode = options.Mode,
    Stream = false,
    IncludeReferences = true,
    OnlyNeedContext = false,
    OnlyNeedPrompt = false,
    TopK = options.TopK,
    ChunkTopK = options.ChunkTopK,
    EnableRerank = options.EnableRerank
};
```

Then call:

```csharp
var result = await lightRAG.QueryAsync(dataSetCase.Question, queryParam, cancellationToken);
```

Extract contexts from `result.RawData["data"]["chunks"]` supporting both object dictionaries and `JsonElement` values. Required context fields:

- `content`
- `chunk_id`
- `file_path`
- `reference_id`

Return answer from `result.Content ?? string.Empty`.

- [ ] **Step 3: Write adapter tests**

Use `ApiRetrievalEvaluationTestDoubles.CreateServerFactory(dataSet)` if it still gives enough indexed fake data. Otherwise test context extraction through an internal static method:

```csharp
internal static IReadOnlyList<RagasRetrievedContext> ExtractContexts(Dictionary<string, object>? rawData)
```

Cover dictionary raw data and `JsonElement` raw data.

- [ ] **Step 4: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~LightRagRagasQueryClientTests" --no-restore --verbosity minimal
```

Expected: all query adapter tests pass.

## Task 7: Add OpenAI-Compatible Evaluator

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/IRagasEvaluator.cs`
- Create: `src/LightRAGNet.Server/Services/Evaluation/OpenAiCompatibleRagasEvaluator.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/OpenAiCompatibleRagasEvaluatorTests.cs`

- [ ] **Step 1: Define evaluator interface**

```csharp
internal interface IRagasEvaluator
{
    Task<RagasEvaluatorResult> EvaluateAsync(
        RagasEvaluationCaseInput input,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write fake HTTP tests**

Use a custom `HttpMessageHandler` to capture request content and return:

```json
{
  "choices": [
    {
      "message": {
        "content": "{\"faithfulness\":{\"score\":0.8,\"reason\":\"supported\"},\"answer_relevance\":{\"score\":0.9,\"reason\":\"direct\"},\"context_recall\":{\"score\":0.7,\"reason\":\"facts\"},\"context_precision\":{\"score\":0.6,\"reason\":\"focused\"}}"
      }
    }
  ]
}
```

Assertions:

- `Authorization` header is bearer but diagnostics never expose the key,
- request model equals `EvaluatorModel`,
- prompt asks for strict JSON only,
- prompt contains question, answer, contexts, and ground truth,
- parsed metrics are returned.

- [ ] **Step 3: Implement evaluator**

Build URL:

```csharp
var baseUrl = string.IsNullOrWhiteSpace(options.Value.BaseUrl)
    ? "https://api.deepseek.com"
    : options.Value.BaseUrl.TrimEnd('/');
var endpoint = $"{baseUrl}/chat/completions";
```

Request shape:

```csharp
var payload = new
{
    model = options.Value.EvaluatorModel,
    temperature = 0,
    messages = new[]
    {
        new { role = "system", content = "You are a RAG evaluation judge. Return strict JSON only." },
        new { role = "user", content = prompt }
    }
};
```

Parse `choices[0].message.content`, then call `RagasJudgeResponseParser.Parse`.

- [ ] **Step 4: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~OpenAiCompatibleRagasEvaluatorTests" --no-restore --verbosity minimal
```

Expected: all evaluator tests pass.

## Task 8: Add Runner

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunner.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunnerTests.cs`

- [ ] **Step 1: Write runner tests with fakes**

Create fake `IRagasRagQueryClient` and fake `IRagasEvaluator`.

Cover:

- two successful cases aggregate average metrics,
- no retrieved contexts marks case failed and does not call evaluator,
- evaluator parse failure marks case failed,
- cancellation marks run cancelled.

- [ ] **Step 2: Implement runner surface**

```csharp
internal sealed class RagasEvaluationRunner(
    RagasEvaluationRunStore store,
    IRagasRagQueryClient queryClient,
    IRagasEvaluator evaluator,
    RagasEvaluationTextSnapshotter snapshotter,
    ILogger<RagasEvaluationRunner> logger)
{
    public Task ExecuteAsync(
        RagasEvaluationRunRecord run,
        IReadOnlyList<RagasDatasetCase> cases,
        CancellationToken cancellationToken);
}
```

Execution order:

1. set `Status=Running`, `StartedAt=UtcNow`, save,
2. for each case call query adapter,
3. if contexts are empty, append failed case with diagnostic `no_contexts`,
4. call evaluator once per case,
5. if parser failed, append failed case with parser diagnostic,
6. snapshot answer/context text,
7. compute summary averages from succeeded cases only,
8. set terminal status to `Completed`, `Cancelled`, or `Failed`,
9. save after each case and after terminal status.

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunnerTests" --no-restore --verbosity minimal
```

Expected: all runner tests pass.

## Task 9: Add Run Coordinator

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunCoordinator.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunCoordinatorTests.cs`

- [ ] **Step 1: Write coordinator tests**

Cover:

- create returns queued run and stores it,
- second create while active returns `409`,
- cancel active run calls cancellation token source and returns current record,
- get unknown run returns `404`,
- disabled config returns `403`,
- missing admin token or evaluator key returns `503`.

- [ ] **Step 2: Implement coordinator surface**

```csharp
internal sealed class RagasEvaluationRunCoordinator(
    IOptions<RagasEvaluationOptions> options,
    RagasEvaluationDataLoader dataLoader,
    RagasEvaluationRunStore store,
    RagasEvaluationRunner runner,
    RagasEvaluationTextSnapshotter snapshotter,
    ILogger<RagasEvaluationRunCoordinator> logger)
{
    public Task<RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>> CreateAsync(
        CreateRagasEvaluationRunRequest request,
        CancellationToken cancellationToken);

    public Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> GetAsync(
        string runId,
        CancellationToken cancellationToken);

    public Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> CancelAsync(
        string runId,
        CancellationToken cancellationToken);
}
```

Run id format:

```csharp
var runId = $"ragas-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29];
```

Background execution:

```csharp
var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
activeRuns[run.RunId] = cts;
_ = Task.Run(async () =>
{
    try
    {
        await runner.ExecuteAsync(run, cases, cts.Token);
    }
    finally
    {
        activeRuns.TryRemove(run.RunId, out _);
        cts.Dispose();
    }
});
```

Use a `SemaphoreSlim` or `lock` around create-run active checks so two simultaneous creates cannot both pass.

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunCoordinatorTests" --no-restore --verbosity minimal
```

Expected: all coordinator tests pass.

## Task 10: Add Controller and Auth

**Files:**

- Create: `src/LightRAGNet.Server/Controllers/RagasEvaluationController.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationControllerTests.cs`

- [ ] **Step 1: Write controller tests**

Using `LightRagServerFactory`, cover:

- missing `X-Evaluation-Token` returns `401`,
- wrong token returns `401`,
- valid token reaches coordinator,
- create/get/cancel endpoints return expected status codes,
- active run conflict returns `409`,
- disabled endpoint returns `403`,
- misconfigured endpoint returns `503`.

- [ ] **Step 2: Implement controller**

Route:

```csharp
[ApiController]
[Route("api/evaluation/ragas/runs")]
public sealed class RagasEvaluationController(
    IOptions<RagasEvaluationOptions> options,
    RagasEvaluationRunCoordinator coordinator) : ControllerBase
```

Token check:

```csharp
private bool HasValidToken()
{
    var configured = options.Value.AdminToken;
    if (string.IsNullOrWhiteSpace(configured))
    {
        return false;
    }

    return Request.Headers.TryGetValue("X-Evaluation-Token", out var provided)
        && string.Equals(provided.ToString(), configured, StringComparison.Ordinal);
}
```

Endpoints:

```csharp
[HttpPost]
public async Task<ActionResult<CreateRagasEvaluationRunResponse>> CreateAsync(
    [FromBody] CreateRagasEvaluationRunRequest? request,
    CancellationToken cancellationToken)

[HttpGet("{runId}")]
public async Task<ActionResult<RagasEvaluationRunResponse>> GetAsync(
    string runId,
    CancellationToken cancellationToken)

[HttpPost("{runId}/cancel")]
public async Task<ActionResult<RagasEvaluationRunResponse>> CancelAsync(
    string runId,
    CancellationToken cancellationToken)
```

Convert operation result:

```csharp
return result.Success
    ? StatusCode(result.StatusCode, result.Value)
    : StatusCode(result.StatusCode, new { code = result.ErrorCode, message = result.ErrorMessage });
```

- [ ] **Step 3: Register services**

In `Program.cs`, add after RAGAS options:

```csharp
builder.Services.AddSingleton<RagasJudgeResponseParser>();
builder.Services.AddSingleton<RagasEvaluationTextSnapshotter>();
builder.Services.AddSingleton<RagasEvaluationRunStore>();
builder.Services.AddSingleton<RagasEvaluationDataLoader>();
builder.Services.AddScoped<IRagasRagQueryClient, LightRagRagasQueryClient>();
builder.Services.AddHttpClient<IRagasEvaluator, OpenAiCompatibleRagasEvaluator>();
builder.Services.AddSingleton<RagasEvaluationRunner>();
builder.Services.AddSingleton<RagasEvaluationRunCoordinator>();
```

If scoped `LightRAG` cannot be consumed safely from singleton runner/coordinator during implementation, change runner and coordinator lifetimes to scoped and move background execution through `IServiceScopeFactory`. The test must prove create-run returns immediately and the background path still resolves scoped services correctly.

- [ ] **Step 4: Verify**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationControllerTests" --no-restore --verbosity minimal
```

Expected: all controller tests pass.

## Task 11: End-to-End Verification and Docs Note

**Files:**

- Modify: `docs/superpowers/archives/2026-05/2026-05-27-ragas-evaluation-api-archives.md` only after implementation is accepted and verified.

- [ ] **Step 1: Run focused RAGAS tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal
```

Expected: all RAGAS tests pass.

- [ ] **Step 2: Run full Server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal
```

Expected: all Server tests pass.

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: all solution tests pass.

- [ ] **Step 4: Add manual real-evaluator smoke note**

Add a short section to the eventual archive with:

```json
{
  "Evaluation": {
    "Ragas": {
      "Enabled": true,
      "AdminToken": "<local secret>",
      "EvaluatorModel": "deepseek-v4-flash",
      "ApiKey": "<local secret or DEEPSEEK_API_KEY>",
      "BaseUrl": "https://api.deepseek.com"
    }
  }
}
```

Sample request:

```http
POST /api/evaluation/ragas/runs
X-Evaluation-Token: <local secret>
Content-Type: application/json

{
  "caseNames": [],
  "maxCases": 1,
  "includeFullText": false,
  "query": {
    "mode": "Mix",
    "topK": 40,
    "chunkTopK": 20,
    "enableRerank": true
  }
}
```

Warning text:

```text
Real evaluator smoke calls external model APIs and may cost money. It is opt-in and is not part of the default test suite.
```

## Plan Self-Review

- Spec coverage:
  - Async API endpoints: Tasks 9-10.
  - WorkingDir JSON store: Task 4.
  - Built-in dataset and case filtering: Task 5.
  - Query current workspace: Task 6 and Task 8.
  - .NET-native evaluator and strict judge JSON: Task 2 and Task 7.
  - Security and config: Task 1, Task 9, Task 10.
  - Text privacy: Task 3 and Task 8.
  - Default tests without external services: Tasks 2-10.
  - No UI: locked decision and no frontend files.
- Red-flag scan:
  - No unresolved marker words remain.
  - Ambiguous packaging path is resolved by copying dataset into Server project.
  - Runner testability is resolved by `IRagasRagQueryClient`.
- Type consistency:
  - DTOs, internal models, options, query adapter, evaluator, runner, coordinator, and controller use the same names across tasks.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-27-ragas-evaluation-api-implementation-plan.md`.

Recommended execution after review: implement Tasks 1-4 first, run focused tests, then continue with Tasks 5-8, and finish with Tasks 9-11. Pause before real evaluator smoke because it requires local secrets and may call paid APIs.
