# Testability Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor LightRAGNet into a testable `src/` and `tests/` layout and add the first high-value characterization tests without adding product features.

**Architecture:** Keep current behavior intact while moving production projects under `src/` and adding test projects under `tests/`. Extract only small pure logic seams from large services where direct testing would otherwise require brittle private-method access.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NSubstitute, Microsoft.NET.Test.Sdk, coverlet.collector, Microsoft.AspNetCore.Mvc.Testing.

---

## File Structure

- Modify: `AGENTS.md` to keep the managed asset-compounding retrieval block produced by plugin bootstrap.
- Modify: `LightRAGNet.slnx` with all production projects moved under `src/` and test projects under `tests/`.
- Move: `LightRAGNet.Core/` -> `src/LightRAGNet.Core/`.
- Move: `LightRAGNet/` -> `src/LightRAGNet/`.
- Move: `LightRAGNet.Hosting/` -> `src/LightRAGNet.Hosting/`.
- Move: `LightRAGNet.LLM/` -> `src/LightRAGNet.LLM/`.
- Move: `LightRAGNet.Embedding/` -> `src/LightRAGNet.Embedding/`.
- Move: `LightRAGNet.Rerank/` -> `src/LightRAGNet.Rerank/`.
- Move: `LightRAGNet.Storage/` -> `src/LightRAGNet.Storage/`.
- Move: `LightRAGNet.Server/` -> `src/LightRAGNet.Server/`.
- Move: `LightRAGNet.Web/` -> `src/LightRAGNet.Web/`.
- Move: `LightRAGNet.Share/` -> `src/LightRAGNet.Share/`.
- Move: `LightRAGNet.Example/` -> `src/LightRAGNet.Example/`.
- Create: `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj`.
- Create: `tests/LightRAGNet.Server.Tests/LightRAGNet.Server.Tests.csproj`.
- Create: `tests/LightRAGNet.Tests/TestDoubles/FakeTokenizer.cs`.
- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryRagTaskStateStore.cs`.
- Create: `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`.
- Create: `tests/LightRAGNet.Tests/RetrievalContext/TokenBudgetPlannerTests.cs`.
- Create: `tests/LightRAGNet.Tests/RetrievalContext/ChunkTokenLimiterTests.cs`.
- Create: `tests/LightRAGNet.Tests/RetrievalContext/ReferenceListBuilderTests.cs`.
- Create: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/SourceIdsLimiterTests.cs`.
- Create: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/DescriptionMergerTests.cs`.
- Create: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`.
- Create: `tests/LightRAGNet.Server.Tests/ServerHostSmokeTests.cs`.
- Create: `src/LightRAGNet/Services/RetrievalContext/TokenBudgetPlan.cs`.
- Create: `src/LightRAGNet/Services/RetrievalContext/TokenBudgetPlanner.cs`.
- Create: `src/LightRAGNet/Services/RetrievalContext/ChunkTokenLimiter.cs`.
- Create: `src/LightRAGNet/Services/RetrievalContext/ReferenceListBuilder.cs`.
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs` to delegate chunk limiting and reference generation to the new pure components.
- Modify: `src/LightRAGNet/LightRAGNet.csproj` with `InternalsVisibleTo` for `LightRAGNet.Tests` if internal retrieval or merge types stay internal.

## Commit Rhythm

Use small commits with English conventional messages:

- `docs: initialize asset compounding guidance`
- `chore: move projects under src`
- `test: add core test infrastructure`
- `test: cover document chunking behavior`
- `refactor: extract retrieval context test seams`
- `test: cover graph merge and task queue behavior`

---

### Task 1: Commit Asset Bootstrap Guidance

**Files:**
- Modify: `AGENTS.md`

- [ ] **Step 1: Verify asset bootstrap is idempotent**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.1\skills\compound-development-asset\scripts\bootstrap_asset_compounding.py . --write --json
```

Expected: JSON contains `"changed": false` and `"action": "unchanged"`.

