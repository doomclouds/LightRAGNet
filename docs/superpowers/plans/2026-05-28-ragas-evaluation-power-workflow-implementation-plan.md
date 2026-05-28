# RAGAS Evaluation Power Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reproducible RAGAS evaluation operations workflow with run listing, safe JSON/CSV export, benchmark statistics, baseline comparison, and an opt-in real evaluator smoke guide.

**Architecture:** Extend the existing Server RAGAS API without changing create/get/cancel semantics. Keep public API contracts in `LightRAGNet.Share`, keep persistence in the current WorkingDir JSON store, add small focused Server services for export and comparison, and preserve fake-only automated tests.

**Tech Stack:** .NET 10, ASP.NET Core controllers, xUnit, FluentAssertions, `System.Text.Json`, UTF-8 CSV generation, existing `LightRagServerFactory` test infrastructure.

---

## File Structure

- Modify: `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`
  - Add list/export/compare DTOs and extend benchmark summary fields.
- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunStore.cs`
  - Add sorted list support.
- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunner.cs`
  - Populate benchmark summary fields.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationExportService.cs`
  - Build safe JSON export and RFC4180-style CSV content.
- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationComparisonService.cs`
  - Compare current and baseline runs.
- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunCoordinator.cs`
  - Add list/export/compare operations.
- Modify: `src/LightRAGNet.Server/Controllers/RagasEvaluationController.cs`
  - Add list/export/compare endpoints.
- Modify: `src/LightRAGNet.Server/Program.cs`
  - Register export and comparison services.
- Create: `docs/evaluation/ragas-power-workflow.md`
  - Document the opt-in real evaluator smoke workflow.
- Modify or create tests under `tests/LightRAGNet.Server.Tests/Evaluation/`
  - `RagasEvaluationRunStoreTests.cs`
  - `RagasEvaluationRunnerTests.cs`
  - `RagasEvaluationExportServiceTests.cs`
  - `RagasEvaluationComparisonServiceTests.cs`
  - `RagasEvaluationRunCoordinatorTests.cs`
  - `RagasEvaluationControllerTests.cs`

## Shared Contracts

Add these DTOs to `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`:

```csharp
public sealed class RagasEvaluationRunListResponse
{
    public List<RagasEvaluationRunSummaryItemDto> Runs { get; set; } = [];
}

public sealed class RagasEvaluationRunSummaryItemDto
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public double? RagasScore { get; set; }
    public double? DurationSeconds { get; set; }
}

public sealed class RagasEvaluationComparisonResponse
{
    public string RunId { get; set; } = string.Empty;
    public string BaselineRunId { get; set; } = string.Empty;
    public string Status { get; set; } = "Comparable";
    public Dictionary<string, RagasEvaluationMetricComparisonDto> Metrics { get; set; } = [];
    public RagasEvaluationCaseCountComparisonDto CaseCounts { get; set; } = new();
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
}

public sealed class RagasEvaluationMetricComparisonDto
{
    public double? Baseline { get; set; }
    public double? Current { get; set; }
    public double? Delta { get; set; }
    public string Direction { get; set; } = "NotMeasured";
}

public sealed class RagasEvaluationCaseCountComparisonDto
{
    public int BaselineTotal { get; set; }
    public int CurrentTotal { get; set; }
    public int MatchedCases { get; set; }
}
```

Extend `RagasEvaluationSummaryDto`:

```csharp
public double? SuccessRate { get; set; }
public double? ElapsedTimeSeconds { get; set; }
public double? AverageSecondsPerCase { get; set; }
public double? MinRagasScore { get; set; }
public double? MaxRagasScore { get; set; }
public Dictionary<string, int> FailureReasons { get; set; } = [];
```

## Task 1: Add Shared DTOs and Compile

**Files:**

- Modify: `src/LightRAGNet.Share/Models/RagasEvaluationRequests.cs`

- [ ] **Step 1: Add the DTOs**

Append the shared contracts from this plan to `RagasEvaluationRequests.cs` and extend `RagasEvaluationSummaryDto` with the benchmark fields.

- [ ] **Step 2: Run compile check**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal
```

