# Server Operational Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an evidence-driven System Status API and React island page that show LightRAGNet server, storage, provider configuration, queue, and conversion health without inventing UI data.

**Architecture:** Add a plugin-style `ISystemHealthCheck` layer under `LightRAGNet.Server`, aggregate checks with timeout and redaction in `SystemHealthService`, expose `GET /api/system/health`, and render the result from a React island mounted by a thin Blazor host page. React displays backend DTOs only; it does not recompute overall status, fix-first ordering, or feature impact.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core SQLite, Qdrant.Client, Neo4j.Driver, xUnit/FluentAssertions, React 19, TypeScript, Vite, Vitest.

---

## Reference Documents

- Spec: `docs/superpowers/specs/2026-05-24-server-operational-readiness-design.md`
- Visual prototype: `docs/superpowers/visuals/system-status-ui-concepts.html`
- Existing React island pattern: `src/LightRAGNet.Web/Components/Pages/GraphView.razor`
- Existing Vite entry config: `src/LightRAGNet.Web/ClientApp/vite.config.ts`
- Existing Web source tests: `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`
- Existing Server test factory: `tests/LightRAGNet.Server.Tests/LightRagServerFactory.cs`

## File Structure

Create:

- `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthModels.cs`  
  DTOs, status enum, evidence helpers, and constants used by API and checks.
- `src/LightRAGNet.Server/Services/SystemHealth/ISystemHealthCheck.cs`  
  Plugin interface for individual checks.
