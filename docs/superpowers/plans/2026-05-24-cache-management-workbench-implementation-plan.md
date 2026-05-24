# Cache Management Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an evidence-driven Cache Management workbench that reports real cache hit rates, saved calls, and safe clear plans through backend metrics and a React Web UI.

**Architecture:** Runtime cache access is consolidated into `LightRagLlmCacheService.GetOrCreate...` methods so hit/miss/factory duration are measured at the only boundary that knows cache validity and miss generation cost. Cache metrics are persisted separately from `llm_cache`, cache inventory is inspected through a safe interface, and Server APIs aggregate metrics plus inventory into a React workbench DTO. Old `TryGet...` / `Save...` runtime APIs are removed in this task.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, ASP.NET Core controllers, keyed `IKVStore`, JSON persistence, Blazor host pages, React 19, TypeScript, Vite, Vitest.

---

## Scope And Guardrails

- Implement the design in `docs/superpowers/specs/2026-05-24-cache-management-workbench-design.md`.
- Use the visual reference at `docs/superpowers/visuals/cache-management-ui-concept.html`.
- Do not display full prompt text, `return_value`, provider responses, API keys, bearer tokens, passwords, or authorization headers in API responses or UI.
- Do not keep runtime `TryGet...` / `Save...` APIs after migration. Existing tests that currently call those methods must use `GetOrCreate...` or test seeding helpers.
- Do not compute hit rate from entry count. Hit rate comes from persisted `read` metrics with `outcome = hit | miss | invalid | disabled | error`.
- Metrics write failures must not break the RAG path.

## File Structure

### Core Query Cache

- Create `src/LightRAGNet/Services/QueryCache/CacheMetricOperation.cs`
  - Owns `Read`, `Save`, `Delete`, `Clear` constants.
- Create `src/LightRAGNet/Services/QueryCache/CacheReadOutcome.cs`
  - Owns `Hit`, `Miss`, `Invalid`, `Disabled`, `Error` constants.
- Create `src/LightRAGNet/Services/QueryCache/CacheMetricEvent.cs`
  - Immutable persisted metric event DTO.
- Create `src/LightRAGNet/Services/QueryCache/CacheValueResult.cs`
  - Return type for all `GetOrCreate...` runtime cache methods.
- Create `src/LightRAGNet/Services/QueryCache/ICacheMetricsStore.cs`
  - Store abstraction for append and read operations.
- Create `src/LightRAGNet/Services/QueryCache/ICacheMetricsRecorder.cs`
  - Runtime non-throwing recorder abstraction.
- Create `src/LightRAGNet/Services/QueryCache/CacheMetricsRecorder.cs`
  - Converts runtime observations into store events.
- Create `src/LightRAGNet/Services/QueryCache/JsonCacheMetricsStore.cs`
  - JSON file persistence with atomic writes and bounded retention.
- Create `src/LightRAGNet/Services/QueryCache/CacheMetricsOptions.cs`
  - Retention and enablement options.
- Create `src/LightRAGNet.Core/Interfaces/IInspectableKVStore.cs`
  - Safe read-only KV snapshot interface used by cache inventory.
- Modify `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
  - Add `GetOrCreate...` methods.
  - Remove old `TryGet...` / `Save...` public runtime methods.
  - Keep private helpers for lookup, save, entry parsing, revision.
- Modify `src/LightRAGNet.Storage/JsonKVStore.cs`
  - Implement `IInspectableKVStore` with cloned snapshots for production cache inventory.
- Modify `src/LightRAGNet/LightRAGOptions.cs`
  - Add cache metrics options only if options stay under `LightRAG`; otherwise use dedicated `CacheMetricsOptions`.
- Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Register metrics store and recorder.

### Runtime Call Sites

- Modify `src/LightRAGNet/LightRAG.cs`
  - Migrate keyword cache and query answer cache to `GetOrCreate`.
- Modify `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
  - Migrate extract cache to `GetOrCreateExtractAsync`.
- Modify `src/LightRAGNet/Services/KnowledgeGraphMerge/DescriptionMerger.cs`
  - Migrate summary cache to `GetOrCreateSummaryAsync`.

### Server Cache Management

- Create `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementModels.cs`
  - API-facing DTOs.
- Create `src/LightRAGNet.Server/Services/CacheManagement/CacheEntryInspector.cs`
  - Safe `llm_cache` inventory scanner over `IInspectableKVStore` snapshots.
- Create `src/LightRAGNet.Server/Services/CacheManagement/CacheClearPlanner.cs`
  - Generates safe clear plans from metrics and inventory.
- Create `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementService.cs`
  - Aggregates metrics, inventory, insights, trend, clear plan, and executes clear requests.
- Create `src/LightRAGNet.Server/Controllers/CacheManagementController.cs`
  - `GET /api/cache-management/overview`
  - `POST /api/cache-management/clear`
- Modify `src/LightRAGNet.Server/Program.cs`
  - Register cache management services if the app uses server-local service registration.

### Web UI

- Create `src/LightRAGNet.Web/Components/Pages/CacheManagement.razor`
  - Blazor React island host.
- Modify `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`
  - Add `Cache Management` nav link.
- Modify `src/LightRAGNet.Web/ClientApp/vite.config.ts`
  - Add `cacheManagement` entry and output assets.
- Create `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.ts`
  - Fetch overview and clear endpoints.
- Create `src/LightRAGNet.Web/ClientApp/src/types/cacheManagement.ts`
  - TypeScript DTOs.
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`
  - Mount/unmount entry.
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheManagementWorkbench.tsx`
  - Main workbench orchestration.
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheSummaryCards.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheFamilyTable.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheInsights.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheEfficiencyTrend.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheClearPlan.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheEntryDrilldown.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheMeasurementContract.tsx`
- Create `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css`
- Generated by build: `src/LightRAGNet.Web/wwwroot/cache-management/assets/cache-management.js`
- Generated by build: `src/LightRAGNet.Web/wwwroot/cache-management/assets/cache-management.css`

### Tests

- Modify `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`
  - Replace old TryGet/Save tests with GetOrCreate tests.
- Create `tests/LightRAGNet.Tests/QueryCache/JsonCacheMetricsStoreTests.cs`
- Create `tests/LightRAGNet.Tests/QueryCache/CacheMetricsRecorderTests.cs`
- Modify affected query cache integration tests under `tests/LightRAGNet.Tests/QueryCache/`
  - Use cache store seeding helpers or `GetOrCreate`.
- Create `tests/LightRAGNet.Server.Tests/CacheManagementControllerTests.cs`
- Create `tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs`
- Create or modify `tests/LightRAGNet.Web.Tests/CacheManagementHostSourceTests.cs`
- Create `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.test.ts`
- Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheManagementWorkbench.test.tsx`

---

### Task 1: Add Cache Metric Primitives

**Files:**
- Create: `src/LightRAGNet/Services/QueryCache/CacheMetricOperation.cs`
- Create: `src/LightRAGNet/Services/QueryCache/CacheReadOutcome.cs`
- Create: `src/LightRAGNet/Services/QueryCache/CacheMetricEvent.cs`
- Create: `src/LightRAGNet/Services/QueryCache/CacheValueResult.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/CacheMetricEventTests.cs`

- [ ] **Step 1: Write failing tests for metric primitives**

Create `tests/LightRAGNet.Tests/QueryCache/CacheMetricEventTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Tests.QueryCache;

public sealed class CacheMetricEventTests
{
    [Fact]
    public void CacheMetricEvent_CreateReadEvent_KeepsSafeFieldsOnly()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-24T12:00:00Z");

        var metric = CacheMetricEvent.CreateRead(
            timestamp,
            workspace: "_",
            cacheType: LightRagCacheKeyBuilder.QueryCacheType,
            outcome: CacheReadOutcome.Hit,
            mode: "Mix",
            durationMs: 4,
            factoryDurationMs: null,
            cacheKey: "Mix:query:abcdef0123456789",
            revision: 12);

        metric.Operation.Should().Be(CacheMetricOperation.Read);
        metric.Outcome.Should().Be(CacheReadOutcome.Hit);
        metric.CacheKeyPrefix.Should().Be("Mix:query:abcdef");
        metric.Workspace.Should().Be("_");
        metric.CacheType.Should().Be("query");
        metric.Mode.Should().Be("Mix");
        metric.DurationMs.Should().Be(4);
        metric.FactoryDurationMs.Should().BeNull();
        metric.Revision.Should().Be(12);
    }

    [Fact]
    public void CacheValueResult_Hit_ReturnsExpectedFlags()
    {
        var result = CacheValueResult<string>.FromHit(
            "cached",
            LightRagCacheKeyBuilder.QueryCacheType,
            "Mix:query:abcdef",
            TimeSpan.FromMilliseconds(3));

        result.Value.Should().Be("cached");
        result.CacheEnabled.Should().BeTrue();
        result.Hit.Should().BeTrue();
        result.Saved.Should().BeFalse();
        result.FactoryDuration.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~CacheMetricEventTests" --no-restore --verbosity minimal
```

Expected: fails because `CacheMetricEvent`, `CacheMetricOperation`, `CacheReadOutcome`, and `CacheValueResult<T>` do not exist.

- [ ] **Step 3: Add metric primitive files**

Create `src/LightRAGNet/Services/QueryCache/CacheMetricOperation.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public static class CacheMetricOperation
{
    public const string Read = "read";
    public const string Save = "save";
    public const string Delete = "delete";
    public const string Clear = "clear";
}
```

Create `src/LightRAGNet/Services/QueryCache/CacheReadOutcome.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public static class CacheReadOutcome
{
    public const string Hit = "hit";
    public const string Miss = "miss";
    public const string Invalid = "invalid";
    public const string Disabled = "disabled";
    public const string Error = "error";
}
```

Create `src/LightRAGNet/Services/QueryCache/CacheMetricEvent.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public sealed record CacheMetricEvent(
    string Id,
    DateTimeOffset Timestamp,
    string Workspace,
    string CacheType,
    string Operation,
    string? Outcome,
    string? Mode,
    long DurationMs,
    long? FactoryDurationMs,
    string? CacheKeyPrefix,
    long? Revision)
{
    public static CacheMetricEvent CreateRead(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        long durationMs,
        long? factoryDurationMs,
        string? cacheKey,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Read,
            outcome,
            mode,
            Math.Max(0, durationMs),
            factoryDurationMs is null ? null : Math.Max(0, factoryDurationMs.Value),
            BuildKeyPrefix(cacheKey),
            revision);
    }

    public static CacheMetricEvent CreateSave(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        string? mode,
        long durationMs,
        string? cacheKey,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Save,
            null,
            mode,
            Math.Max(0, durationMs),
            null,
            BuildKeyPrefix(cacheKey),
            revision);
    }

    public static CacheMetricEvent CreateClear(
        DateTimeOffset timestamp,
        string workspace,
        string cacheType,
        long durationMs,
        long? revision)
    {
        return new CacheMetricEvent(
            Guid.NewGuid().ToString("N"),
            timestamp,
            NormalizeWorkspace(workspace),
            cacheType,
            CacheMetricOperation.Clear,
            null,
            null,
            Math.Max(0, durationMs),
            null,
            null,
            revision);
    }

    private static string NormalizeWorkspace(string workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
    }

    private static string? BuildKeyPrefix(string? cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            return null;
        }

        return cacheKey.Length <= 16 ? cacheKey : cacheKey[..16];
    }
}
```