Expected: compile succeeds. Existing RAGAS tests may fail only if they compare complete serialized DTO shapes; update those expected shapes in later tasks.

## Task 2: Add Store List Support

**Files:**

- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunStore.cs`
- Modify: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunStoreTests.cs`

- [ ] **Step 1: Add failing tests**

Add tests:

```csharp
[Fact]
public async Task ListAsync_WhenStoreMissing_ReturnsEmptyList()
{
    var store = CreateStore();

    var runs = await store.ListAsync(CancellationToken.None);

    runs.Should().BeEmpty();
}

[Fact]
public async Task ListAsync_ReturnsRunsNewestFirst()
{
    var store = CreateStore();
    var older = CreateRun("ragas-older", new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero));
    var newer = CreateRun("ragas-newer", new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero));
    await store.UpsertAsync(older, CancellationToken.None);
    await store.UpsertAsync(newer, CancellationToken.None);

    var runs = await store.ListAsync(CancellationToken.None);

    runs.Select(run => run.RunId).Should().Equal("ragas-newer", "ragas-older");
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunStoreTests.ListAsync" --no-restore --verbosity minimal
```

Expected: build fails because `ListAsync` does not exist.

- [ ] **Step 3: Implement `ListAsync`**

Add to `RagasEvaluationRunStore`:

```csharp
public async Task<IReadOnlyList<RagasEvaluationRunRecord>> ListAsync(CancellationToken cancellationToken)
{
    await gate.WaitAsync(cancellationToken);
    try
    {
        var runs = await LoadAllUnlockedAsync(cancellationToken);

        return runs
            .OrderByDescending(run => run.CreatedAt)
            .ToArray();
    }
    finally
    {
        gate.Release();
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run the same focused test command.

Expected: `ListAsync` tests pass.

## Task 3: Enrich Benchmark Summary

**Files:**

- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunner.cs`
- Modify: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunnerTests.cs`

- [ ] **Step 1: Add summary tests**

Add tests covering:

```csharp
[Fact]
public async Task ExecuteAsync_WhenCasesComplete_ComputesBenchmarkStatistics()
{
    var fixture = CreateSuccessfulRunnerFixture(caseCount: 2);
    var run = CreateRun();

    await fixture.Runner.ExecuteAsync(run, fixture.Cases, CancellationToken.None);

    run.Summary.SuccessRate.Should().Be(1.0);
    run.Summary.MinRagasScore.Should().BeGreaterThan(0);
    run.Summary.MaxRagasScore.Should().BeGreaterThan(0);
    run.Summary.ElapsedTimeSeconds.Should().BeGreaterThan(0);
    run.Summary.AverageSecondsPerCase.Should().BeGreaterThan(0);
    run.Summary.FailureReasons.Should().BeEmpty();
}

