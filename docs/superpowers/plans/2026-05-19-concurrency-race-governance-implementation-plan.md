# Concurrency Race Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable concurrency boundaries for file persistence, task progress dispatch, UI notifications, page operation cancellation, and the LightRAG state pump so the recently observed race classes do not keep reappearing.

**Architecture:** Put cross-project file persistence helpers in `LightRAGNet.Core`, keep service/UI operation helpers in `LightRAGNet.Services.Utilities`, then migrate the highest-risk paths one layer at a time. Each migration starts with focused tests that reproduce the failure class or lock down the new boundary, followed by minimal implementation and a small commit.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, Blazor Server, SignalR client, existing `IKVStore`, existing task queue abstractions.

---

## Source Documents

- Spec: `docs/superpowers/specs/2026-05-19-concurrency-race-governance-design.md`
- Existing problem asset: `docs/superpowers/problems/2026-05/2026-05-19-markdown-documents-debounce-race-problem.md`
- Existing problem asset: `docs/superpowers/problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md`

## Alignment With Original Governance Approach

This plan follows the previously agreed approach: build small concurrency infrastructure first, then migrate high-risk call sites. The concrete mapping is:

- `AtomicFileWriter`: foundation helper in Core, used by `RagTaskStateStore` and `JsonKVStore`.
- `AsyncEventDispatcher<T>`: Channel-based event dispatcher with optional keyed coalescing, used by task progress and SignalR UI notifications.
- `AsyncOperationSlot`: current-operation CTS ownership, used by `AsyncDebouncer` and `RagChat`.
- `PerKeyAsyncLock`: keyed resource serialization helper, added as foundation infrastructure before broader task/document/workspace migrations.
- Concurrency regression tests: captured as reusable test patterns in helper tests and final verification scan.

The first recommended step, the current `RagTaskStateStore` short-lock fix, has already been committed before this plan. This plan starts from step 2: the small concurrency foundation, then targeted migrations.

## File Structure

- Create `src/LightRAGNet.Core/IO/AtomicFileWriteOptions.cs`
  - Owns retry count and retry delay configuration for atomic file replacement.
- Create `src/LightRAGNet.Core/IO/AtomicFileWriter.cs`
  - Writes UTF-8 text to a unique temp file and replaces the target with bounded retry on `IOException` and `UnauthorizedAccessException`.
  - Lives in Core because both `LightRAGNet` and `LightRAGNet.Storage` can reference Core without creating a circular dependency.
- Create `tests/LightRAGNet.Tests/Utilities/AtomicFileWriterTests.cs`
  - Covers unique temp cleanup, retry success, retry exhaustion, and cancellation.
- Modify `src/LightRAGNet/Services/TaskQueue/RagTaskStateStore.cs`
  - Removes duplicated retry logic and calls `AtomicFileWriter.WriteAllTextAsync`.
- Modify `src/LightRAGNet.Storage/JsonKVStore.cs`
  - Locks all reads and writes through the existing `_lock`.
  - Returns snapshots, not internal dictionary instances.
  - Persists through `AtomicFileWriter`.
- Create `tests/LightRAGNet.Tests/Storage/JsonKVStoreConcurrencyTests.cs`
  - Covers read snapshots, concurrent upsert/read/delete, and persistence failure propagation.
- Create `src/LightRAGNet/Services/Utilities/AsyncEventDispatcher.cs`
  - Uses `Channel<T>` to serialize event handling, supports optional keyed coalescing, `DrainAsync`, cancellation-aware disposal, and centralized exception logging.
- Create `tests/LightRAGNet.Tests/Utilities/AsyncEventDispatcherTests.cs`
  - Covers serial order, keyed coalescing, drain-before-terminal behavior, handler exception isolation, and dispose.
- Modify `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`
  - Replaces fire-and-forget progress updates with `AsyncEventDispatcher<TaskState>` and drains it before final completion/failure persistence.
- Modify `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`
  - Adds deterministic tests for progress ordering and no progress writes after terminal status.
- Modify `src/LightRAGNet.Web/Services/RagTaskNotificationService.cs`
  - Replaces fire-and-forget SignalR handler fan-out with a serialized dispatcher.
- Create `tests/LightRAGNet.Tests/Web/RagTaskNotificationServiceSourceTests.cs`
  - Source-level safety net for the Web service dispatch contract because the current core test project does not host Blazor components.
- Create `src/LightRAGNet/Services/Utilities/AsyncOperationSlot.cs`
  - Owns replace/cancel/dispose semantics for one active async operation.
- Create `src/LightRAGNet/Services/Utilities/PerKeyAsyncLock.cs`
  - Provides fine-grained serialization for taskId, documentId, and workspace resources without introducing a global lock.
- Create `tests/LightRAGNet.Tests/Utilities/PerKeyAsyncLockTests.cs`
  - Covers same-key serialization, different-key parallelism, cancellation, and key cleanup.
- Modify `src/LightRAGNet/Services/Utilities/AsyncDebouncer.cs`
  - Reuses `AsyncOperationSlot` or mirrors its semantics through the same public behavior.
- Create `tests/LightRAGNet.Tests/Utilities/AsyncOperationSlotTests.cs`
  - Covers replace cancellation, dispose cancellation, no start after dispose, and local token stability.
- Modify `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
  - Replaces shared `_queryCancellationTokenSource` access after awaits with a locally captured operation token.
- Modify `src/LightRAGNet/LightRAG.cs`
  - Adds a thread-safe state processor startup guard and subscriber-isolated state publishing for the existing state buffer.
- Create `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGStateProcessorTests.cs`
  - Covers concurrent insert/delete startup, subscriber exception isolation, and cross-document progress delivery.
- Create after implementation is accepted: `docs/superpowers/archives/2026-05/2026-05-19-concurrency-race-governance-archive.md`
  - Records final delivered scope and verification evidence.

## Task 1: Atomic File Writer Foundation

**Files:**
- Create: `src/LightRAGNet.Core/IO/AtomicFileWriteOptions.cs`
- Create: `src/LightRAGNet.Core/IO/AtomicFileWriter.cs`
- Test: `tests/LightRAGNet.Tests/Utilities/AtomicFileWriterTests.cs`

- [ ] **Step 1: Write failing atomic writer tests**

Create `tests/LightRAGNet.Tests/Utilities/AtomicFileWriterTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.IO;