Create `src/LightRAGNet/Services/QueryCache/CacheValueResult.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public sealed record CacheValueResult<T>(
    T Value,
    bool CacheEnabled,
    bool Hit,
    bool Saved,
    string? CacheKey,
    string CacheType,
    TimeSpan CacheLookupDuration,
    TimeSpan? FactoryDuration)
{
    public static CacheValueResult<T> FromHit(
        T value,
        string cacheType,
        string cacheKey,
        TimeSpan cacheLookupDuration)
    {
        return new CacheValueResult<T>(
            value,
            CacheEnabled: true,
            Hit: true,
            Saved: false,
            cacheKey,
            cacheType,
            cacheLookupDuration,
            FactoryDuration: null);
    }

    public static CacheValueResult<T> FromMiss(
        T value,
        bool cacheEnabled,
        bool saved,
        string? cacheKey,
        string cacheType,
        TimeSpan cacheLookupDuration,
        TimeSpan factoryDuration)
    {
        return new CacheValueResult<T>(
            value,
            cacheEnabled,
            Hit: false,
            saved,
            cacheKey,
            cacheType,
            cacheLookupDuration,
            factoryDuration);
    }
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~CacheMetricEventTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src\LightRAGNet\Services\QueryCache\CacheMetricOperation.cs src\LightRAGNet\Services\QueryCache\CacheReadOutcome.cs src\LightRAGNet\Services\QueryCache\CacheMetricEvent.cs src\LightRAGNet\Services\QueryCache\CacheValueResult.cs tests\LightRAGNet.Tests\QueryCache\CacheMetricEventTests.cs
git commit -m "feat: add cache metric primitives"
```

---

### Task 2: Add JSON Cache Metrics Store And Recorder

**Files:**
- Create: `src/LightRAGNet/Services/QueryCache/CacheMetricsOptions.cs`
- Create: `src/LightRAGNet/Services/QueryCache/ICacheMetricsStore.cs`
- Create: `src/LightRAGNet/Services/QueryCache/ICacheMetricsRecorder.cs`
- Create: `src/LightRAGNet/Services/QueryCache/JsonCacheMetricsStore.cs`
- Create: `src/LightRAGNet/Services/QueryCache/CacheMetricsRecorder.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/JsonCacheMetricsStoreTests.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/CacheMetricsRecorderTests.cs`

- [ ] **Step 1: Write failing tests for metrics store**

Create `tests/LightRAGNet.Tests/QueryCache/JsonCacheMetricsStoreTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.QueryCache;

public sealed class JsonCacheMetricsStoreTests
{
    [Fact]
    public async Task AppendAsync_PersistsEventsAndReadAsyncLoadsThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lightrag-cache-metrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "cache_metrics.json");

        try
        {
            var store = new JsonCacheMetricsStore(
                filePath,
                new CacheMetricsOptions { MaxEvents = 10, RetentionDays = 30 },
                NullLogger<JsonCacheMetricsStore>.Instance);

            var metric = CacheMetricEvent.CreateRead(
                DateTimeOffset.Parse("2026-05-24T12:00:00Z"),
                "_",
                LightRagCacheKeyBuilder.QueryCacheType,
                CacheReadOutcome.Hit,
                "Mix",
                4,
                null,
                "Mix:query:abcdef012345",
                1);

            await store.AppendAsync(metric);

            var reloaded = new JsonCacheMetricsStore(
                filePath,
                new CacheMetricsOptions { MaxEvents = 10, RetentionDays = 30 },
                NullLogger<JsonCacheMetricsStore>.Instance);

            var events = await reloaded.ReadAsync(DateTimeOffset.Parse("2026-05-24T00:00:00Z"), DateTimeOffset.Parse("2026-05-25T00:00:00Z"));
            events.Should().ContainSingle();
            events[0].Outcome.Should().Be(CacheReadOutcome.Hit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AppendAsync_AppliesMaxEventsRetention()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lightrag-cache-metrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "cache_metrics.json");

        try
        {
            var store = new JsonCacheMetricsStore(
                filePath,
                new CacheMetricsOptions { MaxEvents = 2, RetentionDays = 30 },
                NullLogger<JsonCacheMetricsStore>.Instance);

            await store.AppendAsync(CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow.AddMinutes(-2), "_", "query", CacheReadOutcome.Miss, "Mix", 1, 5, "a", 1));
            await store.AppendAsync(CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow.AddMinutes(-1), "_", "query", CacheReadOutcome.Hit, "Mix", 1, null, "b", 1));
            await store.AppendAsync(CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow, "_", "query", CacheReadOutcome.Hit, "Mix", 1, null, "c", 1));

            var events = await store.ReadAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
            events.Should().HaveCount(2);
            events.Select(e => e.CacheKeyPrefix).Should().Contain(new[] { "b", "c" });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Write failing tests for non-throwing recorder**

Create `tests/LightRAGNet.Tests/QueryCache/CacheMetricsRecorderTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.QueryCache;

public sealed class CacheMetricsRecorderTests
{
    [Fact]
    public async Task RecordReadAsync_WhenStoreThrows_DoesNotThrow()
    {
        var recorder = new CacheMetricsRecorder(
            new ThrowingCacheMetricsStore(),
            NullLogger<CacheMetricsRecorder>.Instance);

        var act = async () => await recorder.RecordReadAsync(
            "_",
            LightRagCacheKeyBuilder.QueryCacheType,
            CacheReadOutcome.Hit,
            "Mix",
            TimeSpan.FromMilliseconds(1),
            factoryDuration: null,
            cacheKey: "Mix:query:abcdef",
            revision: 1,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingCacheMetricsStore : ICacheMetricsStore
    {
        public Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("metrics store failed");
        }

        public Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CacheMetricEvent>>([]);
        }
    }
}
```

- [ ] **Step 3: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~JsonCacheMetricsStoreTests|FullyQualifiedName~CacheMetricsRecorderTests" --no-restore --verbosity minimal
```

Expected: fails because store and recorder types do not exist.

- [ ] **Step 4: Implement metrics store and recorder**

Create `src/LightRAGNet/Services/QueryCache/CacheMetricsOptions.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public sealed class CacheMetricsOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxEvents { get; set; } = 20000;
}
```

Create `src/LightRAGNet/Services/QueryCache/ICacheMetricsStore.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public interface ICacheMetricsStore
{
    Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
```

Create `src/LightRAGNet/Services/QueryCache/ICacheMetricsRecorder.cs`:

```csharp
namespace LightRAGNet.Services.QueryCache;

public interface ICacheMetricsRecorder
{
    Task RecordReadAsync(
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        TimeSpan duration,
        TimeSpan? factoryDuration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default);

    Task RecordSaveAsync(
        string workspace,
        string cacheType,
        string? mode,
        TimeSpan duration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default);

    Task RecordClearAsync(
        string workspace,
        string cacheType,
        TimeSpan duration,
        long? revision,
        CancellationToken cancellationToken = default);
}
```

Create `src/LightRAGNet/Services/QueryCache/JsonCacheMetricsStore.cs`:

```csharp
using System.Text.Json;
using LightRAGNet.Core.IO;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.QueryCache;

public sealed class JsonCacheMetricsStore(
    string filePath,
    CacheMetricsOptions options,
    ILogger<JsonCacheMetricsStore> logger) : ICacheMetricsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var events = await LoadAllAsync(cancellationToken);
            events.Add(metric);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.RetentionDays));
            var retained = events
                .Where(item => item.Timestamp >= cutoff)
                .OrderBy(item => item.Timestamp)
                .TakeLast(Math.Max(1, options.MaxEvents))
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var json = JsonSerializer.Serialize(retained, LightRAGJsonOptions.HumanReadableIndented);
            await AtomicFileWriter.WriteAllTextAsync(filePath, json, cancellationToken: cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var events = await LoadAllAsync(cancellationToken);
            return events
                .Where(item => item.Timestamp >= from && item.Timestamp <= to)
                .OrderBy(item => item.Timestamp)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<CacheMetricEvent>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<CacheMetricEvent>>(json, LightRAGJsonOptions.HumanReadable) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load cache metrics from {FilePath}.", filePath);
            return [];
        }
    }
}
```

Create `src/LightRAGNet/Services/QueryCache/CacheMetricsRecorder.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.QueryCache;

public sealed class CacheMetricsRecorder(
    ICacheMetricsStore store,
    ILogger<CacheMetricsRecorder> logger) : ICacheMetricsRecorder
{
    public Task RecordReadAsync(
        string workspace,
        string cacheType,
        string outcome,
        string? mode,
        TimeSpan duration,
        TimeSpan? factoryDuration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateRead(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            outcome,
            mode,
            (long)duration.TotalMilliseconds,
            factoryDuration is null ? null : (long)factoryDuration.Value.TotalMilliseconds,
            cacheKey,
            revision);

        return AppendWithoutThrowAsync(metric, cancellationToken);
    }

    public Task RecordSaveAsync(
        string workspace,
        string cacheType,
        string? mode,
        TimeSpan duration,
        string? cacheKey,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateSave(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            mode,
            (long)duration.TotalMilliseconds,
            cacheKey,
            revision);

        return AppendWithoutThrowAsync(metric, cancellationToken);
    }

    public Task RecordClearAsync(
        string workspace,
        string cacheType,
        TimeSpan duration,
        long? revision,
        CancellationToken cancellationToken = default)
    {
        var metric = CacheMetricEvent.CreateClear(
            DateTimeOffset.UtcNow,
            workspace,
            cacheType,
            (long)duration.TotalMilliseconds,
            revision);

        return AppendWithoutThrowAsync(metric, cancellationToken);
    }

    private async Task AppendWithoutThrowAsync(CacheMetricEvent metric, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendAsync(metric, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record cache metric {Operation}/{Outcome}.", metric.Operation, metric.Outcome);
        }
    }
}
```

- [ ] **Step 5: Register metrics services**

Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs` after `services.Configure<LightRAGOptions>`:

```csharp
services.Configure<CacheMetricsOptions>(configuration.GetSection("CacheMetrics"));
```

Add registrations near `LightRagLlmCacheService`:

```csharp
services.AddSingleton<ICacheMetricsStore>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<JsonCacheMetricsStore>>();
    var lightragOptions = sp.GetRequiredService<IOptions<LightRAGOptions>>().Value;
    var metricsOptions = sp.GetRequiredService<IOptions<CacheMetricsOptions>>().Value;
    var workingDir = lightragOptions.WorkingDir;

    if (!Path.IsPathRooted(workingDir))
    {
        workingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workingDir);
    }

    Directory.CreateDirectory(workingDir);
    return new JsonCacheMetricsStore(
        Path.Combine(workingDir, "cache_metrics.json"),
        metricsOptions,
        logger);
});
services.AddSingleton<ICacheMetricsRecorder, CacheMetricsRecorder>();
```

- [ ] **Step 6: Run tests and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~JsonCacheMetricsStoreTests|FullyQualifiedName~CacheMetricsRecorderTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet\Services\QueryCache src\LightRAGNet.Hosting\ServiceCollectionExtensions.cs tests\LightRAGNet.Tests\QueryCache\JsonCacheMetricsStoreTests.cs tests\LightRAGNet.Tests\QueryCache\CacheMetricsRecorderTests.cs
git commit -m "feat: persist cache metrics"
```

---

### Task 3: Implement GetOrCreate Runtime Cache APIs

**Files:**
- Modify: `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
- Test: `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`

- [ ] **Step 1: Write failing tests for GetOrCreate behavior**

Add these tests to `tests/LightRAGNet.Tests/QueryCache/LightRagLlmCacheServiceTests.cs`:

```csharp
[Fact]
public async Task GetOrCreateQueryResponseAsync_WhenCacheHit_DoesNotCallFactory()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var service = CreateService(store, keyBuilder: keyBuilder);
    var queryParam = new QueryParam { Mode = QueryMode.Mix };
    var keywords = new KeywordsResult { HighLevelKeywords = ["cache"], LowLevelKeywords = ["chunk"] };
    var key = keyBuilder.BuildRagQueryKey("workspace-a", 2, "what is cache?", queryParam, keywords);
    store.Seed(
        key,
        new LightRagCacheEntry(
            "cached answer",
            LightRagCacheKeyBuilder.QueryCacheType,
            "what is cache?",
            new Dictionary<string, object?> { ["workspace_query_revision"] = 2 },
            123)
        .ToDictionary());
    var factoryCalls = 0;

    var result = await service.GetOrCreateQueryResponseAsync(
        "workspace-a",
        2,
        "what is cache?",
        queryParam,
        keywords,
        _ =>
        {
            factoryCalls++;
            return Task.FromResult("live answer");
        });

    result.Value.Should().Be("cached answer");
    result.Hit.Should().BeTrue();
    result.Saved.Should().BeFalse();
    factoryCalls.Should().Be(0);
}