[Fact]
public async Task ExecuteAsync_WhenCaseHasNoContexts_AddsFailureReason()
{
    var fixture = CreateNoContextRunnerFixture();
    var run = CreateRun();

    await fixture.Runner.ExecuteAsync(run, fixture.Cases, CancellationToken.None);

    run.Summary.Succeeded.Should().Be(0);
    run.Summary.Failed.Should().Be(1);
    run.Summary.SuccessRate.Should().Be(0);
    run.Summary.MinRagasScore.Should().BeNull();
    run.Summary.MaxRagasScore.Should().BeNull();
    run.Summary.FailureReasons.Should().ContainKey("no_contexts").WhoseValue.Should().Be(1);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunnerTests" --no-restore --verbosity minimal
```

Expected: summary assertions fail until the runner populates new fields.

- [ ] **Step 3: Update summary calculation**

Change `CreateSummary` to accept timestamps:

```csharp
private static RagasEvaluationSummaryDto CreateSummary(
    int total,
    IReadOnlyList<RagasEvaluationCaseResultDto> results,
    int cancelledRemaining,
    DateTimeOffset? startedAt,
    DateTimeOffset? completedAt)
```

Compute:

```csharp
var succeeded = results
    .Where(result => result.Status == RagasEvaluationCaseStatus.Succeeded.ToString())
    .ToArray();
var failed = results
    .Where(result => result.Status == RagasEvaluationCaseStatus.Failed.ToString())
    .ToArray();
var elapsed = startedAt is not null && completedAt is not null
    ? (completedAt.Value - startedAt.Value).TotalSeconds
    : (double?)null;
var scores = succeeded
    .Select(result => result.Metrics.RagasScore)
    .Where(score => score.HasValue)
    .Select(score => score!.Value)
    .ToArray();

return new RagasEvaluationSummaryDto
{
    Total = total,
    Succeeded = succeeded.Length,
    Failed = failed.Length,
    Cancelled = results.Count(result => result.Status == RagasEvaluationCaseStatus.Cancelled.ToString())
        + cancelledRemaining,
    AverageMetrics = AverageMetrics(succeeded),
    SuccessRate = total > 0 ? (double)succeeded.Length / total : null,
    ElapsedTimeSeconds = elapsed,
    AverageSecondsPerCase = elapsed is not null && total > 0 ? elapsed.Value / total : null,
    MinRagasScore = scores.Length > 0 ? scores.Min() : null,
    MaxRagasScore = scores.Length > 0 ? scores.Max() : null,
    FailureReasons = failed
        .SelectMany(result => result.Diagnostics)
        .GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
};
```

Update every `CreateSummary(...)` call to pass `run.StartedAt` and `run.CompletedAt`.

- [ ] **Step 4: Verify GREEN**

Run the runner tests again.

Expected: benchmark summary tests pass.

## Task 4: Add Export Service

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationExportService.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationExportServiceTests.cs`

- [ ] **Step 1: Write export tests**

Create tests:

```csharp
[Fact]
public void ExportJson_ReturnsRunWithoutAddingHiddenText()
{
    var service = new RagasEvaluationExportService();
    var run = CreateRun(includeFullText: false);

    var result = service.ExportJson(run);

    result.ContentType.Should().Be("application/json; charset=utf-8");
    result.FileName.Should().EndWith(".json");
    result.Content.Should().Contain(run.RunId);
    result.Content.Should().NotContain("secret-key");
}

[Fact]
public void ExportCsv_EscapesValuesAndUsesSafeColumns()
{
    var service = new RagasEvaluationExportService();
    var run = CreateRunWithCaseName("case, \"quoted\"");

    var result = service.ExportCsv(run);

    result.ContentType.Should().Be("text/csv; charset=utf-8");
    result.Content.Should().Contain("run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash");
    result.Content.Should().Contain("\"case, \"\"quoted\"\"\"");
    result.Content.Should().NotContain("AnswerText");
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationExportServiceTests" --no-restore --verbosity minimal
```

Expected: build fails because `RagasEvaluationExportService` does not exist.

- [ ] **Step 3: Implement export service**

Create:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed record RagasEvaluationExportResult(
    string Content,
    string ContentType,
    string FileName);

internal sealed class RagasEvaluationExportService
{
    public RagasEvaluationExportResult ExportJson(RagasEvaluationRunRecord run)
    {
        var payload = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            format = "json",
            run
        };
        var content = JsonSerializer.Serialize(payload, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);

        return new RagasEvaluationExportResult(
            content,
            "application/json; charset=utf-8",
            $"{run.RunId}.json");
    }

    public RagasEvaluationExportResult ExportCsv(RagasEvaluationRunRecord run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash");

        foreach (var item in run.Cases)
        {
            builder.AppendJoin(
                ',',
                Csv(run.RunId),
                Csv(item.CaseName),
                Csv(item.Status),
                Csv(Number(item.Metrics.Faithfulness)),
                Csv(Number(item.Metrics.AnswerRelevance)),
                Csv(Number(item.Metrics.ContextRecall)),
                Csv(Number(item.Metrics.ContextPrecision)),
                Csv(Number(item.Metrics.RagasScore)),
                Csv(item.Contexts.Count.ToString(CultureInfo.InvariantCulture)),
                Csv(item.AnswerHash));
            builder.AppendLine();
        }

        return new RagasEvaluationExportResult(
            builder.ToString(),
            "text/csv; charset=utf-8",
            $"{run.RunId}.csv");
    }

    private static string Number(double? value) =>
        value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static string Csv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(',', StringComparison.Ordinal)
            || escaped.Contains('"', StringComparison.Ordinal)
            || escaped.Contains('\n', StringComparison.Ordinal)
            || escaped.Contains('\r', StringComparison.Ordinal)
            ? $"\"{escaped}\""
            : escaped;
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run the export service tests.

Expected: all export service tests pass.

## Task 5: Add Comparison Service

**Files:**

- Create: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationComparisonService.cs`
- Create: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationComparisonServiceTests.cs`

- [ ] **Step 1: Write comparison tests**

Create tests:

```csharp
[Fact]
public void Compare_WhenCurrentScoreHigher_ReportsImproved()
{
    var service = new RagasEvaluationComparisonService();
    var baseline = CreateRun("baseline", ragasScore: 0.8);
    var current = CreateRun("current", ragasScore: 0.85);

    var result = service.Compare(current, baseline);

    result.Metrics["ragasScore"].Direction.Should().Be("Improved");
    result.Metrics["ragasScore"].Delta.Should().BeApproximately(0.05, 0.0001);
}

[Fact]
public void Compare_WhenCaseSetsDiffer_AddsDiagnostic()
{
    var service = new RagasEvaluationComparisonService();
    var baseline = CreateRun("baseline", ["case-a"], ragasScore: 0.8);
    var current = CreateRun("current", ["case-b"], ragasScore: 0.8);

    var result = service.Compare(current, baseline);

    result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "case_set_differs");
    result.CaseCounts.MatchedCases.Should().Be(0);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationComparisonServiceTests" --no-restore --verbosity minimal
```

Expected: build fails because `RagasEvaluationComparisonService` does not exist.

- [ ] **Step 3: Implement comparison service**

Create:

```csharp
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationComparisonService
{
    private const double Epsilon = 0.0001;

    public RagasEvaluationComparisonResponse Compare(
        RagasEvaluationRunRecord current,
        RagasEvaluationRunRecord baseline)
    {
        var response = new RagasEvaluationComparisonResponse
        {
            RunId = current.RunId,
            BaselineRunId = baseline.RunId,
            Metrics =
            {
                ["ragasScore"] = CompareMetric(current.Summary.AverageMetrics.RagasScore, baseline.Summary.AverageMetrics.RagasScore),
                ["faithfulness"] = CompareMetric(current.Summary.AverageMetrics.Faithfulness, baseline.Summary.AverageMetrics.Faithfulness),
                ["answerRelevance"] = CompareMetric(current.Summary.AverageMetrics.AnswerRelevance, baseline.Summary.AverageMetrics.AnswerRelevance),
                ["contextRecall"] = CompareMetric(current.Summary.AverageMetrics.ContextRecall, baseline.Summary.AverageMetrics.ContextRecall),
                ["contextPrecision"] = CompareMetric(current.Summary.AverageMetrics.ContextPrecision, baseline.Summary.AverageMetrics.ContextPrecision)
            },
            CaseCounts = CountCases(current, baseline)
        };

        if (response.CaseCounts.MatchedCases != response.CaseCounts.BaselineTotal
            || response.CaseCounts.MatchedCases != response.CaseCounts.CurrentTotal)
        {
            response.Diagnostics.Add(new RagasEvaluationDiagnosticDto
            {
                Code = "case_set_differs",
                Message = "Current and baseline runs do not contain the same case set."
            });
        }

        return response;
    }

    private static RagasEvaluationMetricComparisonDto CompareMetric(double? current, double? baseline)
    {
        if (!current.HasValue || !baseline.HasValue)
        {
            return new RagasEvaluationMetricComparisonDto
            {
                Baseline = baseline,
                Current = current,
                Direction = "NotMeasured"
            };
        }

        var delta = current.Value - baseline.Value;
        var direction = delta switch
        {
            > Epsilon => "Improved",
            < -Epsilon => "Regressed",
            _ => "Unchanged"
        };

        return new RagasEvaluationMetricComparisonDto
        {
            Baseline = baseline,
            Current = current,
            Delta = delta,
            Direction = direction
        };
    }

    private static RagasEvaluationCaseCountComparisonDto CountCases(
        RagasEvaluationRunRecord current,
        RagasEvaluationRunRecord baseline)
    {
        var currentCases = current.Cases.Select(item => item.CaseName).ToHashSet(StringComparer.Ordinal);
        var baselineCases = baseline.Cases.Select(item => item.CaseName).ToHashSet(StringComparer.Ordinal);

        return new RagasEvaluationCaseCountComparisonDto
        {
            BaselineTotal = baselineCases.Count,
            CurrentTotal = currentCases.Count,
            MatchedCases = currentCases.Intersect(baselineCases, StringComparer.Ordinal).Count()
        };
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run the comparison service tests.

Expected: all comparison service tests pass.

## Task 6: Add Coordinator Operations

**Files:**

- Modify: `src/LightRAGNet.Server/Services/Evaluation/RagasEvaluationRunCoordinator.cs`
- Modify: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationRunCoordinatorTests.cs`

- [ ] **Step 1: Add coordinator tests**

Add tests covering:

```csharp
[Fact]
public async Task ListAsync_ReturnsLightweightRuns()
{
    var fixture = CreateCoordinatorFixture();
    await fixture.Store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

    var result = await fixture.Coordinator.ListAsync(CancellationToken.None);

    result.Success.Should().BeTrue();
    result.Value!.Runs.Should().ContainSingle();
    result.Value.Runs[0].RunId.Should().Be("ragas-a");
}

[Fact]
public async Task ExportAsync_UnknownRun_ReturnsNotFound()
{
    var fixture = CreateCoordinatorFixture();

    var result = await fixture.Coordinator.ExportAsync("missing", "json", CancellationToken.None);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be("run_not_found");
    result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
}

[Fact]
public async Task CompareAsync_SameRun_ReturnsBadRequest()
{
    var fixture = CreateCoordinatorFixture();
    await fixture.Store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

    var result = await fixture.Coordinator.CompareAsync("ragas-a", "ragas-a", CancellationToken.None);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be("same_run_compare");
    result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationRunCoordinatorTests" --no-restore --verbosity minimal
```

Expected: build fails because the new coordinator methods do not exist.

- [ ] **Step 3: Inject new services**

Add fields and constructor parameters:

```csharp
private readonly RagasEvaluationExportService exportService;
private readonly RagasEvaluationComparisonService comparisonService;
```

- [ ] **Step 4: Implement list/export/compare**

Add methods:

```csharp
internal async Task<RagasEvaluationOperationResult<RagasEvaluationRunListResponse>> ListAsync(
    CancellationToken cancellationToken)
{
    var runs = await store.ListAsync(cancellationToken);
    return RagasEvaluationOperationResult<RagasEvaluationRunListResponse>.Ok(new RagasEvaluationRunListResponse
    {
        Runs = runs.Select(ToSummaryItem).ToList()
    });
}

internal async Task<RagasEvaluationOperationResult<RagasEvaluationExportResult>> ExportAsync(
    string runId,
    string? format,
    CancellationToken cancellationToken)
{
    var run = await store.GetAsync(runId, cancellationToken);
    if (run is null)
    {
        return RagasEvaluationOperationResult<RagasEvaluationExportResult>.Fail(
            "run_not_found",
            $"RAGAS evaluation run '{runId}' was not found.",
            StatusCodes.Status404NotFound);
    }

    return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
        ? RagasEvaluationOperationResult<RagasEvaluationExportResult>.Ok(exportService.ExportCsv(run))
        : string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(format)
            ? RagasEvaluationOperationResult<RagasEvaluationExportResult>.Ok(exportService.ExportJson(run))
            : RagasEvaluationOperationResult<RagasEvaluationExportResult>.Fail(
                "unsupported_export_format",
                "Supported RAGAS evaluation export formats are json and csv.",
                StatusCodes.Status400BadRequest);
}

internal async Task<RagasEvaluationOperationResult<RagasEvaluationComparisonResponse>> CompareAsync(
    string runId,
    string baselineRunId,
    CancellationToken cancellationToken)
{
    if (string.Equals(runId, baselineRunId, StringComparison.Ordinal))
    {
        return RagasEvaluationOperationResult<RagasEvaluationComparisonResponse>.Fail(
            "same_run_compare",
            "A RAGAS evaluation run cannot be compared with itself.",
            StatusCodes.Status400BadRequest);
    }

    var current = await store.GetAsync(runId, cancellationToken);
    var baseline = await store.GetAsync(baselineRunId, cancellationToken);
    if (current is null || baseline is null)
    {
        return RagasEvaluationOperationResult<RagasEvaluationComparisonResponse>.Fail(
            "run_not_found",
            "The current or baseline RAGAS evaluation run was not found.",
            StatusCodes.Status404NotFound);
    }

    return RagasEvaluationOperationResult<RagasEvaluationComparisonResponse>.Ok(
        comparisonService.Compare(current, baseline));
}

private static RagasEvaluationRunSummaryItemDto ToSummaryItem(RagasEvaluationRunRecord run) =>
    new()
    {
        RunId = run.RunId,
        Status = run.Status.ToString(),
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Total = run.Summary.Total,
        Succeeded = run.Summary.Succeeded,
        Failed = run.Summary.Failed,
        Cancelled = run.Summary.Cancelled,
        RagasScore = run.Summary.AverageMetrics.RagasScore,
        DurationSeconds = run.Summary.ElapsedTimeSeconds
    };
```

- [ ] **Step 5: Verify GREEN**

Run coordinator tests again.

Expected: coordinator tests pass.

## Task 7: Add Controller Routes

**Files:**

- Modify: `src/LightRAGNet.Server/Controllers/RagasEvaluationController.cs`
- Modify: `tests/LightRAGNet.Server.Tests/Evaluation/RagasEvaluationControllerTests.cs`

- [ ] **Step 1: Add controller tests**

Add tests for:

- `GET /api/evaluation/ragas/runs` requires token,
- valid token returns list response,
- `GET /api/evaluation/ragas/runs/{runId}/export?format=json` returns JSON content,
- `GET /api/evaluation/ragas/runs/{runId}/export?format=csv` returns CSV content,
- unsupported export format returns `400`,
- compare endpoint returns comparison response.

Use existing controller test factory patterns and fake services.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationControllerTests" --no-restore --verbosity minimal
```

Expected: new route tests return `404` or fail compilation until controller methods are added.

- [ ] **Step 3: Add controller actions**

Add to `RagasEvaluationController`:

```csharp
[HttpGet]
public async Task<ActionResult<RagasEvaluationRunListResponse>> ListAsync(
    CancellationToken cancellationToken)
{
    if (ValidateRequestAccess() is { } failure)
    {
        return failure;
    }

    var result = await coordinator.ListAsync(cancellationToken);

    return ToActionResult(result);
}

[HttpGet("{runId}/export")]
public async Task<IActionResult> ExportAsync(
    string runId,
    [FromQuery] string? format,
    CancellationToken cancellationToken)
{
    if (ValidateRequestAccess() is { } failure)
    {
        return failure;
    }

    var result = await coordinator.ExportAsync(runId, format, cancellationToken);
    if (!result.Success)
    {
        return StatusCode(result.StatusCode, new
        {
            code = result.ErrorCode,
            message = result.ErrorMessage
        });
    }

    var export = result.Value!;
    return File(Encoding.UTF8.GetBytes(export.Content), export.ContentType, export.FileName);
}

[HttpGet("{runId}/compare/{baselineRunId}")]
public async Task<ActionResult<RagasEvaluationComparisonResponse>> CompareAsync(
    string runId,
    string baselineRunId,
    CancellationToken cancellationToken)
{
    if (ValidateRequestAccess() is { } failure)
    {
        return failure;
    }

    var result = await coordinator.CompareAsync(runId, baselineRunId, cancellationToken);

    return ToActionResult(result);
}
```

`RagasEvaluationController.cs` already imports `System.Text`, so no new import is needed for `Encoding`.

- [ ] **Step 4: Verify GREEN**

Run controller tests.

Expected: all RAGAS controller tests pass.

## Task 8: Register Services

**Files:**

- Modify: `src/LightRAGNet.Server/Program.cs`

- [ ] **Step 1: Add registrations**

Add:

```csharp
builder.Services.AddSingleton<RagasEvaluationExportService>();
builder.Services.AddSingleton<RagasEvaluationComparisonService>();
```

Pass both services into the `RagasEvaluationRunCoordinator` factory:

```csharp
sp.GetRequiredService<RagasEvaluationExportService>(),
sp.GetRequiredService<RagasEvaluationComparisonService>(),
```

- [ ] **Step 2: Verify DI**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationControllerTests" --no-restore --verbosity minimal
```

Expected: no DI activation failure.

## Task 9: Add Power Workflow Documentation

**Files:**

- Create: `docs/evaluation/ragas-power-workflow.md`

- [ ] **Step 1: Add workflow document**

Create:

````markdown
# RAGAS Evaluation Power Workflow

This workflow is an opt-in development and operations path for collecting RAGAS-compatible evaluation evidence from the current LightRAGNet workspace.

## Safety

Real evaluator smoke calls external model APIs and may cost money. Keep tokens and API keys in local user secrets, environment variables, or untracked configuration.

## Local Configuration

```json
{
  "Evaluation": {
    "Ragas": {
      "Enabled": true,
      "AdminToken": "<local secret>",
      "EvaluatorModel": "deepseek-v4-flash",
      "ApiKey": "<local secret or DEEPSEEK_API_KEY>",
      "BaseUrl": "https://api.deepseek.com",
      "MaxCasesPerRun": 5,
      "AllowPersistFullText": false
    }
  }
}
```

## Smoke Run

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

## Inspect and Export

```http
GET /api/evaluation/ragas/runs
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}/export?format=json
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}/export?format=csv
X-Evaluation-Token: <local secret>
```

## Compare Against a Baseline

```http
GET /api/evaluation/ragas/runs/{runId}/compare/{baselineRunId}
X-Evaluation-Token: <local secret>
```

Use an explicit baseline run id from a trusted prior run. Do not treat the latest successful run as a baseline unless it was intentionally selected.

## Automated Test Boundary

Default automated tests use fake query/evaluator services. They must not require real evaluator keys, Qdrant, Neo4j, or paid model calls.
````

- [ ] **Step 2: Verify markdown exists**

Run:

```powershell
Test-Path .\docs\evaluation\ragas-power-workflow.md
```

Expected: `True`.

## Task 10: End-to-End Verification

**Files:**

- Modify: `docs/superpowers/archives/2026-05/2026-05-28-ragas-evaluation-power-workflow-archives.md` only after implementation is accepted and verified.
- Modify: `docs/superpowers/archives/INDEX.md` only after the archive is written.

- [ ] **Step 1: Run focused RAGAS tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal
```

Expected: all RAGAS evaluation tests pass.

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

- [ ] **Step 4: Archive completed requirement**

After implementation is complete and verified, create:

```text
docs/superpowers/archives/2026-05/2026-05-28-ragas-evaluation-power-workflow-archives.md
```

Include:

- delivered endpoints,
- privacy boundary,
- verification commands and results,
- note that real evaluator smoke remains opt-in unless actually run with local secrets,
- related spec and plan links.

Update:

```text
docs/superpowers/archives/INDEX.md
```

Add the archive entry under `2026-05`.

## Plan Self-Review

- Spec coverage:
  - Run listing: Tasks 2, 6, 7.
  - Export JSON/CSV: Tasks 4, 6, 7.
  - Benchmark statistics: Task 3.
  - Baseline comparison: Tasks 5, 6, 7.
  - Real evaluator smoke workflow documentation: Task 9.
  - Verification and archive boundary: Task 10.
- Placeholder scan:
  - No placeholder markers remain.
  - Every new service has explicit test and implementation snippets.
- Type consistency:
  - DTO names match the shared contract section.
  - Coordinator method names match controller usage.
  - Service names match DI registration.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-28-ragas-evaluation-power-workflow-implementation-plan.md`.

Recommended execution path: implement Tasks 1-5 first to lock contracts and pure services, then Tasks 6-8 for API wiring, then Task 9 docs, then Task 10 verification and archive.