namespace LightRAGNet.Tests.Utilities;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public async Task WriteAllTextAsync_WhenTargetMissing_CreatesFileAndRemovesTempFile()
    {
        using var directory = TempDirectory.Create();
        var target = Path.Combine(directory.Path, "state.json");

        await AtomicFileWriter.WriteAllTextAsync(target, """{"ok":true}""");

        (await File.ReadAllTextAsync(target)).Should().Be("""{"ok":true}""");
        Directory.GetFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenCancellationRequestedBeforeReplace_ThrowsAndCleansTempFile()
    {
        using var directory = TempDirectory.Create();
        var target = Path.Combine(directory.Path, "state.json");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => AtomicFileWriter.WriteAllTextAsync(target, "cancelled", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenReplaceFailsTemporarily_RetriesThenSucceeds()
    {
        using var directory = TempDirectory.Create();
        var target = Path.Combine(directory.Path, "state.json");
        var attempts = 0;

        await AtomicFileWriter.WriteAllTextAsync(
            target,
            "value",
            new AtomicFileWriteOptions(MaxReplaceAttempts: 3, RetryDelay: TimeSpan.FromMilliseconds(1)),
            replaceFile: (source, destination) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("temporary lock");
                }

                File.Move(source, destination, overwrite: true);
            });

        attempts.Should().Be(2);
        (await File.ReadAllTextAsync(target)).Should().Be("value");
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenReplaceKeepsFailing_ThrowsAndCleansTempFile()
    {
        using var directory = TempDirectory.Create();
        var target = Path.Combine(directory.Path, "state.json");

        var act = () => AtomicFileWriter.WriteAllTextAsync(
            target,
            "value",
            new AtomicFileWriteOptions(MaxReplaceAttempts: 2, RetryDelay: TimeSpan.FromMilliseconds(1)),
            replaceFile: (_, _) => throw new UnauthorizedAccessException("locked"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        Directory.GetFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        private TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the failing atomic writer tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~AtomicFileWriterTests
```

Expected: FAIL because `LightRAGNet.Core.IO.AtomicFileWriter` does not exist.

- [ ] **Step 3: Implement atomic writer options**

Create `src/LightRAGNet.Core/IO/AtomicFileWriteOptions.cs`:

```csharp
namespace LightRAGNet.Core.IO;

public sealed record AtomicFileWriteOptions(
    int MaxReplaceAttempts = 10,
    TimeSpan? RetryDelay = null)
{
    public TimeSpan EffectiveRetryDelay => RetryDelay ?? TimeSpan.FromMilliseconds(50);
}
```

- [ ] **Step 4: Implement atomic writer**

Create `src/LightRAGNet.Core/IO/AtomicFileWriter.cs`:

```csharp
using System.Text;

namespace LightRAGNet.Core.IO;

public static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(
        string targetPath,
        string content,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default,
        Action<string, string>? replaceFile = null)
    {
        options ??= new AtomicFileWriteOptions();
        replaceFile ??= static (source, destination) => File.Move(source, destination, overwrite: true);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            await ReplaceWithRetryAsync(tempPath, targetPath, options, replaceFile, cancellationToken);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static async Task ReplaceWithRetryAsync(
        string tempPath,
        string targetPath,
        AtomicFileWriteOptions options,
        Action<string, string> replaceFile,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.MaxReplaceAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                replaceFile(tempPath, targetPath);
                return;
            }
            catch (Exception ex) when (IsTransientReplaceException(ex) && attempt < options.MaxReplaceAttempts)
            {
                await Task.Delay(options.EffectiveRetryDelay, cancellationToken);
            }
        }
    }

    private static bool IsTransientReplaceException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup only. The caller already gets the write result.
        }
    }
}
```

- [ ] **Step 5: Run atomic writer tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~AtomicFileWriterTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet.Core/IO tests/LightRAGNet.Tests/Utilities/AtomicFileWriterTests.cs
git commit -m "feat: add atomic file writer"
```

## Task 2: Migrate JSON Persistence Stores

**Files:**
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskStateStore.cs`
- Modify: `src/LightRAGNet.Storage/JsonKVStore.cs`
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskStateStoreTests.cs`
- Test: `tests/LightRAGNet.Tests/Storage/JsonKVStoreConcurrencyTests.cs`

- [ ] **Step 1: Write failing JsonKVStore concurrency tests**

Create `tests/LightRAGNet.Tests/Storage/JsonKVStoreConcurrencyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.Storage;

public sealed class JsonKVStoreConcurrencyTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsSnapshot_NotInternalDictionary()
    {
        using var directory = TempDirectory.Create();
        var store = CreateStore(directory, "kv.json");
        await store.UpsertAsync(new()
        {
            ["doc-1"] = new Dictionary<string, object> { ["content"] = "original" }
        });

        var value = await store.GetByIdAsync("doc-1");
        value!["content"] = "mutated";

        var secondRead = await store.GetByIdAsync("doc-1");
        secondRead!["content"].Should().Be("original");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNestedSnapshot_NotInternalList()
    {
        using var directory = TempDirectory.Create();
        var store = CreateStore(directory, "kv.json");
        await store.UpsertAsync(new()
        {
            ["doc-1"] = new Dictionary<string, object>
            {
                ["content"] = "original",
                ["chunks_list"] = new List<object> { "chunk-a" }
            }
        });

        var value = await store.GetByIdAsync("doc-1");
        ((List<object>)value!["chunks_list"]).Add("chunk-b");

        var secondRead = await store.GetByIdAsync("doc-1");
        secondRead!["chunks_list"].Should().BeAssignableTo<List<object>>()
            .Which.Should().Equal("chunk-a");
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsSnapshots_NotInternalDictionaries()
    {
        using var directory = TempDirectory.Create();
        var store = CreateStore(directory, "kv.json");
        await store.UpsertAsync(new()
        {
            ["doc-1"] = new Dictionary<string, object> { ["content"] = "one" },
            ["doc-2"] = new Dictionary<string, object> { ["content"] = "two" }
        });

        var values = await store.GetByIdsAsync(["doc-1", "doc-2"]);
        values[0]["content"] = "mutated";

        var secondRead = await store.GetByIdAsync("doc-1");
        secondRead!["content"].Should().Be("one");
    }

    [Fact]
    public async Task ConcurrentReadWriteDelete_DoesNotThrow()
    {
        using var directory = TempDirectory.Create();
        var store = CreateStore(directory, "kv.json");
        var failures = new List<Exception>();

        var workers = Enumerable.Range(0, 30).Select(async index =>
        {
            try
            {
                var id = $"doc-{index % 5}";
                await store.UpsertAsync(new()
                {
                    [id] = new Dictionary<string, object> { ["content"] = index.ToString() }
                });
                _ = await store.GetByIdAsync(id);
                await store.FilterKeysAsync([id, $"new-{index}"]);
                if (index % 3 == 0)
                {
                    await store.DeleteAsync([id]);
                }
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        });

        await Task.WhenAll(workers);

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDoneCallbackAsync_PersistsThroughAtomicWriter()
    {
        using var directory = TempDirectory.Create();
        var filePath = Path.Combine(directory.Path, "kv.json");
        var store = new JsonKVStore(filePath, NullLogger<JsonKVStore>.Instance);
        await store.UpsertAsync(new()
        {
            ["doc-1"] = new Dictionary<string, object> { ["content"] = "persisted" }
        });

        await store.IndexDoneCallbackAsync();

        File.ReadAllText(filePath).Should().Contain("persisted");
        Directory.GetFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDoneCallbackAsync_WhenPersistenceFails_ThrowsToCaller()
    {
        using var directory = TempDirectory.Create();
        var blockedDirectory = Path.Combine(directory.Path, "blocked");
        File.WriteAllText(blockedDirectory, "this path is a file");
        var store = new JsonKVStore(
            Path.Combine(blockedDirectory, "kv.json"),
            NullLogger<JsonKVStore>.Instance);
        await store.UpsertAsync(new()
        {
            ["doc-1"] = new Dictionary<string, object> { ["content"] = "persisted" }
        });

        var act = () => store.IndexDoneCallbackAsync();

        await act.Should().ThrowAsync<Exception>()
            .Where(ex => ex is IOException or UnauthorizedAccessException);
    }

    private static JsonKVStore CreateStore(TempDirectory directory, string fileName)
    {
        return new JsonKVStore(Path.Combine(directory.Path, fileName), NullLogger<JsonKVStore>.Instance);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        private TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run failing JsonKVStore concurrency tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~JsonKVStoreConcurrencyTests
```

Expected: FAIL because `GetByIdAsync` and `GetByIdsAsync` return internal dictionary instances.

- [ ] **Step 3: Migrate RagTaskStateStore to AtomicFileWriter**

Modify `src/LightRAGNet/Services/TaskQueue/RagTaskStateStore.cs`.

Add using:

```csharp
using LightRAGNet.Core.IO;
```

Replace `SaveToFileAsync` with:

```csharp
private async Task SaveToFileAsync(CancellationToken cancellationToken)
{
    try
    {
        var tasks = _tasksCache.Values.ToList();
        var data = new TasksFileData
        {
            Version = "1.0",
            LastUpdated = DateTime.UtcNow,
            Tasks = tasks
        };

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(
            _tasksFilePath,
            json,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Task state saved to file, task count: {Count}", tasks.Count);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to save task state to file");
        throw;
    }
}
```

Remove these members from `RagTaskStateStore`:

```csharp
private const int MaxFileReplaceAttempts = 10;
private static readonly TimeSpan FileReplaceRetryDelay = TimeSpan.FromMilliseconds(50);
private async Task ReplaceTaskFileWithRetryAsync(string tempPath, CancellationToken cancellationToken) { ... }
private static bool IsTransientFileReplaceException(Exception exception) { ... }
private void TryDeleteTempFile(string tempPath) { ... }
```

- [ ] **Step 4: Migrate JsonKVStore to locked snapshots and atomic save**

Modify `src/LightRAGNet.Storage/JsonKVStore.cs`.

Add using:

```csharp
using LightRAGNet.Core.IO;
```

Replace read methods with locked snapshot versions:

```csharp
public async Task<Dictionary<string, object>?> GetByIdAsync(
    string id,
    CancellationToken cancellationToken = default)
{
    await _lock.WaitAsync(cancellationToken);
    try
    {
        return _data.TryGetValue(id, out var value)
            ? CloneRecord(value)
            : null;
    }
    finally
    {
        _lock.Release();
    }
}

public async Task<List<Dictionary<string, object>>> GetByIdsAsync(
    IEnumerable<string> ids,
    CancellationToken cancellationToken = default)
{
    await _lock.WaitAsync(cancellationToken);
    try
    {
        return ids
            .Where(_data.ContainsKey)
            .Select(id => CloneRecord(_data[id]))
            .ToList();
    }
    finally
    {
        _lock.Release();
    }
}

public async Task<HashSet<string>> FilterKeysAsync(
    HashSet<string> keys,
    CancellationToken cancellationToken = default)
{
    await _lock.WaitAsync(cancellationToken);
    try
    {
        var existing = _data.Keys.ToHashSet();
        return keys.Where(k => !existing.Contains(k)).ToHashSet();
    }
    finally
    {
        _lock.Release();
    }
}

public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
{
    await _lock.WaitAsync(cancellationToken);
    try
    {
        return _data.Count == 0;
    }
    finally
    {
        _lock.Release();
    }
}

private static Dictionary<string, object> CloneRecord(Dictionary<string, object> source)
{
    return source.ToDictionary(
        pair => pair.Key,
        pair => CloneValue(pair.Value),
        StringComparer.Ordinal);
}

private static object CloneValue(object value)
{
    return value switch
    {
        Dictionary<string, object> dictionary => CloneRecord(dictionary),
        IReadOnlyDictionary<string, object> dictionary => dictionary.ToDictionary(
            pair => pair.Key,
            pair => CloneValue(pair.Value),
            StringComparer.Ordinal),
        List<object> list => list.Select(CloneValue).ToList(),
        object[] array => array.Select(CloneValue).ToList(),
        JsonElement json => JsonSerializer.Deserialize<object>(json.GetRawText()) ?? json.ToString(),
        _ => value
    };
}
```

Replace `SaveData` with:

```csharp
private async Task SaveDataAsync(CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await AtomicFileWriter.WriteAllTextAsync(_filePath, json, cancellationToken: cancellationToken);
}
```

Update callers:

```csharp
await SaveDataAsync(cancellationToken);
```

Keep save exceptions visible to callers:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to save data to {FilePath}", _filePath);
    throw;
}
```

- [ ] **Step 5: Run persistence tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RagTaskStateStoreTests|FullyQualifiedName~JsonKVStoreConcurrencyTests|FullyQualifiedName~DocumentDeletionStorageIntegrationTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet/Services/TaskQueue/RagTaskStateStore.cs src/LightRAGNet.Storage/JsonKVStore.cs tests/LightRAGNet.Tests/Storage/JsonKVStoreConcurrencyTests.cs
git commit -m "fix: centralize json file persistence"
```