[Fact]
public async Task GetOrCreateExtractAsync_WhenCacheMiss_CallsFactoryAndSavesKey()
{
    var store = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    var service = CreateService(store, keyBuilder: keyBuilder);
    var canonicalPrompt = "extract prompt";

    var result = await service.GetOrCreateExtractAsync(
        canonicalPrompt,
        "chunk-a",
        _ => Task.FromResult("raw extract"));

    result.Value.Should().Be("raw extract");
    result.Hit.Should().BeFalse();
    result.Saved.Should().BeTrue();
    result.CacheKey.Should().Be(keyBuilder.BuildExtractKey(canonicalPrompt));
    store.Items.Should().ContainKey(result.CacheKey!);
}

[Fact]
public async Task GetOrCreateSummaryAsync_WhenIndexingCacheDisabled_CallsFactoryWithoutSaving()
{
    var store = new InMemoryKvStore();
    var service = CreateService(
        store,
        options: new LightRAGOptions
        {
            EnableLlmCache = true,
            EnableLlmCacheForEntityExtract = false
        });

    var result = await service.GetOrCreateSummaryAsync(
        "summary prompt",
        _ => Task.FromResult("summary"));

    result.Value.Should().Be("summary");
    result.CacheEnabled.Should().BeFalse();
    result.Hit.Should().BeFalse();
    result.Saved.Should().BeFalse();
    store.Items.Should().BeEmpty();
}
```

Update `CreateService` helper in the same test file to accept `ICacheMetricsRecorder? recorder = null`:

```csharp
private static LightRagLlmCacheService CreateService(
    InMemoryKvStore? store = null,
    LightRagCacheKeyBuilder? keyBuilder = null,
    LightRAGOptions? options = null,
    ICacheMetricsRecorder? recorder = null)
{
    return new LightRagLlmCacheService(
        store ?? new InMemoryKvStore(),
        Options.Create(options ?? new LightRAGOptions()),
        keyBuilder ?? new LightRagCacheKeyBuilder(),
        recorder ?? new NoopCacheMetricsRecorder(),
        NullLogger<LightRagLlmCacheService>.Instance);
}