- [ ] **Step 2: Inspect the bootstrap diff**

Run:

```powershell
git diff -- AGENTS.md
```

Expected: diff contains one managed block between `<!-- asset-compounding-guidance:start -->` and `<!-- asset-compounding-guidance:end -->`.

- [ ] **Step 3: Commit the bootstrap guidance**

Run:

```powershell
git add AGENTS.md
git commit -m "docs: initialize asset compounding guidance"
```

Expected: commit succeeds and `git status --short` is clean or only shows files created by the next active task.

---

### Task 2: Move Production Projects Under `src/`

**Files:**
- Move: all production project directories listed in File Structure.
- Modify: `LightRAGNet.slnx`
- Modify: every moved `*.csproj` that contains `ProjectReference`.

- [ ] **Step 1: Create the target directory**

Run:

```powershell
New-Item -ItemType Directory -Force .\src | Out-Null
```

Expected: `src/` exists.

- [ ] **Step 2: Move production project directories**

Run each command from the repository root:

```powershell
Move-Item -LiteralPath .\LightRAGNet.Core -Destination .\src\LightRAGNet.Core
Move-Item -LiteralPath .\LightRAGNet -Destination .\src\LightRAGNet
Move-Item -LiteralPath .\LightRAGNet.Hosting -Destination .\src\LightRAGNet.Hosting
Move-Item -LiteralPath .\LightRAGNet.LLM -Destination .\src\LightRAGNet.LLM
Move-Item -LiteralPath .\LightRAGNet.Embedding -Destination .\src\LightRAGNet.Embedding
Move-Item -LiteralPath .\LightRAGNet.Rerank -Destination .\src\LightRAGNet.Rerank
Move-Item -LiteralPath .\LightRAGNet.Storage -Destination .\src\LightRAGNet.Storage
Move-Item -LiteralPath .\LightRAGNet.Server -Destination .\src\LightRAGNet.Server
Move-Item -LiteralPath .\LightRAGNet.Web -Destination .\src\LightRAGNet.Web
Move-Item -LiteralPath .\LightRAGNet.Share -Destination .\src\LightRAGNet.Share
Move-Item -LiteralPath .\LightRAGNet.Example -Destination .\src\LightRAGNet.Example
```

Expected: root no longer contains those project directories; `src/` contains all of them.

- [ ] **Step 3: Update solution project paths**

Replace `LightRAGNet.X\LightRAGNet.X.csproj` entries with `src\LightRAGNet.X\LightRAGNet.X.csproj` in `LightRAGNet.slnx`.

Expected `LightRAGNet.slnx` contains:

```xml
<Project Path="src\LightRAGNet.Core\LightRAGNet.Core.csproj" />
<Project Path="src\LightRAGNet.LLM\LightRAGNet.LLM.csproj" />
<Project Path="src\LightRAGNet.Embedding\LightRAGNet.Embedding.csproj" />
<Project Path="src\LightRAGNet.Rerank\LightRAGNet.Rerank.csproj" />
<Project Path="src\LightRAGNet.Storage\LightRAGNet.Storage.csproj" />
<Project Path="src\LightRAGNet\LightRAGNet.csproj" />
<Project Path="src\LightRAGNet.Example\LightRAGNet.Example.csproj" />
<Project Path="src\LightRAGNet.Hosting\LightRAGNet.Hosting.csproj" />
<Project Path="src\LightRAGNet.Web\LightRAGNet.Web.csproj" />
<Project Path="src\LightRAGNet.Server\LightRAGNet.Server.csproj" />
<Project Path="src\LightRAGNet.Share\LightRAGNet.Share.csproj" />
```

- [ ] **Step 4: Keep project references correct inside `src/`**

Run:

```powershell
rg "<ProjectReference" .\src -g "*.csproj"
```

Expected: references still use sibling relative paths such as `..\LightRAGNet.Core\LightRAGNet.Core.csproj`; because all projects moved together, these should remain valid.