## Task 3: Async Event Dispatcher Foundation

**Files:**
- Create: `src/LightRAGNet/Services/Utilities/AsyncEventDispatcher.cs`
- Test: `tests/LightRAGNet.Tests/Utilities/AsyncEventDispatcherTests.cs`

- [ ] **Step 1: Write failing dispatcher tests**

Create `tests/LightRAGNet.Tests/Utilities/AsyncEventDispatcherTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.Utilities;

public sealed class AsyncEventDispatcherTests
{
    [Fact]
    public async Task EnqueueAsync_ProcessesEventsSerially()
    {
        var seen = new List<int>();
        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (value, _) =>
            {
                await Task.Delay(value == 1 ? 30 : 1);
                seen.Add(value);
            },
            NullLogger<AsyncEventDispatcher<int>>.Instance);

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueAsync(2);
        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 2);
    }

    [Fact]
    public async Task EnqueueLatestAsync_WhenKeyMatches_ProcessesOnlyLatestPendingValue()
    {
        var seen = new List<int>();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (value, token) =>
            {
                seen.Add(value);
                if (value == 1)
                {
                    await releaseFirst.Task.WaitAsync(token);
                }
            },
            NullLogger<AsyncEventDispatcher<int>>.Instance,
            keySelector: value => "task-1");

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueLatestAsync(2);
        await dispatcher.EnqueueLatestAsync(3);
        releaseFirst.SetResult();
        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 3);
    }

    [Fact]
    public async Task DrainAsync_WaitsForQueuedAndCoalescedEvents()
    {
        var seen = new List<string>();
        await using var dispatcher = new AsyncEventDispatcher<string>(
            async (value, _) =>
            {
                await Task.Delay(10);
                seen.Add(value);
            },
            NullLogger<AsyncEventDispatcher<string>>.Instance,
            keySelector: value => value.Split(':')[0]);

        await dispatcher.EnqueueAsync("a:1");
        await dispatcher.EnqueueLatestAsync("b:1");
        await dispatcher.EnqueueLatestAsync("b:2");
        await dispatcher.DrainAsync();

        seen.Should().Contain("a:1");
        seen.Should().Contain("b:2");
        seen.Should().NotContain("b:1");
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerThrows_DoesNotStopLaterEvents()
    {
        var seen = new List<int>();
        await using var dispatcher = new AsyncEventDispatcher<int>(
            (value, _) =>
            {
                if (value == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                seen.Add(value);
                return Task.CompletedTask;
            },
            NullLogger<AsyncEventDispatcher<int>>.Instance);

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueAsync(2);
        await dispatcher.DrainAsync();

        seen.Should().Equal(2);
    }
}
```