- `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthService.cs`  
  Concurrent check orchestration, per-check timeout, exception capture, summary, fix-first, feature impact, and redaction.
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/ServerApiHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/SqliteHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/WorkingDirHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/QdrantHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/Neo4jHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/LlmConfigHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/EmbeddingConfigHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/RerankConfigHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/RagTaskQueueHealthCheck.cs`
- `src/LightRAGNet.Server/Services/SystemHealth/Checks/DocumentConversionQueueHealthCheck.cs`
- `src/LightRAGNet.Server/Controllers/SystemHealthController.cs`
- `tests/LightRAGNet.Server.Tests/SystemHealthServiceTests.cs`
- `tests/LightRAGNet.Server.Tests/SystemHealthCheckTests.cs`
- `tests/LightRAGNet.Server.Tests/SystemHealthControllerTests.cs`
- `src/LightRAGNet.Web/Components/Pages/SystemStatus.razor`
- `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.ts`
- `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.test.ts`
- `src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusWorkbench.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusSummary.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusChecks.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusFixFirst.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusFeatureImpact.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/SystemStatusEvidence.tsx`
- `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`

Modify:

- `src/LightRAGNet.Server/Program.cs`  
  Register system health checks and service.
- `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`  
  Add `System Status` nav entry.
- `src/LightRAGNet.Web/ClientApp/vite.config.ts`  
  Add `systemStatus` Vite entry and deterministic output asset names.
- `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs` or new `SystemStatusHostSourceTests.cs`  
  Verify React island host and build artifacts.

---

### Task 1: Backend Health DTOs and Aggregator

**Files:**
- Create: `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthModels.cs`
- Create: `src/LightRAGNet.Server/Services/SystemHealth/ISystemHealthCheck.cs`
- Create: `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthService.cs`
- Test: `tests/LightRAGNet.Server.Tests/SystemHealthServiceTests.cs`

- [ ] **Step 1: Write failing aggregation tests**

Create `tests/LightRAGNet.Server.Tests/SystemHealthServiceTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class SystemHealthServiceTests
{
    [Fact]
    public async Task GetHealthAsync_WhenAnyCheckIsUnhealthy_ReturnsUnhealthyAndSortedFixFirst()
    {
        var service = CreateService(
            ResultCheck.Unhealthy("qdrant", "Qdrant", "Storage", "Qdrant failed", "Start Qdrant."),
            ResultCheck.Degraded("neo4j", "Neo4j", "Storage", "Neo4j failed", "Start Neo4j."),
            ResultCheck.Healthy("sqlite", "SQLite", "Storage"));

        var result = await service.GetHealthAsync();

        result.Status.Should().Be(SystemHealthStatus.Unhealthy);
        result.Summary.Unhealthy.Should().Be(1);
        result.Summary.Degraded.Should().Be(1);
        result.Summary.Healthy.Should().Be(1);
        result.FixFirst.Select(x => x.CheckId).Should().Equal("qdrant", "neo4j");
    }

    [Fact]
    public async Task GetHealthAsync_WhenOnlyDegradedExists_ReturnsDegraded()
    {
        var service = CreateService(
            ResultCheck.Healthy("sqlite", "SQLite", "Storage"),
            ResultCheck.Degraded("rerank-config", "Rerank config", "Providers", "Rerank key missing", "Configure Rerank:ApiKey."));

        var result = await service.GetHealthAsync();

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Summary.Degraded.Should().Be(1);
        result.FixFirst.Should().ContainSingle().Which.CheckId.Should().Be("rerank-config");
    }

    [Fact]
    public async Task GetHealthAsync_WhenAllMeasuredChecksAreHealthy_ReturnsHealthy()
    {
        var service = CreateService(
            ResultCheck.Healthy("server-api", "Server API", "Server"),
            ResultCheck.Healthy("sqlite", "SQLite", "Storage"));

        var result = await service.GetHealthAsync();

        result.Status.Should().Be(SystemHealthStatus.Healthy);
        result.Summary.Healthy.Should().Be(2);
        result.FixFirst.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthAsync_WhenAllChecksAreNotMeasured_ReturnsNotMeasured()
    {
        var service = CreateService(
            ResultCheck.NotMeasured("optional", "Optional", "Other", "No evidence available."));

        var result = await service.GetHealthAsync();

        result.Status.Should().Be(SystemHealthStatus.NotMeasured);
        result.Summary.NotMeasured.Should().Be(1);
    }

    [Fact]
    public async Task GetHealthAsync_WhenCheckThrows_CapturesFailureWithoutThrowing()
    {
        var service = CreateService(new ThrowingCheck("qdrant", "Qdrant", "Storage", SystemHealthStatus.Unhealthy));

        var result = await service.GetHealthAsync();

        result.Status.Should().Be(SystemHealthStatus.Unhealthy);
        result.Checks.Should().ContainSingle().Which.Evidence.Should().ContainKey("errorType");
    }

    [Fact]
    public async Task GetHealthAsync_RedactsSensitiveEvidenceKeys()
    {
        var service = CreateService(new SensitiveEvidenceCheck());

        var result = await service.GetHealthAsync();

        var evidence = result.Checks.Single().Evidence;
        evidence["apiKey"].Should().Be("<redacted>");
        evidence["password"].Should().Be("<redacted>");
        evidence["token"].Should().Be("<redacted>");
        evidence["uri"].Should().Be("neo4j://localhost:7687");
    }

    [Fact]
    public async Task GetHealthAsync_GeneratesFeatureImpactsFromAffectedChecks()
    {
        var service = CreateService(ResultCheck.Degraded(
            "neo4j",
            "Neo4j",
            "Storage",
            "Neo4j is not reachable.",
            "Start Neo4j.",
            ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"]));

        var result = await service.GetHealthAsync();

        result.FeatureImpacts.Should().Contain(x =>
            x.Feature == "KG Query Modes" &&
            x.Status == SystemHealthStatus.Degraded &&
            x.AffectedBy.Contains("neo4j"));
    }

    private static SystemHealthService CreateService(params ISystemHealthCheck[] checks)
    {
        return new SystemHealthService(
            checks,
            Options.Create(new SystemHealthOptions { PerCheckTimeout = TimeSpan.FromSeconds(1) }),
            NullLogger<SystemHealthService>.Instance);
    }

    private sealed class ResultCheck(SystemHealthCheckResult result) : ISystemHealthCheck
    {
        public string Id => result.Id;
        public string Name => result.Name;
        public string Category => result.Category;

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        public static ResultCheck Healthy(string id, string name, string category)
        {
            return new ResultCheck(SystemHealthCheckResult.Healthy(id, name, category, "OK"));
        }

        public static ResultCheck Degraded(
            string id,
            string name,
            string category,
            string message,
            string remediation,
            IReadOnlyList<string>? affects = null)
        {
            return new ResultCheck(SystemHealthCheckResult.Degraded(
                id,
                name,
                category,
                message,
                new Dictionary<string, object?>(),
                remediation,
                affects ?? []));
        }

        public static ResultCheck Unhealthy(string id, string name, string category, string message, string remediation)
        {
            return new ResultCheck(SystemHealthCheckResult.Unhealthy(
                id,
                name,
                category,
                message,
                new Dictionary<string, object?>(),
                remediation,
                []));
        }

        public static ResultCheck NotMeasured(string id, string name, string category, string message)
        {
            return new ResultCheck(SystemHealthCheckResult.NotMeasured(id, name, category, message));
        }
    }

    private sealed class ThrowingCheck(
        string id,
        string name,
        string category,
        SystemHealthStatus failureStatus) : ISystemHealthCheck
    {
        public string Id => id;
        public string Name => name;
        public string Category => category;

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("probe failed");
        }
    }

    private sealed class SensitiveEvidenceCheck : ISystemHealthCheck
    {
        public string Id => "sensitive";
        public string Name => "Sensitive";
        public string Category => "Security";

        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SystemHealthCheckResult.Healthy(
                Id,
                Name,
                Category,
                "configured",
                new Dictionary<string, object?>
                {
                    ["apiKey"] = "secret",
                    ["password"] = "secret",
                    ["token"] = "secret",
                    ["uri"] = "neo4j://localhost:7687"
                }));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthServiceTests" --no-restore --verbosity minimal
```

Expected: FAIL because `LightRAGNet.Server.Services.SystemHealth` types do not exist.

- [ ] **Step 3: Add models and interface**

Create `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthModels.cs`:

```csharp
namespace LightRAGNet.Server.Services.SystemHealth;

public enum SystemHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    NotMeasured
}

public sealed class SystemHealthOptions
{
    public TimeSpan PerCheckTimeout { get; set; } = TimeSpan.FromMilliseconds(1500);
}

public sealed record SystemHealthResponse(
    SystemHealthStatus Status,
    DateTimeOffset GeneratedAt,
    long DurationMs,
    SystemHealthSummary Summary,
    IReadOnlyList<SystemHealthCheckResult> Checks,
    IReadOnlyList<SystemHealthFixFirstItem> FixFirst,
    IReadOnlyList<SystemHealthFeatureImpact> FeatureImpacts);

public sealed record SystemHealthSummary(
    int Healthy,
    int Degraded,
    int Unhealthy,
    int NotMeasured);

public sealed record SystemHealthFixFirstItem(
    string CheckId,
    string Title,
    SystemHealthStatus Status,
    string Remediation,
    IReadOnlyList<string> Affects);

public sealed record SystemHealthFeatureImpact(
    string Feature,
    SystemHealthStatus Status,
    string Reason,
    IReadOnlyList<string> AffectedBy,
    IReadOnlyList<SystemHealthLink> Links);

public sealed record SystemHealthLink(string Label, string Href);

public sealed record SystemHealthCheckResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required SystemHealthStatus Status { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, object?> Evidence { get; init; } = new Dictionary<string, object?>();
    public string Remediation { get; init; } = string.Empty;
    public IReadOnlyList<string> Affects { get; init; } = [];
    public long DurationMs { get; init; }

    public static SystemHealthCheckResult Healthy(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?>? evidence = null,
        IReadOnlyList<string>? affects = null)
    {
        return Create(id, name, category, SystemHealthStatus.Healthy, message, evidence, string.Empty, affects);
    }

    public static SystemHealthCheckResult Degraded(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?> evidence,
        string remediation,
        IReadOnlyList<string> affects)
    {
        return Create(id, name, category, SystemHealthStatus.Degraded, message, evidence, remediation, affects);
    }

    public static SystemHealthCheckResult Unhealthy(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?> evidence,
        string remediation,
        IReadOnlyList<string> affects)
    {
        return Create(id, name, category, SystemHealthStatus.Unhealthy, message, evidence, remediation, affects);
    }

    public static SystemHealthCheckResult NotMeasured(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?>? evidence = null)
    {
        return Create(id, name, category, SystemHealthStatus.NotMeasured, message, evidence, string.Empty, []);
    }

    public SystemHealthCheckResult WithDuration(long durationMs)
    {
        return this with { DurationMs = durationMs };
    }

    public SystemHealthCheckResult WithEvidence(IReadOnlyDictionary<string, object?> evidence)
    {
        return this with { Evidence = evidence };
    }

    private static SystemHealthCheckResult Create(
        string id,
        string name,
        string category,
        SystemHealthStatus status,
        string message,
        IReadOnlyDictionary<string, object?>? evidence,
        string remediation,
        IReadOnlyList<string>? affects)
    {
        return new SystemHealthCheckResult
        {
            Id = id,
            Name = name,
            Category = category,
            Status = status,
            Message = message,
            Evidence = evidence ?? new Dictionary<string, object?>(),
            Remediation = remediation,
            Affects = affects ?? []
        };
    }
}
```

Create `src/LightRAGNet.Server/Services/SystemHealth/ISystemHealthCheck.cs`:

```csharp
namespace LightRAGNet.Server.Services.SystemHealth;

public interface ISystemHealthCheck
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add `SystemHealthService` implementation**

Create `src/LightRAGNet.Server/Services/SystemHealth/SystemHealthService.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth;

public sealed class SystemHealthService(
    IEnumerable<ISystemHealthCheck> checks,
    IOptions<SystemHealthOptions> options,
    ILogger<SystemHealthService> logger)
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apikey",
        "apiKey",
        "password",
        "token",
        "authorization",
        "authorizationHeader",
        "connectionString"
    };

    private readonly IReadOnlyList<ISystemHealthCheck> checks = checks.ToList();
    private readonly TimeSpan perCheckTimeout = options.Value.PerCheckTimeout <= TimeSpan.Zero
        ? TimeSpan.FromMilliseconds(1500)
        : options.Value.PerCheckTimeout;

    public async Task<SystemHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var generatedAt = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(checks.Select(check => RunCheckAsync(check, cancellationToken)));
        var orderedResults = results
            .OrderBy(result => GetCheckOrder(result.Id))
            .ToList();

        var summary = new SystemHealthSummary(
            orderedResults.Count(x => x.Status == SystemHealthStatus.Healthy),
            orderedResults.Count(x => x.Status == SystemHealthStatus.Degraded),
            orderedResults.Count(x => x.Status == SystemHealthStatus.Unhealthy),
            orderedResults.Count(x => x.Status == SystemHealthStatus.NotMeasured));

        var status = ResolveOverallStatus(summary);
        var fixFirst = BuildFixFirst(orderedResults);
        var featureImpacts = BuildFeatureImpacts(orderedResults);

        started.Stop();
        return new SystemHealthResponse(
            status,
            generatedAt,
            started.ElapsedMilliseconds,
            summary,
            orderedResults,
            fixFirst,
            featureImpacts);
    }

    private async Task<SystemHealthCheckResult> RunCheckAsync(
        ISystemHealthCheck check,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(perCheckTimeout);

        try
        {
            var result = await check.CheckAsync(timeout.Token);
            stopwatch.Stop();
            return result
                .WithDuration(stopwatch.ElapsedMilliseconds)
                .WithEvidence(RedactEvidence(result.Evidence));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return CreateProbeFailure(
                check,
                $"{check.Name} check timed out after {perCheckTimeout.TotalMilliseconds:0} ms.",
                "Timeout",
                stopwatch.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["timeoutMs"] = (int)perCheckTimeout.TotalMilliseconds });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "System health check {CheckId} failed.", check.Id);
            return CreateProbeFailure(
                check,
                $"{check.Name} check failed: {ex.GetType().Name}.",
                ex.GetType().Name,
                stopwatch.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["errorMessage"] = ex.Message });
        }
    }

    private static SystemHealthCheckResult CreateProbeFailure(
        ISystemHealthCheck check,
        string message,
        string errorType,
        long durationMs,
        Dictionary<string, object?> evidence)
    {
        evidence["errorType"] = errorType;
        evidence = RedactEvidence(evidence).ToDictionary();

        var status = check.Id switch
        {
            "neo4j" or "rerank-config" or "rag-task-queue" or "document-conversion-queue" => SystemHealthStatus.Degraded,
            _ => SystemHealthStatus.Unhealthy
        };

        var remediation = check.Id switch
        {
            "neo4j" => "Start Neo4j or update Neo4j:Uri.",
            "rerank-config" => "Configure Rerank:ApiKey or intentionally leave rerank disabled.",
            "rag-task-queue" => "Inspect tasks.json and restart the Server worker if tasks are stuck.",
            "document-conversion-queue" => "Inspect conversion queue rows and restart the Server conversion worker if needed.",
            "qdrant" => "Start Qdrant or update Qdrant host/port configuration.",
            "working-dir" => "Ensure LightRAG:WorkingDir exists and is writable by the Server process.",
            "sqlite" => "Check the SQLite connection string and database file permissions.",
            "embedding-config" => "Configure Embedding:ApiKey or DASHSCOPE_API_KEY.",
            "llm-config" => "Configure LLM:ApiKey or DEEPSEEK_API_KEY.",
            _ => "Inspect server logs for the failed health check."
        };

        var affects = check.Id switch
        {
            "neo4j" => ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"],
            "qdrant" => ["DocumentIndexing", "Naive", "Mix", "VectorRecall"],
            "working-dir" => ["KVStore", "Artifacts", "ConvertedMarkdown", "RagStorage"],
            "sqlite" => ["Documents", "Uploads", "ConversionStatus", "WebManagement"],
            "embedding-config" => ["DocumentIndexing", "VectorRecall"],
            "llm-config" => ["QueryGeneration", "EntityExtraction", "SummaryGeneration"],
            "rerank-config" => ["RerankQuality"],
            "rag-task-queue" => ["DocumentIndexing"],
            "document-conversion-queue" => ["PdfDocxConversion"],
            _ => []
        };

        return new SystemHealthCheckResult
        {
            Id = check.Id,
            Name = check.Name,
            Category = check.Category,
            Status = status,
            Message = message,
            Evidence = evidence,
            Remediation = remediation,
            Affects = affects,
            DurationMs = durationMs
        };
    }

    private static SystemHealthStatus ResolveOverallStatus(SystemHealthSummary summary)
    {
        if (summary.Unhealthy > 0)
        {
            return SystemHealthStatus.Unhealthy;
        }

        if (summary.Degraded > 0)
        {
            return SystemHealthStatus.Degraded;
        }

        if (summary.Healthy > 0)
        {
            return SystemHealthStatus.Healthy;
        }

        return SystemHealthStatus.NotMeasured;
    }

    private static IReadOnlyList<SystemHealthFixFirstItem> BuildFixFirst(IReadOnlyList<SystemHealthCheckResult> results)
    {
        return results
            .Where(result => result.Status is SystemHealthStatus.Unhealthy or SystemHealthStatus.Degraded)
            .OrderBy(result => result.Status == SystemHealthStatus.Unhealthy ? 0 : 1)
            .ThenBy(result => GetCheckOrder(result.Id))
            .Select(result => new SystemHealthFixFirstItem(
                result.Id,
                result.Name,
                result.Status,
                result.Remediation,
                result.Affects))
            .ToList();
    }

    private static IReadOnlyList<SystemHealthFeatureImpact> BuildFeatureImpacts(IReadOnlyList<SystemHealthCheckResult> results)
    {
        var byId = results.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var impacts = new List<SystemHealthFeatureImpact>();

        AddImpactIf(byId, impacts, "sqlite", "Web Management", "/markdown-documents");
        AddImpactIf(byId, impacts, "working-dir", "RAG Storage and Artifacts", "/markdown-documents");
        AddImpactIf(byId, impacts, "qdrant", "Vector Retrieval", "/");
        AddImpactIf(byId, impacts, "neo4j", "KG Query Modes", "/graph-view");
        AddImpactIf(byId, impacts, "llm-config", "LLM Generation", "/");
        AddImpactIf(byId, impacts, "embedding-config", "Document Indexing", "/markdown-documents");
        AddImpactIf(byId, impacts, "rerank-config", "Rerank Quality", "/");
        AddImpactIf(byId, impacts, "rag-task-queue", "Document Indexing Queue", "/markdown-documents");
        AddImpactIf(byId, impacts, "document-conversion-queue", "PDF/DOCX Conversion", "/markdown-documents");

        return impacts;
    }

    private static void AddImpactIf(
        IReadOnlyDictionary<string, SystemHealthCheckResult> byId,
        List<SystemHealthFeatureImpact> impacts,
        string checkId,
        string feature,
        string href)
    {
        if (!byId.TryGetValue(checkId, out var check) ||
            check.Status is SystemHealthStatus.Healthy or SystemHealthStatus.NotMeasured)
        {
            return;
        }

        impacts.Add(new SystemHealthFeatureImpact(
            feature,
            check.Status,
            check.Message,
            [check.Id],
            [new SystemHealthLink("Open", href)]));
    }

    private static IReadOnlyDictionary<string, object?> RedactEvidence(IReadOnlyDictionary<string, object?> evidence)
    {
        var redacted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in evidence)
        {
            redacted[key] = SensitiveKeys.Contains(key) || key.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "<redacted>"
                : value;
        }

        return redacted;
    }

    private static int GetCheckOrder(string id)
    {
        return id switch
        {
            "server-api" => 0,
            "sqlite" => 10,
            "working-dir" => 20,
            "qdrant" => 30,
            "neo4j" => 40,
            "llm-config" => 50,
            "embedding-config" => 60,
            "rerank-config" => 70,
            "rag-task-queue" => 80,
            "document-conversion-queue" => 90,
            _ => 1000
        };
    }
}
```

- [ ] **Step 5: Run aggregation tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthServiceTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/LightRAGNet.Server/Services/SystemHealth tests/LightRAGNet.Server.Tests/SystemHealthServiceTests.cs
git commit -m "feat: add system health aggregation"
```

---

### Task 2: Concrete Health Checks

**Files:**
- Create: all files under `src/LightRAGNet.Server/Services/SystemHealth/Checks/`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Test: `tests/LightRAGNet.Server.Tests/SystemHealthCheckTests.cs`

- [ ] **Step 1: Write failing check tests**

Create `tests/LightRAGNet.Server.Tests/SystemHealthCheckTests.cs` with tests for config and local checks first. Keep Qdrant/Neo4j tests fake by testing exception mapping through `SystemHealthService`; do not touch real local storage.

```csharp
using FluentAssertions;
using LightRAGNet.Embedding;
using LightRAGNet.LLM;
using LightRAGNet.Rerank;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.SystemHealth;
using LightRAGNet.Server.Services.SystemHealth.Checks;
using LightRAGNet.Storage;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class SystemHealthCheckTests
{
    [Fact]
    public async Task LlmConfigHealthCheck_WhenApiKeyConfigured_ReturnsHealthyWithoutExposingKey()
    {
        var check = new LlmConfigHealthCheck(Options.Create(new DeepSeekOptions
        {
            ApiKey = "secret-key",
            ModelName = "deepseek-chat",
            BaseUrl = "https://api.deepseek.com"
        }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Healthy);
        result.Evidence["configured"].Should().Be(true);
        result.Evidence["model"].Should().Be("deepseek-chat");
        result.Evidence.Values.Should().NotContain("secret-key");
    }

    [Fact]
    public async Task LlmConfigHealthCheck_WhenApiKeyMissing_ReturnsUnhealthy()
    {
        var check = new LlmConfigHealthCheck(Options.Create(new DeepSeekOptions { ApiKey = "" }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Unhealthy);
        result.Remediation.Should().Contain("LLM:ApiKey");
    }

    [Fact]
    public async Task EmbeddingConfigHealthCheck_WhenDimensionMissing_ReturnsUnhealthy()
    {
        var check = new EmbeddingConfigHealthCheck(Options.Create(new AliyunEmbeddingOptions
        {
            ApiKey = "test-key",
            Dimension = 0
        }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Unhealthy);
        result.Message.Should().Contain("dimension");
    }

    [Fact]
    public async Task RerankConfigHealthCheck_WhenApiKeyMissing_ReturnsDegraded()
    {
        var check = new RerankConfigHealthCheck(Options.Create(new AliyunRerankOptions { ApiKey = "" }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Affects.Should().Contain("RerankQuality");
    }

    [Fact]
    public async Task WorkingDirHealthCheck_WhenDirectoryWritable_ReturnsHealthy()
    {
        var root = Path.Combine(Path.GetTempPath(), "LightRAGNet.Health", Guid.NewGuid().ToString("N"));
        try
        {
            var check = new WorkingDirHealthCheck(
                Options.Create(new DocumentArtifactStoreOptions { RootPath = root }),
                NullLogger<WorkingDirHealthCheck>.Instance);

            var result = await check.CheckAsync(CancellationToken.None);

            result.Status.Should().Be(SystemHealthStatus.Healthy);
            result.Evidence["writable"].Should().Be(true);
            result.Evidence["path"].Should().Be(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteHealthCheck_WhenDatabaseConnects_ReturnsHealthy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var check = new SqliteHealthCheck(context);
        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Healthy);
        result.Evidence["canConnect"].Should().Be(true);
    }

    [Fact]
    public async Task RagTaskQueueHealthCheck_WhenOldPendingTaskExists_ReturnsDegraded()
    {
        var store = new InMemoryRagTaskStateStore([
            new LightRAGNet.Models.RagTask
            {
                TaskId = "task-old",
                DocumentId = 1,
                Content = "content",
                FilePath = "file.md",
                Status = LightRAGNet.Models.RagTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            }
        ]);

        var check = new RagTaskQueueHealthCheck(store);
        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Evidence["staleActiveTasks"].Should().Be(1);
    }

    [Fact]
    public async Task DocumentConversionQueueHealthCheck_WhenOldProcessingConversionExists_ReturnsDegraded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.MarkdownDocuments.Add(new MarkdownDocument
        {
            FileName = "sample.pdf",
            FileSize = 10,
            ConversionStatus = DocumentConversionStatus.Processing,
            ConversionStartedAt = DateTime.UtcNow.AddHours(-2)
        });
        await context.SaveChangesAsync();

        var check = new DocumentConversionQueueHealthCheck(context);
        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Evidence["staleProcessing"].Should().Be(1);
    }

    private sealed class InMemoryRagTaskStateStore(List<LightRAGNet.Models.RagTask> tasks) : IRagTaskStateStore
    {
        public Task SaveTaskStateAsync(LightRAGNet.Models.RagTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<LightRAGNet.Models.RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(tasks);
        public Task<LightRAGNet.Models.RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(tasks.FirstOrDefault(x => x.TaskId == taskId));
        public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAllTasksAsync(List<LightRAGNet.Models.RagTask> tasks, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthCheckTests" --no-restore --verbosity minimal
```

Expected: FAIL because concrete checks do not exist.

- [ ] **Step 3: Implement local and config checks**

Create the check classes with these exact IDs and categories:

```csharp
namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class ServerApiHealthCheck : ISystemHealthCheck
{
    public string Id => "server-api";
    public string Name => "Server API";
    public string Category => "Server";

    public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "System health endpoint returned successfully.",
            new Dictionary<string, object?> { ["reachable"] = true }));
    }
}
```

For `LlmConfigHealthCheck`, `EmbeddingConfigHealthCheck`, and `RerankConfigHealthCheck`, use `IOptions<DeepSeekOptions>`, `IOptions<AliyunEmbeddingOptions>`, and `IOptions<AliyunRerankOptions>`. Only report `configured`, `source`, `model`, `baseUrl`, and `dimension`; never report key values. Key source can be computed as `"appsettings"` when options key is non-empty, `"environment"` when fallback environment variable is present, otherwise `"missing"`.

For `WorkingDirHealthCheck`, inject `IOptions<DocumentArtifactStoreOptions>` and `ILogger<WorkingDirHealthCheck>`. Ensure the directory exists, write `.health-probe-{Guid}.tmp`, then delete it in a `finally` block.

For `SqliteHealthCheck`, inject `AppDbContext`, call `Database.CanConnectAsync`, then run `MarkdownDocuments.CountAsync`.

For `RagTaskQueueHealthCheck`, inject `IRagTaskStateStore`, call `LoadAllTasksAsync`, count active tasks with `Pending` or `Processing`, failed tasks with `Failed`, and stale active tasks older than 30 minutes.

For `DocumentConversionQueueHealthCheck`, inject `AppDbContext`, count documents by `ConversionStatus`, and mark degraded when `Queued` or `Processing` rows are older than 30 minutes.

- [ ] **Step 4: Implement Qdrant and Neo4j checks**

Create `QdrantHealthCheck` with injected `QdrantClient` and `IOptions<QdrantOptions>`.

```csharp
public sealed class QdrantHealthCheck(QdrantClient client, IOptions<QdrantOptions> options) : ISystemHealthCheck
{
    public string Id => "qdrant";
    public string Name => "Qdrant";
    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken);
        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Qdrant is reachable.",
            new Dictionary<string, object?>
            {
                ["host"] = options.Value.Host,
                ["port"] = options.Value.Port,
                ["embeddingDimension"] = options.Value.EmbeddingDimension,
                ["collectionCount"] = collections.Count
            });
    }
}
```

Create `Neo4jHealthCheck` with injected `IDriver` and `IOptions<Neo4JOptions>`.

```csharp
public sealed class Neo4jHealthCheck(IDriver driver, IOptions<Neo4JOptions> options) : ISystemHealthCheck
{
    public string Id => "neo4j";
    public string Name => "Neo4j";
    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("RETURN 1 AS ok");
        var records = await cursor.ToListAsync(record => record["ok"].As<int>());
        cancellationToken.ThrowIfCancellationRequested();

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Neo4j is reachable.",
            new Dictionary<string, object?>
            {
                ["uri"] = options.Value.Uri,
                ["probe"] = "RETURN 1",
                ["result"] = records.SingleOrDefault()
            },
            ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"]);
    }
}
```

If Neo4j driver APIs require a cancellation-capable method in this package version, adapt by preserving the same behavior and tests. Do not include password in evidence.

- [ ] **Step 5: Register service and checks**

Modify `src/LightRAGNet.Server/Program.cs` after existing app services:

```csharp
builder.Services.Configure<SystemHealthOptions>(builder.Configuration.GetSection("SystemHealth"));
builder.Services.AddScoped<SystemHealthService>();
builder.Services.AddScoped<ISystemHealthCheck, ServerApiHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, SqliteHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, WorkingDirHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, QdrantHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, Neo4jHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, LlmConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, EmbeddingConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, RerankConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, RagTaskQueueHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, DocumentConversionQueueHealthCheck>();
```

Add `using LightRAGNet.Server.Services.SystemHealth;` and `using LightRAGNet.Server.Services.SystemHealth.Checks;`.

- [ ] **Step 6: Run check tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthCheckTests|FullyQualifiedName~SystemHealthServiceTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

```powershell
git add src/LightRAGNet.Server/Services/SystemHealth src/LightRAGNet.Server/Program.cs tests/LightRAGNet.Server.Tests/SystemHealthCheckTests.cs
git commit -m "feat: add system health checks"
```

---

### Task 3: Health API Endpoint

**Files:**
- Create: `src/LightRAGNet.Server/Controllers/SystemHealthController.cs`
- Modify: `tests/LightRAGNet.Server.Tests/LightRagServerFactory.cs`
- Test: `tests/LightRAGNet.Server.Tests/SystemHealthControllerTests.cs`

- [ ] **Step 1: Write failing API tests**

Create `tests/LightRAGNet.Server.Tests/SystemHealthControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

public sealed class SystemHealthControllerTests
{
    [Fact]
    public async Task GetHealth_ReturnsHealthPayload()
    {
        using var factory = CreateFactoryWithChecks(
            SystemHealthCheckResult.Healthy("server-api", "Server API", "Server", "OK"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be(SystemHealthStatus.Healthy);
        body.Checks.Should().ContainSingle().Which.Id.Should().Be("server-api");
        body.GeneratedAt.Should().NotBe(default);
        body.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_DoesNotLeakSecretsFromEvidence()
    {
        using var factory = CreateFactoryWithChecks(SystemHealthCheckResult.Healthy(
            "llm-config",
            "LLM config",
            "Providers",
            "configured",
            new Dictionary<string, object?>
            {
                ["apiKey"] = "secret-key",
                ["password"] = "secret-password",
                ["token"] = "secret-token",
                ["model"] = "deepseek-chat"
            }));
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/system/health");

        json.Should().NotContain("secret-key");
        json.Should().NotContain("secret-password");
        json.Should().NotContain("secret-token");
        json.Should().Contain("<redacted>");
    }

    [Fact]
    public async Task GetHealth_WhenCheckThrows_ReturnsOkWithUnhealthyCheck()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<ISystemHealthCheck>();
            services.AddSingleton<ISystemHealthCheck>(new ThrowingCheck());
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();
        body!.Status.Should().Be(SystemHealthStatus.Unhealthy);
        body.Checks.Should().ContainSingle().Which.Id.Should().Be("qdrant");
    }

    private static LightRagServerFactory CreateFactoryWithChecks(params SystemHealthCheckResult[] results)
    {
        return new LightRagServerFactory(services =>
        {
            services.RemoveAll<ISystemHealthCheck>();
            foreach (var result in results)
            {
                services.AddSingleton<ISystemHealthCheck>(new StaticCheck(result));
            }
        });
    }

    private sealed class StaticCheck(SystemHealthCheckResult result) : ISystemHealthCheck
    {
        public string Id => result.Id;
        public string Name => result.Name;
        public string Category => result.Category;
        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ThrowingCheck : ISystemHealthCheck
    {
        public string Id => "qdrant";
        public string Name => "Qdrant";
        public string Category => "Storage";
        public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("qdrant down");
    }
}
```

- [ ] **Step 2: Run API tests and verify they fail**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthControllerTests" --no-restore --verbosity minimal
```

Expected: FAIL because `/api/system/health` does not exist.

- [ ] **Step 3: Add controller**

Create `src/LightRAGNet.Server/Controllers/SystemHealthController.cs`:

```csharp
using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemHealthController(SystemHealthService healthService) : ControllerBase
{
    [HttpGet("health")]
    public async Task<ActionResult<SystemHealthResponse>> GetHealth(CancellationToken cancellationToken)
    {
        var result = await healthService.GetHealthAsync(cancellationToken);
        return Ok(result);
    }
}
```

- [ ] **Step 4: Run API tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealthControllerTests|FullyQualifiedName~SystemHealthServiceTests|FullyQualifiedName~SystemHealthCheckTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 3**

```powershell
git add src/LightRAGNet.Server/Controllers/SystemHealthController.cs tests/LightRAGNet.Server.Tests/SystemHealthControllerTests.cs
git commit -m "feat: expose system health endpoint"
```

---

### Task 4: React API Contract and Build Entry

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.test.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/vite.config.ts`

- [ ] **Step 1: Write failing TypeScript API tests**

Create `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import { getSystemHealth } from "./systemStatusApi";

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { "content-type": "application/json" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

describe("systemStatusApi", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test("getSystemHealth calls system health endpoint", async () => {
    const payload = {
      status: "Degraded",
      generatedAt: "2026-05-24T14:41:16Z",
      durationMs: 58,
      summary: { healthy: 6, degraded: 2, unhealthy: 0, notMeasured: 0 },
      checks: [],
      fixFirst: [],
      featureImpacts: []
    };
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(payload));

    await expect(getSystemHealth("/api-root/")).resolves.toEqual(payload);

    expect(fetch).toHaveBeenCalledWith(
      "/api-root/api/system/health",
      expect.objectContaining({ method: "GET" })
    );
  });

  test("getSystemHealth throws server error message", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ message: "Server unavailable" }, { status: 503 }));

    await expect(getSystemHealth("")).rejects.toThrow("Server unavailable");
  });
});
```

- [ ] **Step 2: Run Vitest and verify failure**

```powershell
Push-Location src\LightRAGNet.Web\ClientApp
npm test -- --run src/api/systemStatusApi.test.ts
Pop-Location
```

Expected: FAIL because `systemStatusApi.ts` does not exist.

- [ ] **Step 3: Implement API types and client**

Create `src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.ts`:

```ts
export type SystemHealthStatus = "Healthy" | "Degraded" | "Unhealthy" | "NotMeasured";

export type SystemHealthSummary = {
  healthy: number;
  degraded: number;
  unhealthy: number;
  notMeasured: number;
};

export type SystemHealthCheckResult = {
  id: string;
  name: string;
  category: string;
  status: SystemHealthStatus;
  message: string;
  evidence: Record<string, unknown>;
  remediation: string;
  affects: string[];
  durationMs: number;
};

export type SystemHealthFixFirstItem = {
  checkId: string;
  title: string;
  status: SystemHealthStatus;
  remediation: string;
  affects: string[];
};

export type SystemHealthLink = {
  label: string;
  href: string;
};

export type SystemHealthFeatureImpact = {
  feature: string;
  status: SystemHealthStatus;
  reason: string;
  affectedBy: string[];
  links: SystemHealthLink[];
};

export type SystemHealthResponse = {
  status: SystemHealthStatus;
  generatedAt: string;
  durationMs: number;
  summary: SystemHealthSummary;
  checks: SystemHealthCheckResult[];
  fixFirst: SystemHealthFixFirstItem[];
  featureImpacts: SystemHealthFeatureImpact[];
};

type ErrorLikeResponse = {
  message?: string;
  error?: string;
  title?: string;
};

function buildUrl(apiBase: string, path: string): string {
  const trimmedBase = apiBase.replace(/\/+$/, "");
  return `${trimmedBase}${path}`;
}

async function readJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  const body = text.length > 0 ? (JSON.parse(text) as T & ErrorLikeResponse) : undefined;

  if (!response.ok) {
    const message = body?.message ?? body?.error ?? body?.title ?? response.statusText;
    throw new Error(message || `Request failed with status ${response.status}`);
  }

  return body as T;
}

export async function getSystemHealth(apiBase: string): Promise<SystemHealthResponse> {
  const response = await fetch(buildUrl(apiBase, "/api/system/health"), { method: "GET" });
  return readJson<SystemHealthResponse>(response);
}
```

- [ ] **Step 4: Add Vite entry**

Modify `src/LightRAGNet.Web/ClientApp/vite.config.ts`:

```ts
rollupOptions: {
  preserveEntrySignatures: "strict",
  input: {
    graphWorkbench: "src/graph-workbench/main.tsx",
    systemStatus: "src/system-status/main.tsx"
  },
  output: {
    format: "es",
    entryFileNames: (chunkInfo) =>
      chunkInfo.name === "systemStatus" ? "system-status/assets/system-status.js" : "graph-workbench/assets/graph-workbench.js",
    chunkFileNames: "assets/[name].js",
    assetFileNames: (assetInfo) => {
      if (assetInfo.names?.some((name) => name.endsWith("system-status.css"))) {
        return "system-status/assets/system-status.css";
      }
      if (assetInfo.names?.some((name) => name.endsWith("graph-workbench.css"))) {
        return "graph-workbench/assets/graph-workbench.css";
      }
      if (assetInfo.names?.some((name) => name.endsWith(".css"))) {
        return "assets/[name][extname]";
      }
      return "assets/[name][extname]";
    }
  }
}
```

If Vite emits CSS names based on import order, adjust `assetFileNames` after the first build so both graph and system status CSS land at deterministic paths. Do not break existing graph assets.

- [ ] **Step 5: Run TypeScript tests**

```powershell
Push-Location src\LightRAGNet.Web\ClientApp
npm test -- --run src/api/systemStatusApi.test.ts
npm run typecheck
Pop-Location
```

Expected: PASS.

- [ ] **Step 6: Commit Task 4**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.ts src/LightRAGNet.Web/ClientApp/src/api/systemStatusApi.test.ts src/LightRAGNet.Web/ClientApp/vite.config.ts
git commit -m "feat: add system status react API"
```

---

### Task 5: React System Status Page and Blazor Host

**Files:**
- Create: React files under `src/LightRAGNet.Web/ClientApp/src/system-status/`
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`
- Create: `src/LightRAGNet.Web/Components/Pages/SystemStatus.razor`
- Modify: `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`
- Test: `tests/LightRAGNet.Web.Tests/SystemStatusHostSourceTests.cs`

- [ ] **Step 1: Write failing Web source tests**

Create `tests/LightRAGNet.Web.Tests/SystemStatusHostSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class SystemStatusHostSourceTests
{
    [Fact]
    public void SystemStatus_HostsReactSystemStatusWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "SystemStatus.razor");

        source.Should().Contain("@page \"/system-status\"");
        source.Should().Contain("system-status-root");
        source.Should().Contain("data-api-base=\"@ApiBase\"");
        source.Should().Contain("mountSystemStatus");
        source.Should().Contain("unmountSystemStatus");
        source.Should().Contain("./system-status/assets/system-status.js");
        source.Should().Contain("system-status/assets/system-status.css");
        source.Should().NotContain("<script type=\"module\"");
    }

    [Fact]
    public void NavMenu_ContainsSystemStatusEntry()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Layout", "NavMenu.razor");

        source.Should().Contain("Href=\"system-status\"");
        source.Should().Contain("System Status");
        source.Should().Contain("MonitorHeart");
    }

    [Fact]
    public void SystemStatus_BuildArtifactsAreCommitted()
    {
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "system-status", "assets", "system-status.js")
            .Should()
            .BeTrue();
        RepositoryFileExists("src", "LightRAGNet.Web", "wwwroot", "system-status", "assets", "system-status.css")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SystemStatusReact_DoesNotPerformHealthAggregationLocally()
    {
        var source = ReadRepositoryFile(
            "src",
            "LightRAGNet.Web",
            "ClientApp",
            "src",
            "system-status",
            "SystemStatusWorkbench.tsx");

        source.Should().NotContain("fixFirst =");
        source.Should().NotContain("featureImpacts =");
        source.Should().NotContain("overallStatus");
        source.Should().Contain("health.status");
        source.Should().Contain("health.fixFirst");
        source.Should().Contain("health.featureImpacts");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]), System.Text.Encoding.UTF8);
    }

    private static bool RepositoryFileExists(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.Exists(Path.Combine([repositoryRoot, .. relativeParts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }
}
```

- [ ] **Step 2: Run source tests and verify failure**

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~SystemStatusHostSourceTests" --no-restore --verbosity minimal
```

Expected: FAIL because the host page and build artifacts do not exist.

- [ ] **Step 3: Add React mount entry**

Create `src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx`:

```tsx
import React from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";
import { SystemStatusWorkbench } from "./SystemStatusWorkbench";
import "../styles/system-status.css";

const mountedRoots = new Map<string, Root>();

export function mountSystemStatus(elementId: string, apiBase = ""): void {
  const rootElement = document.getElementById(elementId);

  if (!rootElement) {
    return;
  }

  unmountSystemStatus(elementId);

  const root = createRoot(rootElement);
  mountedRoots.set(elementId, root);
  root.render(
    <React.StrictMode>
      <SystemStatusWorkbench apiBase={apiBase} />
    </React.StrictMode>
  );
}

export function unmountSystemStatus(elementId: string): void {
  const root = mountedRoots.get(elementId);

  if (!root) {
    return;
  }

  root.unmount();
  mountedRoots.delete(elementId);
}
```

- [ ] **Step 4: Add React workbench and components**

Create `SystemStatusWorkbench.tsx` using backend DTOs only:

```tsx
import { useCallback, useEffect, useMemo, useState } from "react";
import { getSystemHealth, type SystemHealthResponse } from "../api/systemStatusApi";
import { SystemStatusChecks } from "./SystemStatusChecks";
import { SystemStatusFeatureImpact } from "./SystemStatusFeatureImpact";
import { SystemStatusFixFirst } from "./SystemStatusFixFirst";
import { SystemStatusSummary } from "./SystemStatusSummary";

type SystemStatusWorkbenchProps = {
  apiBase: string;
};

export function SystemStatusWorkbench({ apiBase }: SystemStatusWorkbenchProps) {
  const [health, setHealth] = useState<SystemHealthResponse | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const loadHealth = useCallback(async () => {
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const result = await getSystemHealth(apiBase);
      setHealth(result);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Server API unavailable.");
    } finally {
      setIsLoading(false);
    }
  }, [apiBase]);

  useEffect(() => {
    void loadHealth();
  }, [loadHealth]);

  const rawJson = useMemo(() => JSON.stringify(health, null, 2), [health]);

  async function copyJson() {
    if (!health) {
      return;
    }

    await navigator.clipboard.writeText(rawJson);
  }

  return (
    <main className="system-status" data-api-base={apiBase}>
      <header className="system-status__header">
        <div>
          <h1>System Status</h1>
          <p>Evidence-driven diagnostics for LightRAGNet server, storage, providers, and workers.</p>
        </div>
        <div className="system-status__actions">
          <button type="button" onClick={() => void loadHealth()} disabled={isLoading}>
            {isLoading ? "Refreshing" : "Refresh"}
          </button>
          <button type="button" onClick={() => void copyJson()} disabled={!health}>
            Copy JSON
          </button>
        </div>
      </header>

      {errorMessage ? (
        <section className="system-status__api-error">
          <h2>Server API unavailable</h2>
          <p>{errorMessage}</p>
          <button type="button" onClick={() => void loadHealth()}>
            Refresh
          </button>
        </section>
      ) : null}

      {health ? (
        <>
          <SystemStatusSummary health={health} />
          <section className="system-status__layout">
            <SystemStatusChecks checks={health.checks} />
            <aside className="system-status__side">
              <SystemStatusFixFirst items={health.fixFirst} />
              <SystemStatusFeatureImpact items={health.featureImpacts} />
            </aside>
          </section>
        </>
      ) : !errorMessage ? (
        <section className="system-status__loading">Loading system status...</section>
      ) : null}
    </main>
  );
}
```

Create small components:

- `SystemStatusSummary.tsx`: render `health.status`, `health.summary`, generated time, and `health.durationMs`.
- `SystemStatusChecks.tsx`: render `checks` exactly as provided; group by `category` is allowed, but do not compute status.
- `SystemStatusEvidence.tsx`: render `Object.entries(evidence)` in a key/value table; stringify nested values with `JSON.stringify(value)`.
- `SystemStatusFixFirst.tsx`: render `items`; empty state is "No action required."
- `SystemStatusFeatureImpact.tsx`: render `items`; links use `window.location.href = link.href` or normal `<a href={link.href}>`.

Use status-to-class mapping only:

```ts
export function statusClass(status: SystemHealthStatus): string {
  return `system-status__status system-status__status--${status.toLowerCase()}`;
}
```

This mapping is styling only and is allowed.

- [ ] **Step 5: Add CSS**

Create `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`. Use the visual prototype direction but no decorative score ring. Keep cards at 8px radius, dense tables, clear chips, and responsive layout. Use classes prefixed with `system-status__`.

- [ ] **Step 6: Add Blazor host and navigation**

Create `src/LightRAGNet.Web/Components/Pages/SystemStatus.razor`:

```razor
@page "/system-status"
@using Microsoft.JSInterop
@implements IAsyncDisposable
@inject IConfiguration Configuration
@inject IJSRuntime JSRuntime

<PageTitle>System Status</PageTitle>

<link rel="stylesheet" href="system-status/assets/system-status.css" />

<div id="system-status-root" data-api-base="@ApiBase"></div>

@code {
    private const string RootElementId = "system-status-root";
    private IJSObjectReference? systemStatusModule;
    private string ApiBase => Configuration["ApiBaseUrl"] ?? "http://localhost:5261";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        systemStatusModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./system-status/assets/system-status.js");
        await systemStatusModule.InvokeVoidAsync("mountSystemStatus", RootElementId, ApiBase);
    }

    public async ValueTask DisposeAsync()
    {
        if (systemStatusModule is null)
        {
            return;
        }

        try
        {
            await systemStatusModule.InvokeVoidAsync("unmountSystemStatus", RootElementId);
            await systemStatusModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
```

Modify `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`:

```razor
<MudNavLink Href="system-status" Icon="@Icons.Material.Filled.MonitorHeart">System Status</MudNavLink>
```

- [ ] **Step 7: Build React assets**

```powershell
Push-Location src\LightRAGNet.Web\ClientApp
npm run build
Pop-Location
```

Expected:

- `src/LightRAGNet.Web/wwwroot/system-status/assets/system-status.js` exists.
- `src/LightRAGNet.Web/wwwroot/system-status/assets/system-status.css` exists.
- Existing graph workbench assets still exist.

- [ ] **Step 8: Run Web source tests**

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~SystemStatusHostSourceTests|FullyQualifiedName~GraphWorkbenchHostSourceTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 9: Commit Task 5**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/system-status src/LightRAGNet.Web/ClientApp/src/styles/system-status.css src/LightRAGNet.Web/Components/Pages/SystemStatus.razor src/LightRAGNet.Web/Components/Layout/NavMenu.razor src/LightRAGNet.Web/wwwroot/system-status tests/LightRAGNet.Web.Tests/SystemStatusHostSourceTests.cs
git commit -m "feat: add react system status page"
```

---

### Task 6: Final Verification and Documentation Alignment

**Files:**
- Modify if needed: `docs/python-dotnet-feature-parity.md`
- Modify if needed: `docs/superpowers/archives/INDEX.md` and new archive after implementation is complete

- [ ] **Step 1: Run targeted Server tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~SystemHealth" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 2: Run targeted Web tests**

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~SystemStatus" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Run ClientApp verification**

```powershell
Push-Location src\LightRAGNet.Web\ClientApp
npm test -- --run src/api/systemStatusApi.test.ts
npm run typecheck
npm run build
Pop-Location
```

Expected: PASS.

- [ ] **Step 4: Run full solution tests**

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS, or document any pre-existing unrelated failure separately with exact test name and error.

- [ ] **Step 5: Check diff hygiene**

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` has no output. `git status --short` only shows intended files.

- [ ] **Step 6: Asset gate**

Run closeout status scripts before final response or merge:

```powershell
$env:PYTHONIOENCODING='utf-8'
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_status.py . --topic "server-operational-readiness" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_closeout.py . --topic "server-operational-readiness" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "server-operational-readiness" --json
```

Expected: route likely requires a new archive for completed requirement work. Create the archive with `archive-superpowers-feature` after implementation verification.

- [ ] **Step 7: Commit final verification/doc updates**

If Task 6 changes docs or generated assets, stage only the changed files reported by `git status --short`. For example, if an archive is created:

```powershell
git add docs/superpowers/archives/2026-05/2026-05-24-server-operational-readiness-archives.md docs/superpowers/archives/INDEX.md
git commit -m "docs: archive system status readiness"
```

If no files changed, do not create an empty commit.

---

## Self-Review

Spec coverage:

- Evidence-driven backend DTO: Tasks 1-3.
- Plugin-style health checks: Tasks 1-2.
- Per-check timeout and exception capture: Task 1.
- Redaction: Task 1 and Task 3 tests.
- Ten v1 checks: Task 2.
- React island and Blazor host: Task 5.
- No frontend health aggregation: Task 5 source test.
- Refresh, Copy JSON, evidence expansion, feature impact links: Task 5.
- No destructive operations: Task 5 scope and source review.
- Verification and asset gate: Task 6.

Placeholder scan:

- No unfinished markers or vague test instructions are intentionally left.

Type consistency:

- Backend status enum is `SystemHealthStatus`.
- API response type is `SystemHealthResponse`.
- React status union mirrors backend enum string names.
- Route is `/api/system/health`.
- Web route is `/system-status`.