- [ ] **Step 5: Restore and build**

Run:

```powershell
dotnet restore .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
```

Expected: both commands exit with code 0.

- [ ] **Step 6: Commit the structural move**

Run:

```powershell
git add -A
git commit -m "chore: move projects under src"
```

Expected: commit succeeds.

---

### Task 3: Add Test Project Infrastructure

**Files:**
- Create: `tests/LightRAGNet.Tests/LightRAGNet.Tests.csproj`
- Create: `tests/LightRAGNet.Server.Tests/LightRAGNet.Server.Tests.csproj`
- Modify: `LightRAGNet.slnx`

- [ ] **Step 1: Create test projects**

Run:

```powershell
dotnet new xunit -n LightRAGNet.Tests -o .\tests\LightRAGNet.Tests --framework net10.0
dotnet new xunit -n LightRAGNet.Server.Tests -o .\tests\LightRAGNet.Server.Tests --framework net10.0
```

Expected: both test project directories exist.

- [ ] **Step 2: Add references and test packages**

Run:

```powershell
dotnet add .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj reference .\src\LightRAGNet\LightRAGNet.csproj
dotnet add .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj package FluentAssertions
dotnet add .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj package NSubstitute
dotnet add .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj package coverlet.collector
dotnet add .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj reference .\src\LightRAGNet.Server\LightRAGNet.Server.csproj
dotnet add .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj package FluentAssertions
dotnet add .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj package coverlet.collector
```

Expected: package references are added to the test projects.

- [ ] **Step 3: Add test projects to the solution file**

Modify `LightRAGNet.slnx` to include:

```xml
<Project Path="tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj" />
<Project Path="tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj" />
```

- [ ] **Step 4: Remove generated sample tests**

Delete:

```text
tests/LightRAGNet.Tests/UnitTest1.cs
tests/LightRAGNet.Server.Tests/UnitTest1.cs
```

- [ ] **Step 5: Add a server smoke test skeleton**

Create `tests/LightRAGNet.Server.Tests/ServerHostSmokeTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace LightRAGNet.Server.Tests;

public class ServerHostSmokeTests
{
    [Fact]
    public void TestProject_Loads()
    {
        typeof(Program).Assembly.GetName().Name.Should().Be("LightRAGNet.Server");
    }
}
```

If `Program` is inaccessible, add this to `src/LightRAGNet.Server/Program.cs` after top-level statements:

```csharp
public partial class Program;
```