- [ ] **Step 2: Run the failing dispatcher tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~AsyncEventDispatcherTests
```

Expected: FAIL because `AsyncEventDispatcher<T>` does not exist.

- [ ] **Step 3: Implement Channel-based dispatcher**

Create `src/LightRAGNet/Services/Utilities/AsyncEventDispatcher.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.Utilities;

public sealed class AsyncEventDispatcher<T> : IAsyncDisposable
{
    private readonly Channel<DispatchItem> _channel;
    private readonly Func<T, CancellationToken, Task> _handler;
    private readonly ILogger _logger;
    private readonly Func<T, string?>? _keySelector;
    private readonly ConcurrentDictionary<string, long> _latestByKey = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _readerTask;
    private readonly object _drainGate = new();
    private TaskCompletionSource _drainSignal = CompletedDrainSignal();
    private long _nextSequence;
    private long _pendingCount;
    private bool _disposed;

    public AsyncEventDispatcher(
        Func<T, CancellationToken, Task> handler,
        ILogger logger,
        Func<T, string?>? keySelector = null,
        CancellationToken cancellationToken = default)
    {
        _handler = handler;
        _logger = logger;
        _keySelector = keySelector;
        _disposeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _channel = Channel.CreateUnbounded<DispatchItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _readerTask = Task.Run(ProcessAsync);
    }

    public Task EnqueueAsync(T value, CancellationToken cancellationToken = default)
    {
        return EnqueueCoreAsync(value, coalesceByKey: false, cancellationToken);
    }

    public Task EnqueueLatestAsync(T value, CancellationToken cancellationToken = default)
    {
        return EnqueueCoreAsync(value, coalesceByKey: true, cancellationToken);
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        Task drainTask;
        lock (_drainGate)
        {
            drainTask = _drainSignal.Task;
        }

        await drainTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _disposeCts.Cancel();

        try
        {
            await _readerTask;
        }
        catch
        {
            // Handler failures are logged in the reader loop.
        }
        finally
        {
            Interlocked.Exchange(ref _pendingCount, 0);
            lock (_drainGate)
            {
                _drainSignal.TrySetResult();
            }
            _disposeCts.Dispose();
        }
    }

    private Task EnqueueCoreAsync(T value, bool coalesceByKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sequence = Interlocked.Increment(ref _nextSequence);
        string? key = null;
        if (coalesceByKey)
        {
            key = _keySelector?.Invoke(value);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _latestByKey[key] = sequence;
            }
        }

        MarkPending();
        if (!_channel.Writer.TryWrite(new DispatchItem(value, key, sequence)))
        {
            MarkCompleted();
            throw new ObjectDisposedException(nameof(AsyncEventDispatcher<T>));
        }

        return Task.CompletedTask;
    }

    private async Task ProcessAsync()
    {
        var token = _disposeCts.Token;
        await foreach (var item in _channel.Reader.ReadAllAsync(token))
        {
            try
            {
                if (ShouldSkipCoalescedItem(item))
                {
                    continue;
                }

                await _handler(item.Value, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Async event dispatcher handler failed for value {Value}", item.Value);
            }
            finally
            {
                MarkCompleted();
            }
        }
    }

    private bool ShouldSkipCoalescedItem(DispatchItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Key))
        {
            return false;
        }

        if (!_latestByKey.TryGetValue(item.Key, out var latestSequence))
        {
            return false;
        }

        if (latestSequence != item.Sequence)
        {
            return true;
        }

        _latestByKey.TryRemove(item.Key, out _);
        return false;
    }

    private void MarkPending()
    {
        if (Interlocked.Increment(ref _pendingCount) == 1)
        {
            lock (_drainGate)
            {
                if (_drainSignal.Task.IsCompleted)
                {
                    _drainSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }
    }

    private void MarkCompleted()
    {
        if (Interlocked.Decrement(ref _pendingCount) == 0)
        {
            CompleteDrainIfEmpty();
        }
    }

    private void CompleteDrainIfEmpty()
    {
        if (Interlocked.Read(ref _pendingCount) != 0)
        {
            return;
        }

        lock (_drainGate)
        {
            _drainSignal.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedDrainSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private sealed record DispatchItem(T Value, string? Key, long Sequence);
}
```

- [ ] **Step 4: Run dispatcher tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~AsyncEventDispatcherTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/Services/Utilities/AsyncEventDispatcher.cs tests/LightRAGNet.Tests/Utilities/AsyncEventDispatcherTests.cs
git commit -m "feat: add async event dispatcher"
```
## Task 4: Serialize Task Progress Updates

**Files:**
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`
- Modify: `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`

- [ ] **Step 1: Add a deterministic progress-ordering test**

Add this test to `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`:

```csharp
[Fact]
public async Task ProcessTaskAsync_WhenProgressArrivesQuickly_SerializesProgressBeforeCompleted()
{
    var queue = new RecordingRagTaskQueueService(delayProgressWrites: true);
    var task = new RagTask
    {
        TaskId = "task-1",
        Content = "alpha beta gamma",
        RagDocumentId = "doc-1",
        FilePath = "docs/a.md",
        Status = RagTaskStatus.Pending
    };
    queue.Enqueue(task);
    using var host = CreateProcessorHost(queue, progressBurst: true);

    await host.Processor.StartAsync(CancellationToken.None);
    await queue.WaitForStatusAsync(RagTaskStatus.Completed, TimeSpan.FromSeconds(5));
    await host.Processor.StopAsync(CancellationToken.None);

    queue.ProgressWrites.Should().OnlyContain(write => write.TaskId == "task-1");
    queue.Events.Last().Should().Be("status:Completed");
    queue.Events.Should().NotContainInOrder("status:Completed", "progress:ProcessingChunks");
}
```

Add this nested fake queue shape to the same test file. The omitted interface members should return neutral values and should not record events:

```csharp
private sealed class RecordingRagTaskQueueService(bool delayProgressWrites) : IRagTaskQueueService
{
    private readonly Queue<RagTask> _pending = new();
    private readonly TaskCompletionSource<RagTaskStatus> _completedStatus =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Events { get; } = [];
    public List<(string TaskId, TaskStage Stage, int? Progress)> ProgressWrites { get; } = [];

    public void Enqueue(RagTask task)
    {
        _pending.Enqueue(task);
    }

    public Task WaitForStatusAsync(RagTaskStatus status, TimeSpan timeout)
    {
        return _completedStatus.Task.WaitAsync(timeout);
    }

    public Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_pending.Count == 0 ? null : _pending.Dequeue());
    }

    public async Task UpdateTaskProgressAsync(
        string taskId,
        TaskStage? stage,
        int? progress,
        CancellationToken cancellationToken = default)
    {
        if (delayProgressWrites)
        {
            await Task.Yield();
        }

        ProgressWrites.Add((taskId, stage!.Value, progress));
        Events.Add($"progress:{stage}");
    }

    public Task UpdateTaskStatusAsync(
        string taskId,
        RagTaskStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"status:{status}");
        if (status == RagTaskStatus.Completed)
        {
            _completedStatus.TrySetResult(status);
        }

        return Task.CompletedTask;
    }
}
```

Add `ProcessorHost CreateProcessorHost(RecordingRagTaskQueueService queue, bool progressBurst)` beside the existing `CreateLightRag` helper. It should create a `ServiceCollection`, register a configured `LightRAG` instance from the existing in-memory construction pattern, register `IRagTaskQueueService` with `queue`, register `RagTaskCancellationRegistry`, and return both the built provider and `RagTaskProcessorService` so the test can call `StartAsync` / `StopAsync`.

- [ ] **Step 2: Run the failing processor test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RagTaskProcessorServiceTests&FullyQualifiedName~SerializesProgressBeforeCompleted"
```