private sealed class NoopCacheMetricsRecorder : ICacheMetricsRecorder
{
    public Task RecordReadAsync(string workspace, string cacheType, string outcome, string? mode, TimeSpan duration, TimeSpan? factoryDuration, string? cacheKey, long? revision, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RecordSaveAsync(string workspace, string cacheType, string? mode, TimeSpan duration, string? cacheKey, long? revision, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RecordClearAsync(string workspace, string cacheType, TimeSpan duration, long? revision, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRagLlmCacheServiceTests" --no-restore --verbosity minimal
```

Expected: fails because `GetOrCreate...` methods and constructor parameter do not exist.

- [ ] **Step 3: Update service constructor**

Modify `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs` constructor to include recorder:

```csharp
public sealed class LightRagLlmCacheService(
    [FromKeyedServices(KVContracts.LLMCache)] IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    LightRagCacheKeyBuilder keyBuilder,
    ICacheMetricsRecorder metricsRecorder,
    ILogger<LightRagLlmCacheService> logger)
```

- [ ] **Step 4: Add public GetOrCreate methods**

Add methods to `LightRagLlmCacheService`:

```csharp
public Task<CacheValueResult<KeywordsResult>> GetOrCreateKeywordsAsync(
    string workspace,
    QueryMode mode,
    string query,
    Func<CancellationToken, Task<KeywordsResult>> factory,
    CancellationToken cancellationToken = default)
{
    return GetOrCreateAsync(
        workspace,
        LightRagCacheKeyBuilder.KeywordsCacheType,
        mode.ToString(),
        revision: null,
        keyBuilder.BuildKeywordKey(workspace, mode, query),
        IsKeywordCacheEnabled(),
        async token =>
        {
            var data = await llmCacheStore.GetByIdAsync(keyBuilder.BuildKeywordKey(workspace, mode, query), token);
            if (!LightRagCacheEntry.TryFromDictionary(data, out var entry) ||
                !TryDeserializeKeywordPayload(entry.ReturnValue, out var payload))
            {
                return (false, default(KeywordsResult)!);
            }

            return (true, new KeywordsResult
            {
                HighLevelKeywords = payload.HighLevelKeywords ?? [],
                LowLevelKeywords = payload.LowLevelKeywords ?? []
            });
        },
        factory,
        async (value, token) =>
        {
            if (!HasAnyKeyword(value))
            {
                return false;
            }

            var payload = JsonSerializer.Serialize(
                new KeywordCachePayload
                {
                    HighLevelKeywords = value.HighLevelKeywords,
                    LowLevelKeywords = value.LowLevelKeywords
                },
                SerializerOptions);
            await SaveEntryAsync(
                keyBuilder.BuildKeywordKey(workspace, mode, query),
                new LightRagCacheEntry(
                    payload,
                    LightRagCacheKeyBuilder.KeywordsCacheType,
                    query,
                    null,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                token);
            return true;
        },
        cancellationToken);
}
```

Add equivalent public methods:

```csharp
public Task<CacheValueResult<string>> GetOrCreateQueryResponseAsync(
    string workspace,
    long workspaceQueryRevision,
    string query,
    QueryParam queryParam,
    KeywordsResult keywords,
    Func<CancellationToken, Task<string>> factory,
    CancellationToken cancellationToken = default)
{
    var key = BuildQueryKey(workspace, workspaceQueryRevision, query, queryParam, keywords);
    return GetOrCreateAsync(
        workspace,
        LightRagCacheKeyBuilder.QueryCacheType,
        queryParam.Mode.ToString(),
        workspaceQueryRevision,
        key,
        IsQueryCacheEnabled(),
        async token =>
        {
            var data = await llmCacheStore.GetByIdAsync(key, token);
            return LightRagCacheEntry.TryFromDictionary(data, out var entry)
                ? (true, entry.ReturnValue)
                : (false, default!);
        },
        factory,
        async (value, token) =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            await SaveEntryAsync(
                key,
                new LightRagCacheEntry(
                    value,
                    LightRagCacheKeyBuilder.QueryCacheType,
                    query,
                    BuildQueryParamSnapshot(queryParam, keywords, workspaceQueryRevision),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                token);
            return true;
        },
        cancellationToken);
}

public Task<CacheValueResult<string>> GetOrCreateExtractAsync(
    string canonicalPrompt,
    string chunkId,
    Func<CancellationToken, Task<string>> factory,
    CancellationToken cancellationToken = default)
{
    var key = keyBuilder.BuildExtractKey(canonicalPrompt);
    return GetOrCreateIndexingAsync(
        LightRagCacheKeyBuilder.ExtractCacheType,
        key,
        canonicalPrompt,
        chunkId,
        factory,
        cancellationToken);
}

public Task<CacheValueResult<string>> GetOrCreateSummaryAsync(
    string canonicalPrompt,
    Func<CancellationToken, Task<string>> factory,
    CancellationToken cancellationToken = default)
{
    var key = keyBuilder.BuildSummaryKey(canonicalPrompt);
    return GetOrCreateIndexingAsync(
        LightRagCacheKeyBuilder.SummaryCacheType,
        key,
        canonicalPrompt,
        chunkId: null,
        factory,
        cancellationToken);
}
```

Add private helpers in `LightRagLlmCacheService`:

```csharp
private Task<CacheValueResult<string>> GetOrCreateIndexingAsync(
    string cacheType,
    string key,
    string canonicalPrompt,
    string? chunkId,
    Func<CancellationToken, Task<string>> factory,
    CancellationToken cancellationToken)
{
    return GetOrCreateAsync(
        options.Value.Workspace,
        cacheType,
        mode: null,
        revision: null,
        key,
        IsIndexingCacheEnabled(),
        async token =>
        {
            var data = await llmCacheStore.GetByIdAsync(key, token);
            return LightRagCacheEntry.TryFromDictionary(data, out var entry) &&
                   string.Equals(entry.CacheType, cacheType, StringComparison.Ordinal)
                ? (true, entry.ReturnValue)
                : (false, default!);
        },
        factory,
        async (value, token) =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            await SaveEntryAsync(
                key,
                new LightRagCacheEntry(
                    value,
                    cacheType,
                    canonicalPrompt,
                    null,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    chunkId),
                token);
            return true;
        },
        cancellationToken);
}

private async Task<CacheValueResult<T>> GetOrCreateAsync<T>(
    string workspace,
    string cacheType,
    string? mode,
    long? revision,
    string key,
    bool cacheEnabled,
    Func<CancellationToken, Task<(bool Found, T Value)>> tryRead,
    Func<CancellationToken, Task<T>> factory,
    Func<T, CancellationToken, Task<bool>> save,
    CancellationToken cancellationToken)
{
    var lookupStarted = TimeProvider.System.GetTimestamp();
    if (!cacheEnabled)
    {
        var lookupDuration = TimeProvider.System.GetElapsedTime(lookupStarted);
        var factoryStarted = TimeProvider.System.GetTimestamp();
        var value = await factory(cancellationToken);
        var factoryDuration = TimeProvider.System.GetElapsedTime(factoryStarted);
        await metricsRecorder.RecordReadAsync(workspace, cacheType, CacheReadOutcome.Disabled, mode, lookupDuration, factoryDuration, key, revision, cancellationToken);
        return CacheValueResult<T>.FromMiss(value, false, false, null, cacheType, lookupDuration, factoryDuration);
    }

    try
    {
        var readResult = await tryRead(cancellationToken);
        var lookupDuration = TimeProvider.System.GetElapsedTime(lookupStarted);
        if (readResult.Found)
        {
            await metricsRecorder.RecordReadAsync(workspace, cacheType, CacheReadOutcome.Hit, mode, lookupDuration, null, key, revision, cancellationToken);
            return CacheValueResult<T>.FromHit(readResult.Value, cacheType, key, lookupDuration);
        }

        var factoryStarted = TimeProvider.System.GetTimestamp();
        var value = await factory(cancellationToken);
        var factoryDuration = TimeProvider.System.GetElapsedTime(factoryStarted);
        await metricsRecorder.RecordReadAsync(workspace, cacheType, CacheReadOutcome.Miss, mode, lookupDuration, factoryDuration, key, revision, cancellationToken);
        var saveStarted = TimeProvider.System.GetTimestamp();
        var saved = await save(value, cancellationToken);
        var saveDuration = TimeProvider.System.GetElapsedTime(saveStarted);
        if (saved)
        {
            await metricsRecorder.RecordSaveAsync(workspace, cacheType, mode, saveDuration, key, revision, cancellationToken);
        }

        return CacheValueResult<T>.FromMiss(value, true, saved, saved ? key : null, cacheType, lookupDuration, factoryDuration);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        var lookupDuration = TimeProvider.System.GetElapsedTime(lookupStarted);
        logger.LogWarning(ex, "Cache lookup failed for {CacheType} entry {CacheKey}.", cacheType, key);
        var factoryStarted = TimeProvider.System.GetTimestamp();
        var value = await factory(cancellationToken);
        var factoryDuration = TimeProvider.System.GetElapsedTime(factoryStarted);
        await metricsRecorder.RecordReadAsync(workspace, cacheType, CacheReadOutcome.Error, mode, lookupDuration, factoryDuration, key, revision, cancellationToken);
        return CacheValueResult<T>.FromMiss(value, true, false, null, cacheType, lookupDuration, factoryDuration);
    }
}

private async Task SaveEntryAsync(
    string key,
    LightRagCacheEntry entry,
    CancellationToken cancellationToken)
{
    await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
    {
        [key] = entry.ToDictionary()
    }, cancellationToken);
    await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
}
```

- [ ] **Step 5: Run tests and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRagLlmCacheServiceTests" --no-restore --verbosity minimal
```

Expected: pass after updating affected helper signatures.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet\Services\QueryCache\LightRagLlmCacheService.cs tests\LightRAGNet.Tests\QueryCache\LightRagLlmCacheServiceTests.cs
git commit -m "feat: add get-or-create cache runtime"
```

---

### Task 4: Migrate Runtime Call Sites And Remove Old Cache APIs

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
- Modify: `src/LightRAGNet/Services/KnowledgeGraphMerge/DescriptionMerger.cs`
- Modify: `src/LightRAGNet/Services/QueryCache/LightRagLlmCacheService.cs`
- Modify: query cache tests under `tests/LightRAGNet.Tests/QueryCache/`

- [ ] **Step 1: Migrate keyword cache in `LightRAG.cs`**

Replace `GetQueryKeywordsAsync` with:

```csharp
private async Task<QueryKeywordResult> GetQueryKeywordsAsync(
    string query,
    QueryParam queryParam,
    CancellationToken cancellationToken)
{
    if (queryParam.HighLevelKeywords.Count > 0 || queryParam.LowLevelKeywords.Count > 0)
    {
        return new QueryKeywordResult(
            new KeywordsResult
            {
                HighLevelKeywords = queryParam.HighLevelKeywords,
                LowLevelKeywords = queryParam.LowLevelKeywords
            });
    }

    if (!IsKnowledgeGraphQueryMode(queryParam.Mode))
    {
        return new QueryKeywordResult(
            await llmService.ExtractKeywordsAsync(query, cancellationToken: cancellationToken));
    }

    var workspace = documentLifecycleService.GetDefaultWorkspace();
    var cacheResult = await llmCacheService.GetOrCreateKeywordsAsync(
        workspace,
        queryParam.Mode,
        query,
        token => llmService.ExtractKeywordsAsync(query, cancellationToken: token),
        cancellationToken);

    return new QueryKeywordResult(cacheResult.Value);
}

private sealed record QueryKeywordResult(KeywordsResult Keywords);
```

Remove the `ShouldSaveKeywordCache` block from `QueryAsync`:

```csharp
// Delete this block completely:
if (keywordResult.ShouldSaveKeywordCache)
{
    await llmCacheService.SaveKeywordsAsync(...);
}
```

- [ ] **Step 2: Migrate query answer cache in `LightRAG.cs`**

Replace `GenerateQueryAnswerAsync` cache lookup/save logic with:

```csharp
private async Task<string> GenerateQueryAnswerAsync(
    string query,
    QueryParam queryParam,
    KeywordsResult keywords,
    string? systemPrompt,
    QueryAnswerCacheContext? cacheContext,
    CancellationToken cancellationToken)
{
    Task<string> GenerateAsync(CancellationToken token)
    {
        return llmService.GenerateAsync(
            query,
            systemPrompt,
            queryParam.ConversationHistory,
            temperature: 0.3f,
            cancellationToken: token);
    }

    if (cacheContext is null)
    {
        return await GenerateAsync(cancellationToken);
    }

    var cacheResult = await llmCacheService.GetOrCreateQueryResponseAsync(
        cacheContext.Workspace,
        cacheContext.Revision,
        query,
        queryParam,
        keywords,
        GenerateAsync,
        cancellationToken);

    return cacheResult.Value;
}
```

- [ ] **Step 3: Migrate extract cache in `DocumentProcessingService.cs`**

Replace the cache branch in `ProcessChunkAsync` with:

```csharp
var cacheResult = await llmCacheService.GetOrCreateExtractAsync(
    prompt.CanonicalPrompt,
    chunk.Id,
    token => GenerateExtractResponseAsync(prompt, chunk.Id, token),
    cancellationToken);
var rawResponse = cacheResult.Value;
var llmCacheKeys = cacheResult.CacheKey is null
    ? new List<string>()
    : [cacheResult.CacheKey];

if (cacheResult.Hit)
{
    logger.LogDebug("Extract cache hit for chunk {ChunkId}", chunk.Id);
}
else
{
    logger.LogDebug("Extract cache miss for chunk {ChunkId}, generated response.", chunk.Id);
}
```

- [ ] **Step 4: Migrate summary cache in `DescriptionMerger.cs`**

Replace `SummarizeWithCacheAsync` body with:

```csharp
private async Task<string> SummarizeWithCacheAsync(
    string descriptionType,
    string descriptionName,
    List<string> descriptions,
    CancellationToken cancellationToken)
{
    var prompt = SummaryPromptBuilder.Build(
        descriptionType,
        descriptionName,
        descriptions,
        _options.SummaryLengthRecommended);

    var cacheResult = await llmCacheService.GetOrCreateSummaryAsync(
        prompt,
        async token =>
        {
            var summary = await llmService.GenerateAsync(
                prompt,
                temperature: 0.3f,
                cancellationToken: token);
            return CleanThinkTags(summary);
        },
        cancellationToken);

    return cacheResult.Value;
}
```

- [ ] **Step 5: Remove old runtime methods from `LightRagLlmCacheService.cs`**

Delete public methods:

```csharp
TryGetKeywordsAsync
SaveKeywordsAsync
TryGetQueryResponseAsync
SaveQueryResponseAsync
TryGetExtractAsync
SaveExtractAsync
TryGetSummaryAsync
SaveSummaryAsync
```

Keep private helpers that are still used by `GetOrCreate...`, such as:

```csharp
BuildQueryKey
BuildQueryParamSnapshot
HasAnyKeyword
TryDeserializeKeywordPayload
ReadRevision
```

- [ ] **Step 6: Update tests to stop calling removed APIs**

For tests that previously used `SaveQueryResponseAsync`, seed the store directly:

```csharp
private static void SeedQueryCache(
    InMemoryKvStore store,
    LightRagCacheKeyBuilder keyBuilder,
    string workspace,
    long revision,
    string query,
    QueryParam queryParam,
    KeywordsResult keywords,
    string response)
{
    var key = queryParam.Mode == QueryMode.Bypass
        ? keyBuilder.BuildBypassQueryKey(query, queryParam)
        : keyBuilder.BuildRagQueryKey(workspace, revision, query, queryParam, keywords);

    store.Seed(
        key,
        new LightRagCacheEntry(
            response,
            LightRagCacheKeyBuilder.QueryCacheType,
            query,
            new Dictionary<string, object?> { ["workspace_query_revision"] = revision },
            DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        .ToDictionary());
}
```

For tests that previously used `SaveKeywordsAsync`, seed the store directly:

```csharp
private static void SeedKeywordCache(
    InMemoryKvStore store,
    LightRagCacheKeyBuilder keyBuilder,
    string workspace,
    QueryMode mode,
    string query,
    IReadOnlyList<string> highLevelKeywords,
    IReadOnlyList<string> lowLevelKeywords)
{
    var key = keyBuilder.BuildKeywordKey(workspace, mode, query);
    var payload = JsonSerializer.Serialize(
        new
        {
            high_level_keywords = highLevelKeywords,
            low_level_keywords = lowLevelKeywords
        },
        LightRAGJsonOptions.HumanReadable);

    store.Seed(
        key,
        new LightRagCacheEntry(
            payload,
            LightRagCacheKeyBuilder.KeywordsCacheType,
            query,
            null,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        .ToDictionary());
}
```

- [ ] **Step 7: Prove old runtime APIs are gone**

Run:

```powershell
rg -n "TryGetKeywordsAsync|SaveKeywordsAsync|TryGetQueryResponseAsync|SaveQueryResponseAsync|TryGetExtractAsync|SaveExtractAsync|TryGetSummaryAsync|SaveSummaryAsync" src tests
```

Expected: no output.

- [ ] **Step 8: Run targeted tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessingServiceTests|FullyQualifiedName~DescriptionMergerTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add src\LightRAGNet\LightRAG.cs src\LightRAGNet\Services\DocumentProcessing\DocumentProcessingService.cs src\LightRAGNet\Services\KnowledgeGraphMerge\DescriptionMerger.cs src\LightRAGNet\Services\QueryCache\LightRagLlmCacheService.cs tests\LightRAGNet.Tests\QueryCache tests\LightRAGNet.Tests\DocumentProcessing tests\LightRAGNet.Tests\KnowledgeGraphMerge
git commit -m "refactor: migrate cache runtime to get-or-create"
```

---

### Task 5: Add Cache Inventory, Clear Planning, And Management API

**Files:**
- Create: `src/LightRAGNet.Core/Interfaces/IInspectableKVStore.cs`
- Modify: `src/LightRAGNet.Storage/JsonKVStore.cs`
- Create: `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementModels.cs`
- Create: `src/LightRAGNet.Server/Services/CacheManagement/CacheEntryInspector.cs`
- Create: `src/LightRAGNet.Server/Services/CacheManagement/CacheClearPlanner.cs`
- Create: `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementService.cs`
- Create: `src/LightRAGNet.Server/Controllers/CacheManagementController.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Test: `tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs`
- Test: `tests/LightRAGNet.Server.Tests/CacheManagementControllerTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Server.Services.CacheManagement;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class CacheManagementServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_ComputesHitRateFromReadMetrics()
    {
        var metricsStore = new InMemoryCacheMetricsStore([
            CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow.AddMinutes(-10), "_", "query", CacheReadOutcome.Hit, "Mix", 2, null, "Mix:query:a", 3),
            CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow.AddMinutes(-9), "_", "query", CacheReadOutcome.Miss, "Mix", 2, 100, "Mix:query:b", 3),
            CacheMetricEvent.CreateRead(DateTimeOffset.UtcNow.AddMinutes(-8), "_", "query", CacheReadOutcome.Hit, "Mix", 1, null, "Mix:query:c", 3)
        ]);
        var llmCache = new InMemoryKvStore();
        var service = CreateService(metricsStore, llmCache);

        var overview = await service.GetOverviewAsync("_", "24h", CancellationToken.None);

        overview.Summary.OverallHitRate.Should().BeApproximately(2d / 3d, 0.001);
        overview.Summary.ProviderCallsAvoided.Should().Be(2);
        overview.Families.Should().ContainSingle(family => family.CacheType == "query")
            .Which.Hits.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutMetrics_ReturnsNotMeasuredHitRate()
    {
        var service = CreateService(new InMemoryCacheMetricsStore([]), new InMemoryKvStore());

        var overview = await service.GetOverviewAsync("_", "24h", CancellationToken.None);

        overview.Summary.OverallHitRate.Should().BeNull();
        overview.Summary.Measured.Should().BeFalse();
    }

    private static CacheManagementService CreateService(
        ICacheMetricsStore metricsStore,
        InMemoryKvStore llmCache)
    {
        return new CacheManagementService(
            metricsStore,
            new CacheEntryInspector(llmCache),
            new CacheClearPlanner(),
            NullLogger<CacheManagementService>.Instance);
    }

    private sealed class InMemoryCacheMetricsStore(IReadOnlyList<CacheMetricEvent> events) : ICacheMetricsStore
    {
        public Task AppendAsync(CacheMetricEvent metric, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CacheMetricEvent>> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CacheMetricEvent>>(events.Where(item => item.Timestamp >= from && item.Timestamp <= to).ToList());
        }
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~CacheManagementServiceTests" --no-restore --verbosity minimal
```

Expected: fails because cache management service types do not exist.

- [ ] **Step 3: Implement server DTOs**

Create `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementModels.cs`:

```csharp
namespace LightRAGNet.Server.Services.CacheManagement;

public sealed record CacheOverviewResponse(
    string Workspace,
    string Window,
    DateTimeOffset GeneratedAt,
    CacheSummaryDto Summary,
    IReadOnlyList<CacheFamilyDto> Families,
    IReadOnlyList<CacheTrendPointDto> Trend,
    IReadOnlyList<CacheInsightDto> Insights,
    IReadOnlyList<CacheClearPlanDto> ClearPlan,
    IReadOnlyList<CacheEntrySampleDto> EntrySamples);

public sealed record CacheSummaryDto(
    double? OverallHitRate,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs,
    int StaleOrRiskyEntries,
    bool Measured);

public sealed record CacheFamilyDto(
    string CacheType,
    string DisplayName,
    double? HitRate,
    int Hits,
    int Misses,
    int Attempts,
    int EntryCount,
    string ValueLevel,
    string RiskLevel,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs,
    string Message);

public sealed record CacheTrendPointDto(DateTimeOffset Timestamp, double? HitRate, int SavedCalls);

public sealed record CacheInsightDto(string Title, string Message, string Level);

public sealed record CacheClearPlanDto(
    string Id,
    string Title,
    IReadOnlyList<string> CacheTypes,
    int EntryCount,
    string Risk,
    string Impact,
    bool RequiresConfirmation);

public sealed record CacheEntrySampleDto(
    string CacheKeyPrefix,
    string CacheType,
    DateTimeOffset? LastHit,
    string State);

public sealed record CacheClearRequest(string Workspace, string PlanId, bool Confirm);

public sealed record CacheClearResponse(
    bool Succeeded,
    int DeletedEntries,
    IReadOnlyList<string> CacheTypes,
    string Message,
    long? RevisionAfter);
```

- [ ] **Step 4: Implement safe cache inventory scanner**

Create `src/LightRAGNet.Core/Interfaces/IInspectableKVStore.cs`:

```csharp
namespace LightRAGNet.Core.Interfaces;

public interface IInspectableKVStore
{
    Task<IReadOnlyDictionary<string, Dictionary<string, object>>> SnapshotAsync(
        CancellationToken cancellationToken = default);
}
```

Modify `src/LightRAGNet.Storage/JsonKVStore.cs`:

```csharp
public class JsonKVStore : IKVStore, IInspectableKVStore
{
    public async Task<IReadOnlyDictionary<string, Dictionary<string, object>>> SnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return CloneData(_data);
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

Create `src/LightRAGNet.Server/Services/CacheManagement/CacheEntryInspector.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheEntryInspector(IKVStore llmCacheStore)
{
    public async Task<CacheInventory> InspectAsync(string workspace, long currentRevision, CancellationToken cancellationToken)
    {
        if (llmCacheStore is not IInspectableKVStore inspectable)
        {
            return new CacheInventory([], []);
        }

        var snapshot = await inspectable.SnapshotAsync(cancellationToken);
        var entries = snapshot
            .Select(pair => ToInventoryEntry(pair.Key, pair.Value, currentRevision))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToList();

        var samples = entries
            .Take(25)
            .Select(entry => new CacheEntrySampleDto(
                entry.CacheKeyPrefix,
                entry.CacheType,
                LastHit: null,
                entry.State))
            .ToList();

        return new CacheInventory(entries, samples);
    }

    private static CacheInventoryEntry? ToInventoryEntry(
        string key,
        Dictionary<string, object> data,
        long currentRevision)
    {
        if (!LightRagCacheEntry.TryFromDictionary(data, out var entry))
        {
            return null;
        }

        var state = entry.CacheType == LightRagCacheKeyBuilder.QueryCacheType &&
                    entry.QueryParam is not null &&
                    entry.QueryParam.TryGetValue("workspace_query_revision", out var revisionValue) &&
                    TryReadRevision(revisionValue, out var revision) &&
                    revision != currentRevision
            ? "old revision"
            : entry.ChunkId is not null
                ? "doc-linked"
                : "current";

        return new CacheInventoryEntry(
            key,
            key.Length <= 16 ? key : key[..16],
            entry.CacheType,
            state);
    }

    private static bool TryReadRevision(object? value, out long revision)
    {
        revision = 0;
        return value switch
        {
            long number => Set(number, out revision),
            int number => Set(number, out revision),
            string text when long.TryParse(text, out var number) => Set(number, out revision),
            _ => false
        };
    }

    private static bool Set(long value, out long revision)
    {
        revision = value;
        return true;
    }
}

public sealed record CacheInventory(
    IReadOnlyList<CacheInventoryEntry> Entries,
    IReadOnlyList<CacheEntrySampleDto> Samples);

public sealed record CacheInventoryEntry(
    string CacheKey,
    string CacheKeyPrefix,
    string CacheType,
    string State);
```

Also update test double `tests/LightRAGNet.Tests/TestDoubles/InMemoryKvStore.cs` to implement `IInspectableKVStore` with a cloned snapshot. It may keep its existing `Items` property for test assertions, but production inventory must use `SnapshotAsync`.

- [ ] **Step 5: Implement clear planner and service**

Create `src/LightRAGNet.Server/Services/CacheManagement/CacheClearPlanner.cs`:

```csharp
using LightRAGNet.Services.QueryCache;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheClearPlanner
{
    public IReadOnlyList<CacheClearPlanDto> BuildPlan(CacheInventory inventory)
    {
        var staleQueryEntries = inventory.Entries
            .Count(entry => entry.CacheType == LightRagCacheKeyBuilder.QueryCacheType && entry.State == "old revision");
        var unusedSummaryEntries = inventory.Entries
            .Count(entry => entry.CacheType == LightRagCacheKeyBuilder.SummaryCacheType);

        return
        [
            new CacheClearPlanDto(
                "stale-query-cache",
                "Clear stale query cache",
                [LightRagCacheKeyBuilder.QueryCacheType],
                staleQueryEntries,
                "Low",
                "Deletes old workspace revision query answers only.",
                RequiresConfirmation: false),
            new CacheClearPlanDto(
                "summary-cache-review",
                "Review summary cache",
                [LightRagCacheKeyBuilder.SummaryCacheType],
                unusedSummaryEntries,
                "Medium",
                "Deletes summary cache entries selected for review; next merge summary may regenerate them.",
                RequiresConfirmation: true),
            new CacheClearPlanDto(
                "all-llm-cache",
                "Clear all LLM cache",
                [LightRagCacheKeyBuilder.QueryCacheType, LightRagCacheKeyBuilder.KeywordsCacheType, LightRagCacheKeyBuilder.ExtractCacheType, LightRagCacheKeyBuilder.SummaryCacheType],
                inventory.Entries.Count,
                "High",
                "Clears query, keywords, extract and summary cache. Repeated query and indexing efficiency will drop until cache warms again.",
                RequiresConfirmation: true)
        ];
    }
}
```

Create `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementService.cs`:

```csharp
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Server.Services.CacheManagement;

public sealed class CacheManagementService(
    ICacheMetricsStore metricsStore,
    CacheEntryInspector inspector,
    CacheClearPlanner clearPlanner,
    ILogger<CacheManagementService> logger)
{
    public async Task<CacheOverviewResponse> GetOverviewAsync(
        string workspace,
        string window,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspace = string.IsNullOrWhiteSpace(workspace) ? "_" : workspace.Trim();
        var now = DateTimeOffset.UtcNow;
        var from = window == "7d" ? now.AddDays(-7) : now.AddHours(-24);
        var metrics = await metricsStore.ReadAsync(from, now, cancellationToken);
        var currentRevision = 0L;
        var inventory = await inspector.InspectAsync(normalizedWorkspace, currentRevision, cancellationToken);
        var families = BuildFamilies(metrics, inventory);
        var hits = families.Sum(family => family.Hits);
        var attempts = families.Sum(family => family.Attempts);
        var estimatedLatency = families.Any(family => family.EstimatedLatencySavedMs is not null)
            ? families.Sum(family => family.EstimatedLatencySavedMs ?? 0)
            : (long?)null;

        return new CacheOverviewResponse(
            normalizedWorkspace,
            window,
            now,
            new CacheSummaryDto(
                attempts == 0 ? null : (double)hits / attempts,
                hits,
                estimatedLatency,
                inventory.Entries.Count(entry => entry.State is "old revision"),
                attempts > 0),
            families,
            BuildTrend(metrics),
            BuildInsights(families, inventory),
            clearPlanner.BuildPlan(inventory),
            inventory.Samples);
    }

    public Task<CacheClearResponse> ClearAsync(CacheClearRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Cache clear requested: {PlanId} for workspace {Workspace}", request.PlanId, request.Workspace);
        return Task.FromResult(new CacheClearResponse(
            Succeeded: false,
            DeletedEntries: 0,
            CacheTypes: [],
            Message: "Clear execution is implemented after clear plan filtering is covered by API tests.",
            RevisionAfter: null));
    }

    private static IReadOnlyList<CacheFamilyDto> BuildFamilies(
        IReadOnlyList<CacheMetricEvent> metrics,
        CacheInventory inventory)
    {
        var cacheTypes = new[]
        {
            LightRagCacheKeyBuilder.QueryCacheType,
            LightRagCacheKeyBuilder.KeywordsCacheType,
            LightRagCacheKeyBuilder.ExtractCacheType,
            LightRagCacheKeyBuilder.SummaryCacheType
        };

        return cacheTypes.Select(cacheType =>
        {
            var reads = metrics
                .Where(metric => metric.Operation == CacheMetricOperation.Read && metric.CacheType == cacheType)
                .ToList();
            var hits = reads.Count(metric => metric.Outcome == CacheReadOutcome.Hit);
            var misses = reads.Count(metric => metric.Outcome == CacheReadOutcome.Miss);
            var attempts = hits + misses;
            var entryCount = inventory.Entries.Count(entry => entry.CacheType == cacheType);
            var averageMissDuration = reads
                .Where(metric => metric.Outcome == CacheReadOutcome.Miss && metric.FactoryDurationMs is not null)
                .Select(metric => metric.FactoryDurationMs!.Value)
                .DefaultIfEmpty()
                .Average();
            var estimatedLatency = averageMissDuration <= 0 ? (long?)null : (long)(hits * averageMissDuration);

            return new CacheFamilyDto(
                cacheType,
                DisplayName(cacheType),
                attempts == 0 ? null : (double)hits / attempts,
                hits,
                misses,
                attempts,
                entryCount,
                ValueLevel(attempts, hits),
                RiskLevel(cacheType, inventory),
                hits,
                estimatedLatency,
                Message(cacheType, attempts, hits));
        }).ToList();
    }

    private static IReadOnlyList<CacheTrendPointDto> BuildTrend(IReadOnlyList<CacheMetricEvent> metrics)
    {
        return metrics
            .Where(metric => metric.Operation == CacheMetricOperation.Read)
            .GroupBy(metric => new DateTimeOffset(metric.Timestamp.Year, metric.Timestamp.Month, metric.Timestamp.Day, metric.Timestamp.Hour, 0, 0, TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var reads = group.ToList();
                var hits = reads.Count(metric => metric.Outcome == CacheReadOutcome.Hit);
                var attempts = reads.Count(metric => metric.Outcome is CacheReadOutcome.Hit or CacheReadOutcome.Miss);
                return new CacheTrendPointDto(group.Key, attempts == 0 ? null : (double)hits / attempts, hits);
            })
            .ToList();
    }

    private static IReadOnlyList<CacheInsightDto> BuildInsights(IReadOnlyList<CacheFamilyDto> families, CacheInventory inventory)
    {
        var extract = families.First(family => family.CacheType == LightRagCacheKeyBuilder.ExtractCacheType);
        return
        [
            new CacheInsightDto("Keep extract cache", $"Extract cache hit rate is {FormatRate(extract.HitRate)} with {extract.EntryCount} entries.", "Good"),
            new CacheInsightDto("Review stale query cache", $"{inventory.Entries.Count(entry => entry.State == "old revision")} old revision query entries can be reviewed.", "Warning")
        ];
    }

    private static string DisplayName(string cacheType) => cacheType switch
    {
        LightRagCacheKeyBuilder.QueryCacheType => "Query answer",
        LightRagCacheKeyBuilder.KeywordsCacheType => "Keywords",
        LightRagCacheKeyBuilder.ExtractCacheType => "Entity extract",
        LightRagCacheKeyBuilder.SummaryCacheType => "Summary",
        _ => cacheType
    };

    private static string ValueLevel(int attempts, int hits)
    {
        if (attempts == 0)
        {
            return "NotMeasured";
        }

        var hitRate = (double)hits / attempts;
        return hitRate >= 0.8 ? "VeryHigh" : hitRate >= 0.6 ? "High" : hitRate >= 0.3 ? "Medium" : "Low";
    }

    private static string RiskLevel(string cacheType, CacheInventory inventory)
    {
        if (cacheType == LightRagCacheKeyBuilder.QueryCacheType &&
            inventory.Entries.Any(entry => entry.CacheType == cacheType && entry.State == "old revision"))
        {
            return "OldRevision";
        }

        if (cacheType == LightRagCacheKeyBuilder.ExtractCacheType)
        {
            return "DocLinked";
        }

        return "Current";
    }

    private static string Message(string cacheType, int attempts, int hits)
    {
        return attempts == 0
            ? $"{DisplayName(cacheType)} cache has no measured reads in the selected window."
            : $"{DisplayName(cacheType)} cache avoided {hits} provider calls.";
    }

    private static string FormatRate(double? rate)
    {
        return rate is null ? "not measured" : $"{rate.Value:P1}";
    }
}
```

- [ ] **Step 6: Implement controller and DI**

Create `src/LightRAGNet.Server/Controllers/CacheManagementController.cs`:

```csharp
using LightRAGNet.Server.Services.CacheManagement;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/cache-management")]
public sealed class CacheManagementController(CacheManagementService service) : ControllerBase
{
    [HttpGet("overview")]
    public Task<CacheOverviewResponse> GetOverview(
        [FromQuery] string workspace = "_",
        [FromQuery] string window = "24h",
        CancellationToken cancellationToken = default)
    {
        return service.GetOverviewAsync(workspace, window, cancellationToken);
    }

    [HttpPost("clear")]
    public Task<CacheClearResponse> Clear(
        [FromBody] CacheClearRequest request,
        CancellationToken cancellationToken = default)
    {
        return service.ClearAsync(request, cancellationToken);
    }
}
```

Register in `src/LightRAGNet.Server/Program.cs`:

```csharp
builder.Services.AddSingleton<CacheEntryInspector>();
builder.Services.AddSingleton<CacheClearPlanner>();
builder.Services.AddSingleton<CacheManagementService>();
```

- [ ] **Step 7: Add API tests**

Create `tests/LightRAGNet.Server.Tests/CacheManagementControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Server.Services.CacheManagement;

namespace LightRAGNet.Server.Tests;

public sealed class CacheManagementControllerTests
{
    [Fact]
    public async Task GetOverview_ReturnsCacheManagementShape()
    {
        await using var factory = new LightRagServerWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/cache-management/overview?workspace=_&window=24h");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CacheOverviewResponse>();
        body.Should().NotBeNull();
        body!.Summary.Should().NotBeNull();
        body.Families.Should().Contain(family => family.CacheType == "query");
        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("api_key", StringComparison.OrdinalIgnoreCase);
        rawJson.Should().NotContain("authorization", StringComparison.OrdinalIgnoreCase);
        rawJson.Should().NotContain("return_value", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 8: Run server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~CacheManagement" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add src\LightRAGNet.Core\Interfaces\IInspectableKVStore.cs src\LightRAGNet.Storage\JsonKVStore.cs src\LightRAGNet.Server\Services\CacheManagement src\LightRAGNet.Server\Controllers\CacheManagementController.cs src\LightRAGNet.Server\Program.cs tests\LightRAGNet.Server.Tests\CacheManagementServiceTests.cs tests\LightRAGNet.Server.Tests\CacheManagementControllerTests.cs tests\LightRAGNet.Tests\TestDoubles\InMemoryKvStore.cs
git commit -m "feat: add cache management API"
```

---

### Task 6: Implement Real Clear Operations

**Files:**
- Modify: `src/LightRAGNet.Server/Services/CacheManagement/CacheManagementService.cs`
- Modify: `src/LightRAGNet.Server/Services/CacheManagement/CacheEntryInspector.cs`
- Test: `tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs`

- [ ] **Step 1: Add failing clear tests**

Append tests to `CacheManagementServiceTests.cs`:

```csharp
[Fact]
public async Task ClearAsync_AllCacheWithoutConfirmation_ReturnsFailure()
{
    var service = CreateService(new InMemoryCacheMetricsStore([]), new InMemoryKvStore());

    var result = await service.ClearAsync(new CacheClearRequest("_", "all-llm-cache", Confirm: false), CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    result.Message.Should().Contain("confirmation");
}

[Fact]
public async Task ClearAsync_StaleQueryCache_RemovesOnlyOldRevisionEntries()
{
    var llmCache = new InMemoryKvStore();
    var keyBuilder = new LightRagCacheKeyBuilder();
    llmCache.Seed("Mix:query:old", new LightRagCacheEntry("old", "query", "old prompt", new Dictionary<string, object?> { ["workspace_query_revision"] = -1 }, 1).ToDictionary());
    llmCache.Seed("Mix:query:current", new LightRagCacheEntry("current", "query", "current prompt", new Dictionary<string, object?> { ["workspace_query_revision"] = 0 }, 1).ToDictionary());
    llmCache.Seed(keyBuilder.BuildSummaryKey("summary prompt"), new LightRagCacheEntry("summary", "summary", "summary prompt", null, 1).ToDictionary());
    var service = CreateService(new InMemoryCacheMetricsStore([]), llmCache);

    var result = await service.ClearAsync(new CacheClearRequest("_", "stale-query-cache", Confirm: false), CancellationToken.None);

    result.Succeeded.Should().BeTrue();
    result.DeletedEntries.Should().Be(1);
    llmCache.Items.Should().NotContainKey("Mix:query:old");
    llmCache.Items.Should().ContainKey("Mix:query:current");
    llmCache.Items.Values.Should().Contain(entry => entry["cache_type"].ToString() == "summary");
}
```

- [ ] **Step 2: Run clear tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ClearAsync" --no-restore --verbosity minimal
```

Expected: fails because `ClearAsync` still returns a negative response for every request.

- [ ] **Step 3: Implement clear filtering in service**

Update `CacheManagementService.ClearAsync`:

```csharp
public async Task<CacheClearResponse> ClearAsync(CacheClearRequest request, CancellationToken cancellationToken)
{
    var normalizedWorkspace = string.IsNullOrWhiteSpace(request.Workspace) ? "_" : request.Workspace.Trim();
    var inventory = await inspector.InspectAsync(normalizedWorkspace, currentRevision: 0, cancellationToken);
    var plan = clearPlanner.BuildPlan(inventory).FirstOrDefault(item => item.Id == request.PlanId);
    if (plan is null)
    {
        return new CacheClearResponse(false, 0, [], $"Unknown clear plan: {request.PlanId}.", null);
    }

    if (plan.RequiresConfirmation && !request.Confirm)
    {
        return new CacheClearResponse(false, 0, plan.CacheTypes, $"Clear plan {request.PlanId} requires confirmation.", null);
    }

    var keys = request.PlanId switch
    {
        "stale-query-cache" => inventory.Entries
            .Where(entry => entry.CacheType == LightRagCacheKeyBuilder.QueryCacheType && entry.State == "old revision")
            .Select(entry => entry.CacheKey)
            .ToList(),
        "summary-cache-review" => inventory.Entries
            .Where(entry => entry.CacheType == LightRagCacheKeyBuilder.SummaryCacheType)
            .Select(entry => entry.CacheKey)
            .ToList(),
        "all-llm-cache" => inventory.Entries
            .Select(entry => entry.CacheKey)
            .ToList(),
        _ => []
    };

    if (keys.Count == 0)
    {
        return new CacheClearResponse(true, 0, plan.CacheTypes, "No cache entries matched the selected clear plan.", null);
    }

    await inspector.DeleteAsync(keys, cancellationToken);
    return new CacheClearResponse(true, keys.Count, plan.CacheTypes, $"Deleted {keys.Count} cache entries.", null);
}
```

Add to `CacheEntryInspector`:

```csharp
public Task DeleteAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
{
    return llmCacheStore.DeleteAsync(keys, cancellationToken);
}
```

- [ ] **Step 4: Run clear tests and verify pass**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ClearAsync" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src\LightRAGNet.Server\Services\CacheManagement tests\LightRAGNet.Server.Tests\CacheManagementServiceTests.cs
git commit -m "feat: clear managed cache entries"
```

---

### Task 7: Build React Cache Management Workbench

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/types/cacheManagement.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheManagementWorkbench.tsx`
- Create: component files listed in File Structure
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css`
- Modify: `src/LightRAGNet.Web/ClientApp/vite.config.ts`
- Test: `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.test.ts`
- Test: `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheManagementWorkbench.test.tsx`

- [ ] **Step 1: Add TypeScript DTOs**

Create `src/LightRAGNet.Web/ClientApp/src/types/cacheManagement.ts`:

```ts
export type CacheSummaryDto = {
  overallHitRate: number | null;
  providerCallsAvoided: number;
  estimatedLatencySavedMs: number | null;
  staleOrRiskyEntries: number;
  measured: boolean;
};

export type CacheFamilyDto = {
  cacheType: string;
  displayName: string;
  hitRate: number | null;
  hits: number;
  misses: number;
  attempts: number;
  entryCount: number;
  valueLevel: string;
  riskLevel: string;
  providerCallsAvoided: number;
  estimatedLatencySavedMs: number | null;
  message: string;
};

export type CacheTrendPointDto = {
  timestamp: string;
  hitRate: number | null;
  savedCalls: number;
};

export type CacheInsightDto = {
  title: string;
  message: string;
  level: string;
};

export type CacheClearPlanDto = {
  id: string;
  title: string;
  cacheTypes: string[];
  entryCount: number;
  risk: string;
  impact: string;
  requiresConfirmation: boolean;
};

export type CacheEntrySampleDto = {
  cacheKeyPrefix: string;
  cacheType: string;
  lastHit: string | null;
  state: string;
};

export type CacheOverviewResponse = {
  workspace: string;
  window: string;
  generatedAt: string;
  summary: CacheSummaryDto;
  families: CacheFamilyDto[];
  trend: CacheTrendPointDto[];
  insights: CacheInsightDto[];
  clearPlan: CacheClearPlanDto[];
  entrySamples: CacheEntrySampleDto[];
};
```

- [ ] **Step 2: Add API client and tests**

Create `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.ts`:

```ts
import type { CacheOverviewResponse } from "../types/cacheManagement";

export async function getCacheOverview(
  apiBase: string,
  workspace: string,
  window: string,
  signal?: AbortSignal
): Promise<CacheOverviewResponse> {
  const url = new URL("/api/cache-management/overview", apiBase);
  url.searchParams.set("workspace", workspace);
  url.searchParams.set("window", window);
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`Cache overview failed: ${response.status}`);
  }

  return (await response.json()) as CacheOverviewResponse;
}

export async function clearCachePlan(
  apiBase: string,
  workspace: string,
  planId: string,
  confirm: boolean
): Promise<void> {
  const response = await fetch(new URL("/api/cache-management/clear", apiBase), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ workspace, planId, confirm })
  });

  if (!response.ok) {
    throw new Error(`Cache clear failed: ${response.status}`);
  }
}
```

Create `src/LightRAGNet.Web/ClientApp/src/api/cacheManagementApi.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { getCacheOverview } from "./cacheManagementApi";

describe("cacheManagementApi", () => {
  it("passes workspace and window query parameters", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        workspace: "_",
        window: "24h",
        generatedAt: "2026-05-24T12:00:00Z",
        summary: { overallHitRate: null, providerCallsAvoided: 0, estimatedLatencySavedMs: null, staleOrRiskyEntries: 0, measured: false },
        families: [],
        trend: [],
        insights: [],
        clearPlan: [],
        entrySamples: []
      })
    });
    vi.stubGlobal("fetch", fetchMock);

    await getCacheOverview("http://localhost:5261", "_", "24h");

    const url = fetchMock.mock.calls[0][0] as URL;
    expect(url.searchParams.get("workspace")).toBe("_");
    expect(url.searchParams.get("window")).toBe("24h");
  });
});
```

- [ ] **Step 3: Add workbench components**

Create `src/LightRAGNet.Web/ClientApp/src/cache-management/CacheManagementWorkbench.tsx`:

```tsx
import { useCallback, useEffect, useState } from "react";
import { getCacheOverview } from "../api/cacheManagementApi";
import type { CacheOverviewResponse } from "../types/cacheManagement";
import { CacheSummaryCards } from "./CacheSummaryCards";
import { CacheFamilyTable } from "./CacheFamilyTable";
import { CacheInsights } from "./CacheInsights";
import { CacheEfficiencyTrend } from "./CacheEfficiencyTrend";
import { CacheClearPlan } from "./CacheClearPlan";
import { CacheEntryDrilldown } from "./CacheEntryDrilldown";
import { CacheMeasurementContract } from "./CacheMeasurementContract";
import "../styles/cache-management.css";

type Props = {
  apiBase: string;
};

export function CacheManagementWorkbench({ apiBase }: Props) {
  const [workspace, setWorkspace] = useState("_");
  const [window, setWindow] = useState("24h");
  const [overview, setOverview] = useState<CacheOverviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setOverview(await getCacheOverview(apiBase, workspace, window));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Cache overview failed");
    } finally {
      setLoading(false);
    }
  }, [apiBase, workspace, window]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <main className="cache-workbench">
      <header className="cache-page-head">
        <div>
          <h1>Cache Management</h1>
          <p>Inspect cache efficiency, risk, and clear impact from backend-measured evidence.</p>
        </div>
        <div className="cache-toolbar">
          <select value={workspace} onChange={(event) => setWorkspace(event.target.value)}>
            <option value="_">Workspace: _</option>
          </select>
          <select value={window} onChange={(event) => setWindow(event.target.value)}>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
          </select>
          <button type="button" onClick={refresh} disabled={loading}>Refresh</button>
          <button type="button" onClick={() => overview && navigator.clipboard?.writeText(JSON.stringify(overview, null, 2))}>Copy JSON</button>
        </div>
      </header>
      {error && <div className="cache-error">{error}</div>}
      {overview && (
        <>
          <CacheSummaryCards summary={overview.summary} />
          <div className="cache-content-grid">
            <CacheFamilyTable families={overview.families} />
            <CacheInsights insights={overview.insights} />
          </div>
          <div className="cache-content-grid">
            <CacheEfficiencyTrend trend={overview.trend} />
            <CacheClearPlan plans={overview.clearPlan} />
          </div>
          <div className="cache-content-grid">
            <CacheMeasurementContract />
            <CacheEntryDrilldown entries={overview.entrySamples} />
          </div>
        </>
      )}
    </main>
  );
}
```

Create compact component files:

```tsx
// CacheSummaryCards.tsx
import type { CacheSummaryDto } from "../types/cacheManagement";

export function CacheSummaryCards({ summary }: { summary: CacheSummaryDto }) {
  const rate = summary.overallHitRate === null ? "Not measured" : `${(summary.overallHitRate * 100).toFixed(1)}%`;
  const latency = summary.estimatedLatencySavedMs === null ? "Not measured" : `${Math.round(summary.estimatedLatencySavedMs / 60000)} min`;
  return (
    <section className="cache-summary-grid">
      <article><span>Overall hit rate</span><strong>{rate}</strong><p>{summary.measured ? "Measured from read outcomes." : "No read metrics yet."}</p></article>
      <article><span>Provider calls avoided</span><strong>{summary.providerCallsAvoided}</strong><p>Hits across cache families.</p></article>
      <article><span>Estimated latency saved</span><strong>{latency}</strong><p>Uses measured miss factory durations.</p></article>
      <article><span>Stale / risky entries</span><strong>{summary.staleOrRiskyEntries}</strong><p>Entries flagged for review.</p></article>
    </section>
  );
}
```

```tsx
// CacheFamilyTable.tsx
import type { CacheFamilyDto } from "../types/cacheManagement";

export function CacheFamilyTable({ families }: { families: CacheFamilyDto[] }) {
  return (
    <section className="cache-panel">
      <header><h2>Cache families</h2><p>Hit rate is backend-measured, not inferred from entry count.</p></header>
      <table>
        <thead><tr><th>Cache type</th><th>Hit rate</th><th>Hits / attempts</th><th>Entries</th><th>Value</th></tr></thead>
        <tbody>
          {families.map((family) => (
            <tr key={family.cacheType}>
              <td><strong>{family.displayName}</strong><small>{family.riskLevel}</small></td>
              <td>{family.hitRate === null ? "Not measured" : `${(family.hitRate * 100).toFixed(1)}%`}</td>
              <td>{family.hits} / {family.attempts}</td>
              <td>{family.entryCount}</td>
              <td>{family.valueLevel}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
```

```tsx
// CacheInsights.tsx
import type { CacheInsightDto } from "../types/cacheManagement";

export function CacheInsights({ insights }: { insights: CacheInsightDto[] }) {
  return (
    <section className="cache-panel">
      <header><h2>What should I do?</h2><p>Maintenance guidance generated from metrics and inventory.</p></header>
      {insights.map((insight) => <article className="cache-insight" key={insight.title}><strong>{insight.title}</strong><p>{insight.message}</p></article>)}
    </section>
  );
}
```

```tsx
// CacheEfficiencyTrend.tsx
import type { CacheTrendPointDto } from "../types/cacheManagement";

export function CacheEfficiencyTrend({ trend }: { trend: CacheTrendPointDto[] }) {
  return (
    <section className="cache-panel">
      <header><h2>Efficiency trend</h2><p>Hourly hit rate and saved calls.</p></header>
      <div className="cache-bars">
        {trend.map((point) => <span key={point.timestamp} style={{ height: `${Math.max(8, (point.hitRate ?? 0) * 100)}%` }} title={`${point.savedCalls} saved calls`} />)}
      </div>
    </section>
  );
}
```

```tsx
// CacheClearPlan.tsx
import type { CacheClearPlanDto } from "../types/cacheManagement";

export function CacheClearPlan({ plans }: { plans: CacheClearPlanDto[] }) {
  return (
    <section className="cache-panel">
      <header><h2>Clear plan</h2><p>Every destructive action shows impact first.</p></header>
      {plans.map((plan) => <article className="cache-clear-row" key={plan.id}><div><strong>{plan.title}</strong><p>{plan.impact}</p></div><button type="button">{plan.requiresConfirmation ? "Review" : `Clear ${plan.entryCount}`}</button></article>)}
    </section>
  );
}
```

```tsx
// CacheEntryDrilldown.tsx
import type { CacheEntrySampleDto } from "../types/cacheManagement";

export function CacheEntryDrilldown({ entries }: { entries: CacheEntrySampleDto[] }) {
  return (
    <section className="cache-panel">
      <header><h2>Entry drilldown</h2><p>Safe summaries only; full prompts and responses stay hidden.</p></header>
      <table>
        <thead><tr><th>Key prefix</th><th>Type</th><th>State</th></tr></thead>
        <tbody>{entries.map((entry) => <tr key={`${entry.cacheType}-${entry.cacheKeyPrefix}`}><td>{entry.cacheKeyPrefix}</td><td>{entry.cacheType}</td><td>{entry.state}</td></tr>)}</tbody>
      </table>
    </section>
  );
}
```

```tsx
// CacheMeasurementContract.tsx
export function CacheMeasurementContract() {
  return (
    <section className="cache-panel">
      <header><h2>Measurement contract</h2><p>Frontend displays backend evidence only.</p></header>
      <dl className="cache-contract">
        <dt>Hit rate</dt><dd>read outcomes: hit / (hit + miss)</dd>
        <dt>Latency saved</dt><dd>hits multiplied by recent miss factory duration</dd>
        <dt>Safety</dt><dd>no prompt, return value, provider response, or secret in JSON</dd>
      </dl>
    </section>
  );
}
```

- [ ] **Step 4: Add CSS**

Create `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css` using the visual reference:

```css
:root {
  color-scheme: dark;
}

.cache-workbench {
  min-height: 100vh;
  padding: 20px 24px 28px;
  background: #0d1117;
  color: #edf2f7;
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
}

.cache-page-head,
.cache-toolbar,
.cache-content-grid,
.cache-summary-grid {
  display: grid;
  gap: 14px;
}

.cache-page-head {
  grid-template-columns: 1fr auto;
  align-items: start;
  margin-bottom: 16px;
}

.cache-toolbar {
  grid-auto-flow: column;
}

.cache-toolbar select,
.cache-toolbar button,
.cache-clear-row button {
  min-height: 36px;
  border: 1px solid #303946;
  border-radius: 6px;
  background: #151b23;
  color: #edf2f7;
  padding: 0 12px;
}

.cache-summary-grid {
  grid-template-columns: repeat(4, minmax(0, 1fr));
  margin-bottom: 14px;
}

.cache-summary-grid article,
.cache-panel {
  border: 1px solid #303946;
  border-radius: 8px;
  background: #151b23;
}

.cache-summary-grid article {
  padding: 16px;
}

.cache-summary-grid span {
  display: block;
  color: #a9b4c2;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
}

.cache-summary-grid strong {
  display: block;
  margin: 10px 0;
  color: #7bd88f;
  font-size: 32px;
}

.cache-content-grid {
  grid-template-columns: minmax(0, 1.5fr) minmax(360px, .9fr);
  margin-bottom: 14px;
}

.cache-panel header {
  padding: 14px 16px;
  border-bottom: 1px solid #303946;
}

.cache-panel h2 {
  margin: 0 0 4px;
  font-size: 16px;
}

.cache-panel p {
  margin: 0;
  color: #a9b4c2;
  line-height: 1.5;
}

.cache-panel table {
  width: 100%;
  border-collapse: collapse;
}

.cache-panel th,
.cache-panel td {
  padding: 12px 16px;
  border-bottom: 1px solid #263140;
  text-align: left;
}

.cache-panel small {
  display: block;
  margin-top: 6px;
  color: #a9b4c2;
}

.cache-insight,
.cache-clear-row {
  margin: 12px 16px;
  padding: 12px;
  border: 1px solid #263140;
  border-radius: 8px;
  background: #111922;
}

.cache-clear-row {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 12px;
  align-items: center;
}

.cache-bars {
  height: 170px;
  display: grid;
  grid-auto-flow: column;
  align-items: end;
  gap: 8px;
  padding: 16px;
}

.cache-bars span {
  min-height: 8px;
  border-radius: 6px 6px 0 0;
  background: linear-gradient(180deg, #4cc9f0, #2a6f97);
}

.cache-contract {
  padding: 16px;
}

.cache-contract dt {
  color: #a9b4c2;
}

.cache-contract dd {
  margin: 0 0 12px;
}

.cache-error {
  margin-bottom: 14px;
  border: 1px solid rgba(255, 107, 107, .42);
  border-radius: 8px;
  padding: 12px;
  background: rgba(255, 107, 107, .13);
}

@media (max-width: 1100px) {
  .cache-page-head,
  .cache-summary-grid,
  .cache-content-grid {
    grid-template-columns: 1fr;
  }

  .cache-toolbar {
    grid-auto-flow: row;
  }
}
```

- [ ] **Step 5: Add React mount entry**

Create `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`:

```tsx
import { createRoot, type Root } from "react-dom/client";
import { CacheManagementWorkbench } from "./CacheManagementWorkbench";

const roots = new Map<string, Root>();

export function mountCacheManagement(rootElementId: string, apiBase: string) {
  const element = document.getElementById(rootElementId);
  if (!element) {
    throw new Error(`Cache management root not found: ${rootElementId}`);
  }

  const root = createRoot(element);
  roots.set(rootElementId, root);
  root.render(<CacheManagementWorkbench apiBase={apiBase} />);
}

export function unmountCacheManagement(rootElementId: string) {
  const root = roots.get(rootElementId);
  if (!root) {
    return;
  }

  root.unmount();
  roots.delete(rootElementId);
}
```

- [ ] **Step 6: Update Vite config**

Modify `src/LightRAGNet.Web/ClientApp/vite.config.ts`:

```ts
input: {
  graphWorkbench: "src/graph-workbench/main.tsx",
  cacheManagement: "src/cache-management/main.tsx"
},
output: {
  format: "es",
  entryFileNames: (chunkInfo) => {
    if (chunkInfo.name === "cacheManagement") {
      return "cache-management/assets/cache-management.js";
    }

    return "assets/graph-workbench.js";
  },
  chunkFileNames: "assets/[name].js",
  assetFileNames: (assetInfo) => {
    if (assetInfo.names?.some((name) => name.endsWith("cache-management.css"))) {
      return "cache-management/assets/cache-management.css";
    }

    if (assetInfo.names?.some((name) => name.endsWith(".css"))) {
      return "assets/graph-workbench.css";
    }

    return "assets/[name][extname]";
  }
}
```

- [ ] **Step 7: Run frontend tests/build**

Run:

```powershell
Push-Location src\LightRAGNet.Web\ClientApp; npm test -- --runInBand; npm run build; Pop-Location
```

Expected:

- Vitest tests pass.
- Vite writes `src/LightRAGNet.Web/wwwroot/cache-management/assets/cache-management.js`.
- Vite writes `src/LightRAGNet.Web/wwwroot/cache-management/assets/cache-management.css`.

- [ ] **Step 8: Commit**

```powershell
git add src\LightRAGNet.Web\ClientApp src\LightRAGNet.Web\wwwroot\cache-management
git commit -m "feat: add cache management workbench"
```

---

### Task 8: Add Blazor Host, Navigation, And Web Source Tests

**Files:**
- Create: `src/LightRAGNet.Web/Components/Pages/CacheManagement.razor`
- Modify: `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`
- Create: `tests/LightRAGNet.Web.Tests/CacheManagementHostSourceTests.cs`

- [ ] **Step 1: Write failing Web host source tests**

Create `tests/LightRAGNet.Web.Tests/CacheManagementHostSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class CacheManagementHostSourceTests
{
    [Fact]
    public void CacheManagement_HostsReactWorkbench()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "CacheManagement.razor");

        source.Should().Contain("@page \"/cache-management\"");
        source.Should().Contain("cache-management/assets/cache-management.css");
        source.Should().Contain("cache-management/assets/cache-management.js");
        source.Should().Contain("mountCacheManagement");
        source.Should().Contain("unmountCacheManagement");
    }

    [Fact]
    public void NavMenu_IncludesCacheManagement()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Layout", "NavMenu.razor");

        source.Should().Contain("Href=\"cache-management\"");
        source.Should().Contain("Cache Management");
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LightRAGNet.slnx")))
        {
            root = Directory.GetParent(root)!.FullName;
        }

        return File.ReadAllText(Path.Combine(new[] { root }.Concat(pathParts).ToArray()));
    }
}
```

- [ ] **Step 2: Run Web tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~CacheManagementHostSourceTests" --no-restore --verbosity minimal
```

Expected: fails because host page and nav link do not exist.

- [ ] **Step 3: Add Blazor host page**

Create `src/LightRAGNet.Web/Components/Pages/CacheManagement.razor`:

```razor
@page "/cache-management"
@using Microsoft.JSInterop
@implements IAsyncDisposable
@inject IConfiguration Configuration
@inject IJSRuntime JSRuntime

<PageTitle>Cache Management</PageTitle>

<link rel="stylesheet" href="cache-management/assets/cache-management.css" />

<div id="cache-management-root" data-api-base="@ApiBase"></div>

@code {
    private const string RootElementId = "cache-management-root";
    private IJSObjectReference? cacheManagementModule;
    private string ApiBase => Configuration["ApiBaseUrl"] ?? "http://localhost:5261";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        cacheManagementModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./cache-management/assets/cache-management.js");
        await cacheManagementModule.InvokeVoidAsync("mountCacheManagement", RootElementId, ApiBase);
    }

    public async ValueTask DisposeAsync()
    {
        if (cacheManagementModule is null)
        {
            return;
        }

        try
        {
            await cacheManagementModule.InvokeVoidAsync("unmountCacheManagement", RootElementId);
            await cacheManagementModule.DisposeAsync();
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

- [ ] **Step 4: Add navigation link**

Modify `src/LightRAGNet.Web/Components/Layout/NavMenu.razor`:

```razor
<MudNavMenu>
    <MudNavLink Href="" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Chat">RAG Chat</MudNavLink>
    <MudNavLink Href="markdown-documents" Icon="@Icons.Material.Filled.Description">Markdown Documents</MudNavLink>
    <MudNavLink Href="markdown-upload" Icon="@Icons.Material.Filled.Upload">Upload Document</MudNavLink>
    <MudNavLink Href="graph-view" Icon="@Icons.Material.Filled.AccountTree">Knowledge Graph</MudNavLink>
    <MudNavLink Href="cache-management" Icon="@Icons.Material.Filled.Storage">Cache Management</MudNavLink>
</MudNavMenu>
```

- [ ] **Step 5: Run Web tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~CacheManagementHostSourceTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet.Web\Components\Pages\CacheManagement.razor src\LightRAGNet.Web\Components\Layout\NavMenu.razor tests\LightRAGNet.Web.Tests\CacheManagementHostSourceTests.cs
git commit -m "feat: host cache management page"
```

---

### Task 9: End-To-End Verification And No-Old-API Gate

**Files:**
- No planned source changes unless verification exposes failures.

- [ ] **Step 1: Verify old runtime APIs are removed**

Run:

```powershell
rg -n "TryGetKeywordsAsync|SaveKeywordsAsync|TryGetQueryResponseAsync|SaveQueryResponseAsync|TryGetExtractAsync|SaveExtractAsync|TryGetSummaryAsync|SaveSummaryAsync" src tests
```

Expected: no output.

- [ ] **Step 2: Run targeted backend tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessingServiceTests|FullyQualifiedName~DescriptionMergerTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 3: Run targeted server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~CacheManagement|FullyQualifiedName~MarkdownDocumentsControllerTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Run targeted Web tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~CacheManagementHostSourceTests|FullyQualifiedName~GraphWorkbenchHostSourceTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Run frontend build**

Run:

```powershell
Push-Location src\LightRAGNet.Web\ClientApp; npm test; npm run build; Pop-Location
```

Expected: Vitest and Vite build pass.

- [ ] **Step 6: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: pass. If it fails because of a known unrelated pre-existing test, record exact failing test and rerun targeted feature tests.

- [ ] **Step 7: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 8: Commit final verification fixes if any**

If verification required fixes, inspect the changed files and stage the exact feature files that changed:

```powershell
git status --short
git add src\LightRAGNet src\LightRAGNet.Server src\LightRAGNet.Web tests\LightRAGNet.Tests tests\LightRAGNet.Server.Tests tests\LightRAGNet.Web.Tests
git commit -m "fix: stabilize cache management verification"
```

If no fixes were needed, do not create an empty commit.

---

### Task 10: Asset Gate And Closeout

**Files:**
- May create archive after implementation is complete:
  - `docs/superpowers/archives/2026-05/2026-05-24-cache-management-workbench-archives.md`
- May update:
  - `docs/superpowers/archives/INDEX.md`
- May create or update problem/inbox assets if reusable failure modes appear.

- [ ] **Step 1: Run related asset status**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_status.py . --topic "cache management workbench" --json
```

Expected: reports current spec/plan and any related problem assets.

- [ ] **Step 2: Run completion gate**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "cache management workbench" --json
```

Expected: either route to archive or report missing evidence to collect.

- [ ] **Step 3: Archive completed requirement**

If implementation is accepted and verified, create archive with:

```markdown
# Cache Management Workbench

- Date: `2026-05-24`
- Topic slug: `cache-management-workbench`
- Status: `Archived`
- Scope: `Feature`
- Tags: `cache-management`, `metrics`, `react-island`, `operations`

## Summary

LightRAGNet now exposes a Web Cache Management workbench backed by real cache read metrics and safe cache inventory.

## Delivered Scope

- Runtime cache access migrated to `GetOrCreate...`.
- Old `TryGet...` / `Save...` runtime APIs removed.
- Cache metrics store records read outcomes, factory duration and save/clear events.
- Cache Management API returns overview, family metrics, trend, insights, clear plan and safe entry samples.
- React workbench renders hit rate, saved calls, latency estimate, risky entries and clear plans.

## Verification Snapshot

- `rg -n "TryGetKeywordsAsync|SaveKeywordsAsync|TryGetQueryResponseAsync|SaveQueryResponseAsync|TryGetExtractAsync|SaveExtractAsync|TryGetSummaryAsync|SaveSummaryAsync" src tests` returned no matches.
- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~QueryCache|FullyQualifiedName~DocumentProcessingServiceTests|FullyQualifiedName~DescriptionMergerTests" --no-restore --verbosity minimal` passed.
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~CacheManagement|FullyQualifiedName~MarkdownDocumentsControllerTests" --no-restore --verbosity minimal` passed.
- `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "FullyQualifiedName~CacheManagementHostSourceTests|FullyQualifiedName~GraphWorkbenchHostSourceTests" --no-restore --verbosity minimal` passed.
- `npm test` and `npm run build` passed from `src\LightRAGNet.Web\ClientApp`.

## Source Documents

- Spec: [Cache Management Workbench Design](../../specs/2026-05-24-cache-management-workbench-design.md)
- Visual: [Cache Management UI Concept](../../visuals/cache-management-ui-concept.html)
- Plan: [Cache Management Workbench Implementation Plan](../../plans/2026-05-24-cache-management-workbench-implementation-plan.md)
```

Before writing the archive, confirm every command listed in the verification snapshot has actually passed in Task 9; if any command did not pass, record the real failing command and stop before archiving.

- [ ] **Step 4: Validate archive assets**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\archive-superpowers-feature\scripts\validate_archive_asset.py docs\superpowers\archives\2026-05\2026-05-24-cache-management-workbench-archives.md
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_indexes.py .
```

Expected: both pass.

- [ ] **Step 5: Commit archive**

```powershell
git add docs\superpowers\archives\2026-05\2026-05-24-cache-management-workbench-archives.md docs\superpowers\archives\INDEX.md
git commit -m "docs: archive cache management workbench"
```

---

## Self-Review

### Spec Coverage

- Web UI is covered by Tasks 7 and 8.
- Real hit rate metrics are covered by Tasks 1, 2, 3, 5 and 9.
- `GetOrCreate` migration and old API removal are covered by Tasks 3, 4 and 9.
- Clear plan and clear execution are covered by Tasks 5 and 6.
- Security and redaction are covered by Task 5 API tests and Task 7 UI safe rendering.
- Visual reference is used in Task 7 CSS/component structure.
- Verification and asset gate are covered by Tasks 9 and 10.

### Placeholder Scan

- No unresolved planning markers.
- No deferred implementation steps.
- The archive template in Task 10 contains one explicit replacement instruction for actual verification evidence; it is only executed after Task 9 produces the evidence.

### Type Consistency

- `CacheMetricOperation.Read` / `CacheReadOutcome.Hit` names match test and implementation snippets.
- `CacheValueResult<T>` properties match call-site migration snippets.
- Server DTO names match TypeScript DTO names by camelCase JSON output.
- Clear plan ids match UI and service tests: `stale-query-cache`, `summary-cache-review`, `all-llm-cache`.