- [ ] **Step 6: Run the empty infrastructure test**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
```

Expected: test command exits with code 0.

- [ ] **Step 7: Commit test infrastructure**

Run:

```powershell
git add -A
git commit -m "test: add test project infrastructure"
```

Expected: commit succeeds.

---

### Task 4: Add Shared Test Doubles

**Files:**
- Create: `tests/LightRAGNet.Tests/TestDoubles/FakeTokenizer.cs`
- Create: `tests/LightRAGNet.Tests/TestDoubles/InMemoryRagTaskStateStore.cs`

- [ ] **Step 1: Write `FakeTokenizer`**

Create:

```csharp
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class FakeTokenizer : ITokenizer
{
    public List<int> Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Enumerable.Range(1, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
    }

    public string Decode(List<int> tokens)
    {
        return string.Join(" ", tokens.Select(token => $"t{token}"));
    }

    public int CountTokens(string text)
    {
        return Encode(text).Count;
    }
}
```

- [ ] **Step 2: Write `InMemoryRagTaskStateStore`**

Create:

```csharp
using LightRAGNet.Models;
using LightRAGNet.Services.TaskQueue;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class InMemoryRagTaskStateStore : IRagTaskStateStore
{
    private readonly Dictionary<string, RagTask> _tasks = new();

    public Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
    {
        _tasks[task.TaskId] = task;
        return Task.CompletedTask;
    }

    public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_tasks.Values.ToList());
    }

    public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_tasks.GetValueOrDefault(taskId));
    }

    public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.Remove(taskId);
        return Task.CompletedTask;
    }

    public Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
    {
        _tasks.Clear();
        foreach (var task in tasks)
        {
            _tasks[task.TaskId] = task;
        }
        return Task.CompletedTask;
    }

    public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
    {
        _tasks.Clear();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj
```

Expected: test project compiles.

---

### Task 5: Characterize Document Chunking

**Files:**
- Create: `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`

- [ ] **Step 1: Write the first failing chunking test**

Add:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LightRAGNet.Tests.DocumentProcessing;

public class DocumentProcessingServiceTests
{
    [Fact]
    public void ChunkDocument_TrimsContentBeforeTokenization()
    {
        var service = CreateService(chunkSize: 10, overlap: 2);

        var chunks = service.ChunkDocument("  alpha beta  ", "doc-1", "file.md");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("t1 t2");
        chunks[0].Tokens.Should().Be(2);
        chunks[0].FullDocId.Should().Be("doc-1");
        chunks[0].FilePath.Should().Be("file.md");
    }

    private static DocumentProcessingService CreateService(int chunkSize, int overlap)
    {
        var options = Options.Create(new LightRAGOptions
        {
            ChunkTokenSize = chunkSize,
            ChunkOverlapTokenSize = overlap
        });

        return new DocumentProcessingService(
            Substitute.For<ILLMService>(),
            Substitute.For<IEmbeddingService>(),
            new FakeTokenizer(),
            Substitute.For<IKVStore>(),
            options,
            NullLogger<DocumentProcessingService>.Instance);
    }
}
```

- [ ] **Step 2: Run the test and verify red**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter ChunkDocument_TrimsContentBeforeTokenization
```

Expected: either compile fails because types moved/internal access need correction, or the test fails for the expected behavior. Fix only test setup/import issues until the failure reflects behavior.

- [ ] **Step 3: Add more characterization tests after the first red/green loop**

Add tests named:

```csharp
ChunkDocument_UsesSlidingTokenWindowWithOverlap()
ChunkDocument_MergesTinyTrailingFragmentIntoPreviousChunk()
ChunkDocument_SplitsByCharacter()
ChunkDocument_WhenSplitByCharacterOnlyAndSegmentExceedsLimit_Throws()
```

Use `FakeTokenizer` and assert chunk counts, token counts, and `ChunkOrderIndex`.

- [ ] **Step 4: Run document processing tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~DocumentProcessingServiceTests
```

Expected: all document processing tests pass.

---

### Task 6: Extract Retrieval Context Pure Components

**Files:**
- Create: `tests/LightRAGNet.Tests/RetrievalContext/TokenBudgetPlannerTests.cs`
- Create: `tests/LightRAGNet.Tests/RetrievalContext/ChunkTokenLimiterTests.cs`
- Create: `tests/LightRAGNet.Tests/RetrievalContext/ReferenceListBuilderTests.cs`
- Create: `src/LightRAGNet/Services/RetrievalContext/TokenBudgetPlan.cs`
- Create: `src/LightRAGNet/Services/RetrievalContext/TokenBudgetPlanner.cs`
- Create: `src/LightRAGNet/Services/RetrievalContext/ChunkTokenLimiter.cs`
- Create: `src/LightRAGNet/Services/RetrievalContext/ReferenceListBuilder.cs`
- Modify: `src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs`

- [ ] **Step 1: Write failing token budget tests**

Create `TokenBudgetPlannerTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Xunit;

namespace LightRAGNet.Tests.RetrievalContext;

public class TokenBudgetPlannerTests
{
    [Fact]
    public void Plan_ReservesSystemQueryKgOutputAndSafetyTokens()
    {
        var planner = new TokenBudgetPlanner(new FakeTokenizer());

        var plan = planner.Plan(
            maxTotalTokens: 100,
            systemPrompt: "one two three",
            query: "four five",
            knowledgeGraphContext: "six seven eight nine",
            reservedOutputTokens: 20,
            safetyBufferTokens: 10);

        plan.AvailableChunkTokens.Should().Be(61);
        plan.SystemPromptTokens.Should().Be(3);
        plan.QueryTokens.Should().Be(2);
        plan.KnowledgeGraphContextTokens.Should().Be(4);
    }

    [Fact]
    public void Plan_ClampsAvailableChunkTokensAtZero()
    {
        var planner = new TokenBudgetPlanner(new FakeTokenizer());

        var plan = planner.Plan(10, "one two three", "four five", "six seven eight", 20, 5);

        plan.AvailableChunkTokens.Should().Be(0);
    }
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter TokenBudgetPlannerTests
```

Expected: compile fails because `TokenBudgetPlanner` does not exist.

- [ ] **Step 2: Implement token budget seam**

Create `TokenBudgetPlan.cs`:

```csharp
namespace LightRAGNet.Services.RetrievalContext;

internal sealed record TokenBudgetPlan(
    int MaxTotalTokens,
    int SystemPromptTokens,
    int QueryTokens,
    int KnowledgeGraphContextTokens,
    int ReservedOutputTokens,
    int SafetyBufferTokens,
    int AvailableChunkTokens);
```

Create `TokenBudgetPlanner.cs`:

```csharp
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class TokenBudgetPlanner(ITokenizer tokenizer)
{
    public TokenBudgetPlan Plan(
        int maxTotalTokens,
        string systemPrompt,
        string query,
        string knowledgeGraphContext,
        int reservedOutputTokens,
        int safetyBufferTokens)
    {
        var systemPromptTokens = tokenizer.CountTokens(systemPrompt);
        var queryTokens = tokenizer.CountTokens(query);
        var kgTokens = tokenizer.CountTokens(knowledgeGraphContext);
        var available = maxTotalTokens
            - systemPromptTokens
            - queryTokens
            - kgTokens
            - reservedOutputTokens
            - safetyBufferTokens;

        return new TokenBudgetPlan(
            maxTotalTokens,
            systemPromptTokens,
            queryTokens,
            kgTokens,
            reservedOutputTokens,
            safetyBufferTokens,
            Math.Max(0, available));
    }
}
```

If tests cannot access internal types, add to `src/LightRAGNet/LightRAGNet.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="LightRAGNet.Tests" />
</ItemGroup>
```

and add:

```xml
<Using Include="System.Runtime.CompilerServices" />
```

or create `src/LightRAGNet/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LightRAGNet.Tests")]
```

- [ ] **Step 3: Write and implement chunk limiter tests**

Test names:

```csharp
Limit_PreservesChunksUntilTokenBudgetIsExceeded()
Limit_ReturnsEmptyWhenFirstChunkExceedsBudget()
```

Implement `ChunkTokenLimiter` using the existing context format:

```csharp
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class ChunkTokenLimiter(ITokenizer tokenizer)
{
    public List<ChunkData> Limit(IEnumerable<ChunkData> chunks, int maxTokens)
    {
        var result = new List<ChunkData>();
        var currentTokens = 0;

        foreach (var chunk in chunks)
        {
            var fileName = ReferenceListBuilder.ExtractFileName(chunk.FilePath);
            var chunkTokens = tokenizer.CountTokens($"[{fileName}]\n{chunk.Content}");
            if (currentTokens + chunkTokens > maxTokens)
            {
                break;
            }

            result.Add(chunk);
            currentTokens += chunkTokens;
        }

        return result;
    }
}
```

- [ ] **Step 4: Write and implement reference builder tests**

Test names:

```csharp
Build_AssignsSameReferenceIdToChunksWithSameFilePath()
Build_DecodesFileNameFromUrl()
```

Implement `ReferenceListBuilder` with:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class ReferenceListBuilder
{
    public (List<ReferenceItem> References, List<ChunkData> ChunksWithRefIds) Build(IEnumerable<ChunkData> chunks)
    {
        var referenceMap = new Dictionary<string, string>();
        var chunksWithRefIds = new List<ChunkData>();

        foreach (var chunk in chunks)
        {
            var key = string.IsNullOrWhiteSpace(chunk.FilePath) ? "unknown" : chunk.FilePath;
            if (!referenceMap.TryGetValue(key, out var referenceId))
            {
                referenceId = (referenceMap.Count + 1).ToString();
                referenceMap[key] = referenceId;
            }

            chunksWithRefIds.Add(new ChunkData
            {
                ChunkId = chunk.ChunkId,
                Content = chunk.Content,
                FilePath = chunk.FilePath,
                ReferenceId = referenceId
            });
        }

        var references = referenceMap.Select(item => new ReferenceItem
        {
            ReferenceId = item.Value,
            FilePath = item.Key
        }).ToList();

        return (references, chunksWithRefIds);
    }

    public static string ExtractFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return "unknown";
        }

        if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(filePath);
            var fileName = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrEmpty(fileName) ? "unknown" : Uri.UnescapeDataString(fileName);
        }

        var localName = Path.GetFileName(filePath);
        return string.IsNullOrEmpty(localName) ? filePath : localName;
    }
}
```

- [ ] **Step 5: Refactor `RetrievalContextService` to delegate**

Replace private `ApplyTokenLimit` body with `new ChunkTokenLimiter(tokenizer).Limit(chunks, maxTokens)`.

Replace private `GenerateReferenceListFromChunks` body with `new ReferenceListBuilder().Build(chunks)`.

Replace `ExtractFileName` calls with `ReferenceListBuilder.ExtractFileName`.

- [ ] **Step 6: Run retrieval context tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RetrievalContext
```