Expected: FAIL because current progress handler uses unmanaged fire-and-forget writes.

- [ ] **Step 3: Replace fire-and-forget progress writes**

Modify `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`.

Add using:

```csharp
using LightRAGNet.Services.Utilities;
```

Inside `ProcessTaskAsync`, before subscribing to `TaskStateChanged`, create:

```csharp
await using var progressQueue = new AsyncEventDispatcher<TaskState>(
    async (state, token) =>
    {
        if (task.Status is RagTaskStatus.Completed or RagTaskStatus.Failed or RagTaskStatus.Cancelled)
        {
            logger.LogDebug(
                "Discarding late progress update for terminal task {TaskId}: Stage={Stage}, Current={Current}, Total={Total}",
                task.TaskId,
                state.Stage,
                state.Current,
                state.Total);
            return;
        }

        var progress = state.Total > 0
            ? (int)(state.Current * 100.0 / state.Total)
            : (int?)null;

        await taskQueue.UpdateTaskProgressAsync(
            task.TaskId,
            state.Stage,
            progress,
            token);
    },
    logger,
    keySelector: _ => task.TaskId);
```

Replace both `_ = taskQueue.UpdateTaskProgressAsync(...)` branches in the event handler with:

```csharp
try
{
    _ = progressQueue.EnqueueLatestAsync(state, CancellationToken.None);
}
catch (Exception ex)
{
    logger.LogWarning(
        ex,
        "Failed to enqueue progress update for task {TaskId}: Stage={Stage}, Current={Current}, Total={Total}",
        task.TaskId,
        state.Stage,
        state.Current,
        state.Total);
}
```

For successful insert/delete completion, drain before mutating the task into a terminal status:

```csharp
await progressQueue.DrainAsync(CancellationToken.None);
task.Status = RagTaskStatus.Completed;
task.CompletedAt = DateTime.UtcNow;
task.CurrentStage = TaskStage.Completed;
await taskQueue.UpdateTaskStatusAsync(task.TaskId, RagTaskStatus.Completed, cancellationToken: taskCancellationToken);
```

For failure, drain before marking `Failed` so progress already emitted by the pipeline is not discarded as late terminal progress:

```csharp
await progressQueue.DrainAsync(CancellationToken.None);
task.Status = RagTaskStatus.Failed;
task.ErrorMessage = ex.Message;
task.CompletedAt = DateTime.UtcNow;
await taskQueue.UpdateTaskStatusAsync(
    task.TaskId,
    RagTaskStatus.Failed,
    ex.Message,
    taskCancellationToken);
```

For service-shutdown reset, also drain before setting the queue-visible status back to `Pending`:

```csharp
await progressQueue.DrainAsync(CancellationToken.None);
await taskQueue.UpdateTaskStatusAsync(
    task.TaskId,
    RagTaskStatus.Pending,
    null,
    CancellationToken.None);
```

In `finally`, keep event unsubscribe before disposal:

```csharp
lightRAG.TaskStateChanged -= progressHandler;
await progressQueue.DrainAsync(CancellationToken.None);
cancellationRegistry.CompleteProcessingTask(task.TaskId);
```

- [ ] **Step 4: Run processor tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagTaskProcessorServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs
git commit -m "fix: serialize rag task progress updates"
```

## Task 5: Serialize SignalR UI Notifications

**Files:**
- Modify: `src/LightRAGNet.Web/Services/RagTaskNotificationService.cs`
- Test: `tests/LightRAGNet.Tests/Web/RagTaskNotificationServiceSourceTests.cs`

- [ ] **Step 1: Add source safety tests for notification dispatch**

Create `tests/LightRAGNet.Tests/Web/RagTaskNotificationServiceSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class RagTaskNotificationServiceSourceTests
{
    [Fact]
    public void Service_DoesNotFireAndForgetTaskStatusHandlerDispatch()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LightRAGNet.Web",
            "Services",
            "RagTaskNotificationService.cs"));

        source.Should().NotContain("_ = NotifyTaskStatusHandlersAsync(update);");
        source.Should().Contain("EnqueueLatestAsync(update");
    }

    [Fact]
    public void Service_DoesNotFireAndForgetDataClearedHandlerDispatch()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LightRAGNet.Web",
            "Services",
            "RagTaskNotificationService.cs"));

        source.Should().NotContain("_ = NotifyDataClearedHandlersAsync();");
        source.Should().Contain("EnqueueAsync(NotificationDispatchKey.DataCleared");
        source.Should().Contain("DrainAsync(token)");
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

        throw new InvalidOperationException("Repository root not found.");
    }
}
```

- [ ] **Step 2: Run failing notification source tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagTaskNotificationServiceSourceTests
```

Expected: FAIL because current service uses fire-and-forget handler dispatch.

- [ ] **Step 3: Add dispatcher fields**

Modify `src/LightRAGNet.Web/Services/RagTaskNotificationService.cs`.

Add using:

```csharp
using LightRAGNet.Services.Utilities;
```

Change the class declaration so DI can dispose the dispatchers and SignalR connection:

```csharp
public class RagTaskNotificationService(
    ILogger<RagTaskNotificationService> logger,
    IConfiguration configuration)
    : IAsyncDisposable
```

Add fields:

```csharp
private readonly AsyncEventDispatcher<TaskStatusUpdate> _taskStatusDispatchQueue =
    new(
        async (update, token) => await NotifyTaskStatusHandlersAsync(update, token),
        logger,
        keySelector: update => update.TaskId);

private readonly AsyncEventDispatcher<NotificationDispatchKey> _systemDispatchQueue =
    new(
        async (key, token) =>
        {
            if (key == NotificationDispatchKey.DataCleared)
            {
                await _taskStatusDispatchQueue.DrainAsync(token);
                await NotifyDataClearedHandlersAsync(token);
            }
        },
        logger);

private enum NotificationDispatchKey
{
    DataCleared
}
```

- [ ] **Step 4: Replace SignalR callbacks**

Replace the task status callback body with:

```csharp
_ = _taskStatusDispatchQueue.EnqueueLatestAsync(update);
```

Replace the data cleared callback body with:

```csharp
_ = _systemDispatchQueue.EnqueueAsync(NotificationDispatchKey.DataCleared);
```

Change handler methods to accept cancellation tokens:

```csharp
private async Task NotifyTaskStatusHandlersAsync(TaskStatusUpdate update, CancellationToken cancellationToken)
{
    try
    {
        var handlers = TaskStatusUpdated?.GetInvocationList()
            .Cast<Func<object, TaskStatusUpdate, Task>>()
            .ToArray();

        if (handlers is null || handlers.Length == 0)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler(this, update);
        }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "Error calling task status update event handlers: TaskId={TaskId}", update.TaskId);
    }
}
```

Apply the same sequential pattern to `NotifyDataClearedHandlersAsync`.

Add service disposal:

```csharp
public async ValueTask DisposeAsync()
{
    if (_hubConnection is not null)
    {
        await _hubConnection.DisposeAsync();
    }

    await _systemDispatchQueue.DisposeAsync();
    await _taskStatusDispatchQueue.DisposeAsync();
    _initLock.Dispose();
}
```

- [ ] **Step 5: Run notification and build checks**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagTaskNotificationServiceSourceTests
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj
```

Expected: source tests PASS and Web project build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet.Web/Services/RagTaskNotificationService.cs tests/LightRAGNet.Tests/Web/RagTaskNotificationServiceSourceTests.cs
git commit -m "fix: serialize frontend task notifications"
```

## Task 6: Operation Slot And RagChat Cancellation

**Files:**
- Create: `src/LightRAGNet/Services/Utilities/AsyncOperationSlot.cs`
- Modify: `src/LightRAGNet/Services/Utilities/AsyncDebouncer.cs`
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
- Test: `tests/LightRAGNet.Tests/Utilities/AsyncOperationSlotTests.cs`
- Test: `tests/LightRAGNet.Tests/Utilities/AsyncDebouncerTests.cs`

- [ ] **Step 1: Write failing operation slot tests**

Create `tests/LightRAGNet.Tests/Utilities/AsyncOperationSlotTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.Utilities;

namespace LightRAGNet.Tests.Utilities;

public sealed class AsyncOperationSlotTests
{
    [Fact]
    public async Task StartNewAsync_CancelsPreviousOperation()
    {
        await using var slot = new AsyncOperationSlot();
        var first = await slot.StartNewAsync();

        var second = await slot.StartNewAsync();

        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Complete_DisposesOnlyMatchingOperation()
    {
        await using var slot = new AsyncOperationSlot();
        var first = await slot.StartNewAsync();
        var second = await slot.StartNewAsync();

        first.Complete();
        second.Token.IsCancellationRequested.Should().BeFalse();

        second.Complete();
        (await slot.TryGetCurrentTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_CancelsCurrentOperationAndRejectsNewOperation()
    {
        var slot = new AsyncOperationSlot();
        var operation = await slot.StartNewAsync();

        await slot.DisposeAsync();

        operation.Token.IsCancellationRequested.Should().BeTrue();
        var act = () => slot.StartNewAsync().AsTask();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
```

- [ ] **Step 2: Run failing operation slot tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~AsyncOperationSlotTests
```

Expected: FAIL because `AsyncOperationSlot` does not exist.

- [ ] **Step 3: Implement AsyncOperationSlot**

Create `src/LightRAGNet/Services/Utilities/AsyncOperationSlot.cs`:

```csharp
namespace LightRAGNet.Services.Utilities;

public sealed class AsyncOperationSlot : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCts;
    private bool _disposed;

    public async ValueTask<Lease> StartNewAsync()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _currentCts;
            current = new CancellationTokenSource();
            _currentCts = current;
        }

        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        return new Lease(this, current, current.Token);
    }

    public ValueTask<CancellationToken?> TryGetCurrentTokenAsync()
    {
        lock (_gate)
        {
            return ValueTask.FromResult<CancellationToken?>(_currentCts?.Token);
        }
    }

    private void Complete(CancellationTokenSource cts)
    {
        var shouldDispose = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentCts, cts))
            {
                _currentCts = null;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _currentCts;
            _currentCts = null;
        }

        if (current is not null)
        {
            await current.CancelAsync();
            current.Dispose();
        }
    }

    public sealed class Lease : IDisposable
    {
        private readonly AsyncOperationSlot _owner;
        private readonly CancellationTokenSource _cts;
        private bool _completed;

        internal Lease(AsyncOperationSlot owner, CancellationTokenSource cts, CancellationToken token)
        {
            _owner = owner;
            _cts = cts;
            Token = token;
        }

        public CancellationToken Token { get; }

        public void Complete()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _owner.Complete(_cts);
        }

        public void Dispose()
        {
            Complete();
        }
    }
}
```

- [ ] **Step 4: Update AsyncDebouncer to use operation slot**

Modify `src/LightRAGNet/Services/Utilities/AsyncDebouncer.cs` so it owns:

```csharp
private readonly AsyncOperationSlot _slot = new();
```

Replace the manual CTS field logic in `DebounceAsync` with:

```csharp
AsyncOperationSlot.Lease lease;
try
{
    lease = await _slot.StartNewAsync();
}
catch (ObjectDisposedException)
{
    return;
}

using (lease)
{
    try
    {
        await Task.Delay(delay, lease.Token);
        await action(lease.Token);
    }
    catch (OperationCanceledException) when (lease.Token.IsCancellationRequested)
    {
    }
}
```

Replace `DisposeAsync` with:

```csharp
public ValueTask DisposeAsync()
{
    return _slot.DisposeAsync();
}
```

- [ ] **Step 5: Update RagChat to capture operation token locally**

Modify `src/LightRAGNet.Web/Components/Pages/RagChat.razor`.

Replace field:

```csharp
private CancellationTokenSource? _queryCancellationTokenSource;
```

with:

```csharp
private readonly AsyncOperationSlot _queryOperation = new();
```

Add using:

```razor
@using LightRAGNet.Services.Utilities
```

In `SendMessageAsync`, replace shared CTS setup with:

```csharp
AsyncOperationSlot.Lease queryLease;
try
{
    queryLease = await _queryOperation.StartNewAsync();
}
catch (ObjectDisposedException)
{
    return;
}
```

Wrap the query call:

```csharp
using (queryLease)
{
    await ApiClient.QueryRagAsync(
        userMessage,
        async chunk =>
        {
            if (string.IsNullOrEmpty(chunk) || queryLease.Token.IsCancellationRequested)
            {
                return;
            }

            assistantMessage.Text += chunk;
            await InvokeAsync(async () =>
            {
                await ScrollToBottomAsync();
                StateHasChanged();
            });
        },
        queryLease.Token);
}
```

Remove shared CTS disposal from `finally`. In `DisposeAsync`, replace CTS cleanup with:

```csharp
await _queryOperation.DisposeAsync();
```

- [ ] **Step 6: Run operation tests and Web build**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~AsyncOperationSlotTests|FullyQualifiedName~AsyncDebouncerTests"
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj
```