Expected: all retrieval context component tests pass.

- [ ] **Step 7: Commit retrieval context refactor**

Run:

```powershell
git add -A
git commit -m "refactor: extract retrieval context test seams"
```

Expected: commit succeeds.

---

### Task 7: Cover Knowledge Graph Merge Boundaries

**Files:**
- Create: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/SourceIdsLimiterTests.cs`
- Create: `tests/LightRAGNet.Tests/KnowledgeGraphMerge/DescriptionMergerTests.cs`

- [ ] **Step 1: Write `SourceIdsLimiter` tests**

Test names:

```csharp
ApplyLimit_WithFifoMethod_KeepsNewestIds()
ApplyLimit_WithKeepMethod_KeepsOldestIds()
ApplyLimit_WithNonPositiveLimit_ReturnsEmpty()
ComputeTruncationInfo_WhenNoTruncation_ReturnsEmpty()
```

Use:

```csharp
var limiter = new SourceIdsLimiter(
    Options.Create(new LightRAGOptions { SourceIdsLimitMethod = "FIFO" }),
    NullLogger<SourceIdsLimiter>.Instance);
```

Expected behavior:

```csharp
limiter.ApplyLimit(["a", "b", "c"], 2).Should().Equal("b", "c");
```

- [ ] **Step 2: Run and fix access issues only**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter SourceIdsLimiterTests
```