Expected: tests PASS and Web project build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet/Services/Utilities/AsyncOperationSlot.cs src/LightRAGNet/Services/Utilities/AsyncDebouncer.cs src/LightRAGNet.Web/Components/Pages/RagChat.razor tests/LightRAGNet.Tests/Utilities/AsyncOperationSlotTests.cs
git commit -m "fix: centralize page operation cancellation"
```

## Task 7: Per-Key Async Lock Foundation

**Files:**
- Create: `src/LightRAGNet/Services/Utilities/PerKeyAsyncLock.cs`
- Test: `tests/LightRAGNet.Tests/Utilities/PerKeyAsyncLockTests.cs`

- [ ] **Step 1: Write failing keyed lock tests**

Create `tests/LightRAGNet.Tests/Utilities/PerKeyAsyncLockTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.Utilities;

namespace LightRAGNet.Tests.Utilities;

public sealed class PerKeyAsyncLockTests
{
    [Fact]
    public async Task LockAsync_SameKey_SerializesWork()
    {
        var locker = new PerKeyAsyncLock<string>();
        var inside = 0;
        var maxInside = 0;

        var workers = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var lease = await locker.LockAsync("doc-1");
            var current = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, current);
            await Task.Delay(5);
            Interlocked.Decrement(ref inside);
        });

        await Task.WhenAll(workers);

        maxInside.Should().Be(1);
    }

    [Fact]
    public async Task LockAsync_DifferentKeys_CanRunInParallel()
    {
        var locker = new PerKeyAsyncLock<string>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = Task.Run(async () =>
        {
            await using var lease = await locker.LockAsync("doc-a");
            firstEntered.SetResult();
            await releaseFirst.Task;
        });

        await firstEntered.Task;
        await using (await locker.LockAsync("doc-b"))
        {
            secondEntered = true;
        }

        releaseFirst.SetResult();
        await first;
        secondEntered.Should().BeTrue();
    }

    [Fact]
    public async Task LockAsync_WhenWaitingCancellationRequested_Throws()
    {
        var locker = new PerKeyAsyncLock<string>();
        await using var first = await locker.LockAsync("doc-1");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => locker.LockAsync("doc-1", cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run failing keyed lock tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~PerKeyAsyncLockTests
```

Expected: FAIL because `PerKeyAsyncLock<TKey>` does not exist.

- [ ] **Step 3: Implement PerKeyAsyncLock**

Create `src/LightRAGNet/Services/Utilities/PerKeyAsyncLock.cs`:

```csharp
using System.Collections.Concurrent;

namespace LightRAGNet.Services.Utilities;

public sealed class PerKeyAsyncLock<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, RefCountedSemaphore> _locks = new();

    public async ValueTask<Lease> LockAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var entry = _locks.AddOrUpdate(
            key,
            _ => new RefCountedSemaphore(),
            (_, existing) =>
            {
                existing.AddRef();
                return existing;
            });

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Lease(this, key, entry);
        }
        catch
        {
            ReleaseRef(key, entry);
            throw;
        }
    }

    private void Release(TKey key, RefCountedSemaphore entry)
    {
        entry.Semaphore.Release();
        ReleaseRef(key, entry);
    }

    private void ReleaseRef(TKey key, RefCountedSemaphore entry)
    {
        if (entry.ReleaseRef() == 0)
        {
            if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _locks.TryRemove(key, out _);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class RefCountedSemaphore
    {
        private int _refCount = 1;
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public void AddRef() => Interlocked.Increment(ref _refCount);
        public int ReleaseRef() => Interlocked.Decrement(ref _refCount);
    }

    public readonly struct Lease : IAsyncDisposable
    {
        private readonly PerKeyAsyncLock<TKey> _owner;
        private readonly TKey _key;
        private readonly RefCountedSemaphore _entry;

        internal Lease(PerKeyAsyncLock<TKey> owner, TKey key, RefCountedSemaphore entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            _owner.Release(_key, _entry);
            return ValueTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 4: Run keyed lock tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~PerKeyAsyncLockTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/Services/Utilities/PerKeyAsyncLock.cs tests/LightRAGNet.Tests/Utilities/PerKeyAsyncLockTests.cs
git commit -m "feat: add per-key async lock"
```
## Task 8: Guard LightRAG State Processor Dispatch

**Files:**
- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGStateProcessorTests.cs`

- [ ] **Step 1: Add state processor test**

Create `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGStateProcessorTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Models;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class LightRAGStateProcessorTests
{
    [Fact]
    public async Task InsertAsync_WhenStartedConcurrently_DeliversProgressWithoutStartingDuplicateProcessors()
    {
        var rag = CreateLightRagForStateProcessorTest(progressChunkCount: 2);
        var states = new List<TaskState>();
        var gate = new object();
        rag.TaskStateChanged += (_, state) =>
        {
            lock (gate)
            {
                states.Add(state);
            }
        };

        await Task.WhenAll(
            rag.InsertAsync("alpha beta gamma", docId: "doc-a", filePath: "docs/a.md"),
            rag.InsertAsync("delta epsilon zeta", docId: "doc-b", filePath: "docs/b.md"));

        lock (gate)
        {
            states.Should().Contain(state => state.DocId == "doc-a" && state.Stage == TaskStage.Completed);
            states.Should().Contain(state => state.DocId == "doc-b" && state.Stage == TaskStage.Completed);
        }
    }

    [Fact]
    public async Task TaskStateChanged_WhenOneSubscriberThrows_StillNotifiesOtherSubscribers()
    {
        var rag = CreateLightRagForStateProcessorTest(progressChunkCount: 2);
        var delivered = new List<TaskStage>();
        var gate = new object();
        var throwOnce = 0;
        rag.TaskStateChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref throwOnce, 1) == 0)
            {
                throw new InvalidOperationException("subscriber failed");
            }
        };
        rag.TaskStateChanged += (_, state) =>
        {
            lock (gate)
            {
                delivered.Add(state.Stage);
            }
        };

        await rag.InsertAsync("alpha beta gamma", docId: "doc-a", filePath: "docs/a.md");

        lock (gate)
        {
            delivered.Should().Contain(TaskStage.Completed);
        }
    }
}
```

Add a private `CreateLightRagForStateProcessorTest(int progressChunkCount)` helper in this file. Copy the minimal construction pattern from `LightRAGLifecycleIntegrationTests.CreateLightRag`, use `InMemoryDocumentStatusStore`, `InMemoryKvStore`, `InMemoryVectorStore`, `FakeTokenizer`, and substituted LLM / embedding / graph services. Set `ChunkTokenSize` so each inserted document emits at least two progress events before `Completed`.

- [ ] **Step 2: Run state processor test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~LightRAGStateProcessorTests
```

Expected before implementation: the test may already pass, but it documents the concurrency boundary before changing the startup guard.

- [ ] **Step 3: Add processor startup guard**

Modify `src/LightRAGNet/LightRAG.cs`.

Add field:

```csharp
private readonly object _stateProcessorGate = new();
```

Replace `InitializeStateProcessor` with:

```csharp
private void EnsureStateProcessorStarted()
{
    lock (_stateProcessorGate)
    {
        if (_stateProcessorTask is { IsCompleted: false })
        {
            return;
        }

        _stateProcessorTask = Task.Run(ProcessTaskStatesAsync);
    }
}

private async Task ProcessTaskStatesAsync()
{
    while (await _taskStateBuffer.OutputAvailableAsync())
    {
        try
        {
            var state = await _taskStateBuffer.ReceiveAsync();
            PublishTaskState(state);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing task state update");
        }
    }
}

private void PublishTaskState(TaskState state)
{
    var handlers = TaskStateChanged?.GetInvocationList()
        .Cast<EventHandler<TaskState>>()
        .ToArray();

    if (handlers is null || handlers.Length == 0)
    {
        return;
    }

    foreach (var handler in handlers)
    {
        try
        {
            handler(this, state);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Task state subscriber failed: DocId={DocId}, Stage={Stage}, Current={Current}, Total={Total}",
                state.DocId,
                state.Stage,
                state.Current,
                state.Total);
        }
    }
}
```

Replace both startup checks in `InsertAsync` and `DeleteDocumentAsync`:

```csharp
EnsureStateProcessorStarted();
```

- [ ] **Step 4: Run lifecycle tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRAGStateProcessorTests|FullyQualifiedName~LightRAGLifecycleIntegrationTests|FullyQualifiedName~DocumentDeletionServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGStateProcessorTests.cs
git commit -m "fix: guard lightrag state processor startup"
```

## Task 9: Final Verification And Archive

**Files:**
- Create: `docs/superpowers/archives/2026-05/2026-05-19-concurrency-race-governance-archive.md`
- Modify if needed: `docs/superpowers/archives/INDEX.md`
- Modify if needed: `docs/superpowers/problems/INDEX.md`
- Modify if needed: `docs/superpowers/inbox/INDEX.md`

- [ ] **Step 1: Run full test suite**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
```

Expected: all test projects PASS.

- [ ] **Step 2: Run full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx
```

Expected: build succeeds with `0` errors. Investigate any warning introduced by this plan before closeout.

- [ ] **Step 3: Verify no old race patterns remain in target files**

Run:

```powershell
rg -n "_ = taskQueue\.UpdateTaskProgressAsync|_ = NotifyTaskStatusHandlersAsync|_ = NotifyDataClearedHandlersAsync|File\.WriteAllText\(|File\.Move\(.*overwrite: true|CancellationTokenSource\?" src/LightRAGNet src/LightRAGNet.Storage src/LightRAGNet.Web
```

Expected:

- no unmanaged fire-and-forget progress updates in `RagTaskProcessorService`
- no unmanaged fire-and-forget task/data notification dispatch in `RagTaskNotificationService`
- no direct JSON persistence write in `JsonKVStore`
- no direct file replacement retry logic in `RagTaskStateStore`
- no shared query CTS field in `RagChat.razor`
- remaining `CancellationTokenSource` usages are registry-owned or helper-owned

- [ ] **Step 4: Run asset completion gate**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "concurrency race governance" --json
```

Expected: command exits successfully and reports the archive/problem route needed for this completed governance work.

- [ ] **Step 5: Create requirement archive after implementation is accepted**

Create `docs/superpowers/archives/2026-05/2026-05-19-concurrency-race-governance-archive.md`:

```markdown
# Concurrency Race Governance

- Date: `2026-05-19`
- Topic slug: `concurrency-race-governance`
- Status: `Archived`
- Scope: `Reliability`
- Tags: `concurrency`, `race-condition`, `file-persistence`, `task-progress`, `blazor`, `signalr`

## Summary

本轮交付把最近暴露出的竞态问题从局部补丁提升为可复用并发边界：统一 JSON 文件原子写入，串行化任务进度和前端通知，集中页面级操作取消生命周期，并给 LightRAG 状态泵加启动保护。

## Delivered Scope

- Added `AtomicFileWriter` under Core and migrated task state / JSON KV persistence.
- Added locked snapshot reads to `JsonKVStore`.
- Added `AsyncEventDispatcher` and migrated task progress updates away from unmanaged fire-and-forget calls.
- Serialized frontend task/data notifications.
- Added `AsyncOperationSlot` and migrated `AsyncDebouncer` / `RagChat` cancellation ownership.
- Added `PerKeyAsyncLock` for keyed task/document/workspace serialization.
- Guarded `LightRAG` state processor startup and isolated subscriber failures.

## Verification Snapshot

- `dotnet test .\LightRAGNet.slnx`: record the final pass count from Step 1.
- `dotnet build .\LightRAGNet.slnx`: record final error and warning count from Step 2.
- Race pattern scan: record the result from Step 3.

## Source Documents

- Spec: [concurrency race governance design](../../specs/2026-05-19-concurrency-race-governance-design.md)
- Plan: [concurrency race governance implementation plan](../../plans/2026-05-19-concurrency-race-governance-implementation-plan.md)

## Related Problems

- [Markdown documents debounce race](../../problems/2026-05/2026-05-19-markdown-documents-debounce-race-problem.md)
- [Task state file replace lock](../../problems/2026-05/2026-05-19-task-state-file-replace-lock-problem.md)
```

- [ ] **Step 6: Commit closeout assets**

```powershell
git add docs/superpowers/archives docs/superpowers/problems docs/superpowers/inbox
git commit -m "docs: archive concurrency race governance"
```

Stage only paths that changed. If the completion gate reports `none` for problem routing, do not create a new problem asset.

## Self-Review

- Spec coverage:
  - File persistence foundation is covered by Tasks 1 and 2.
  - Task progress serialization is covered by Tasks 3 and 4.
  - UI notification serialization is covered by Task 5.
  - Page operation lifetime cleanup is covered by Task 6.
  - Keyed resource serialization is covered by Task 7.
  - LightRAG state pump startup and subscriber isolation are covered by Task 8.
  - Final verification and requirement archive are covered by Task 9.
- Dependency boundary:
  - `AtomicFileWriter` is in Core so Storage can reuse it without a circular reference.
  - UI code depends on existing LightRAGNet service utilities, matching the current Web project dependency shape.
- Test boundary:
  - Core helpers have direct unit tests.
  - Persistence and task processor changes have behavioral tests.
  - Blazor notification dispatch gets a source-level guard plus Web build because the existing test project does not host Blazor components.