Expected: tests pass after internal visibility is configured; do not change `SourceIdsLimiter` behavior.

- [ ] **Step 3: Write `DescriptionMerger` tests**

Use NSubstitute for `ILLMService`.

Test names:

```csharp
MergeAsync_WithSingleDescription_ReturnsItWithoutLlm()
MergeAsync_WhenBelowForceThreshold_JoinsDescriptionsWithoutLlm()
MergeAsync_WhenForceThresholdReached_UsesLlmSummary()
```

Use options:

```csharp
new LightRAGOptions
{
    SummaryContextSize = 100,
    SummaryMaxTokens = 100,
    ForceLLMSummaryOnMerge = 3,
    SummaryLengthRecommended = 50
}
```

- [ ] **Step 4: Run graph merge tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~KnowledgeGraphMerge
```

Expected: all graph merge tests pass.

---

### Task 8: Cover Task Queue Behavior

**Files:**
- Create: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`

- [ ] **Step 1: Write task queue factory**

In the test file, add:

```csharp
private static (RagTaskQueueService Service, InMemoryRagTaskStateStore Store, IMediator Mediator) CreateService()
{
    var store = new InMemoryRagTaskStateStore();
    var mediator = Substitute.For<IMediator>();
    var service = new RagTaskQueueService(
        store,
        mediator,
        NullLogger<RagTaskQueueService>.Instance);

    return (service, store, mediator);
}
```

- [ ] **Step 2: Write failing enqueue test**

Test:

```csharp
[Fact]
public async Task EnqueueTaskAsync_CreatesPendingTaskAndPublishesEvent()
{
    var (service, _, mediator) = CreateService();

    var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
    var task = await service.GetTaskAsync(taskId);

    task.Should().NotBeNull();
    task!.Status.Should().Be(RagTaskStatus.Pending);
    task.DocumentId.Should().Be(7);
    await mediator.Received(1).Publish(
        Arg.Any<RagTaskStatusChangedEvent>(),
        Arg.Any<CancellationToken>());
}
```

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter EnqueueTaskAsync_CreatesPendingTaskAndPublishesEvent
```

Expected: compile or behavior failure before fixing test doubles; then pass with no production logic change.

- [ ] **Step 3: Add state transition tests**

Add tests named:

```csharp
GetNextTaskAsync_ReturnsLowestPriorityPendingTask()
UpdateTaskStatusAsync_WhenProcessing_SetsStartedAtAndSaves()
UpdateTaskStatusAsync_WhenCompleted_RemovesPersistentState()
RetryTaskAsync_WhenFailedAndBelowMaxRetries_RequeuesTask()
StopAllTasksAsync_FailsPendingAndProcessingTasks()
ClearAllTasksAsync_RemovesAllTasks()
```

Use public service methods only.

- [ ] **Step 4: Run task queue tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter RagTaskQueueServiceTests
```

Expected: all queue behavior tests pass.

- [ ] **Step 5: Commit graph and queue tests**

Run:

```powershell
git add -A
git commit -m "test: cover graph merge and task queue behavior"
```

Expected: commit succeeds.

---

### Task 9: Final Verification And Asset Gate

**Files:**
- Modify: `docs/superpowers/plans/2026-05-17-testability-foundation-implementation-plan.md` only if execution notes need correction.
- Possibly create: `docs/superpowers/archives/2026-05/2026-05-17-testability-foundation-archives.md` after implementation is complete.

- [ ] **Step 1: Run full restore, build, and test**

Run:

```powershell
dotnet restore .\LightRAGNet.slnx
dotnet build .\LightRAGNet.slnx
dotnet test .\LightRAGNet.slnx
```

Expected: all commands exit with code 0.

- [ ] **Step 2: Run asset index checks**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.1\skills\compound-development-asset\scripts\check_indexes.py .
```

Expected: no index errors. If archive/problem/inbox indexes do not exist yet because no such asset was written, explain that explicitly.

- [ ] **Step 3: Run the asset compounding gate**

Use `using-asset-compounding` after implementation, spec alignment, code quality review, and verification. For this requirement, expected route is `archive` if implementation completes and verifies. If implementation uncovers a reusable failure mode, route `both` or `archive` plus `inbox` depending on evidence maturity.

- [ ] **Step 4: Final report**

Report:

```text
Structural migration:
- production projects moved under src
- tests created under tests

Tests added:
- document processing
- retrieval context pure components
- graph merge boundaries
- task queue behavior
- server host smoke

Verification:
- dotnet restore .\LightRAGNet.slnx
- dotnet build .\LightRAGNet.slnx
- dotnet test .\LightRAGNet.slnx

Deferred:
- UI automation
- Qdrant/Neo4j/Testcontainers integration
- full API contract coverage
```
