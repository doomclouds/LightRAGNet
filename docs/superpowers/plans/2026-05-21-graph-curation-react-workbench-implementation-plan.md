# Graph Curation React Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the weak Blazor/Sigma graph page with a Python LightRAG-inspired React graph workbench and add backend graph curation APIs for entity/relation edit, create, merge, and delete.

**Architecture:** Add a backend `GraphCurationService` that coordinates graph store, vector store, KV metadata, tracking KV, and query revision bumps. Add a Vite React island under `src/LightRAGNet.Web/ClientApp` that can later become the main React frontend; Blazor only hosts the compiled graph workbench bundle during this transition.

**Tech Stack:** .NET 10, ASP.NET Core controllers, xUnit, FluentAssertions, existing in-memory test doubles, Vite, React, TypeScript, `@react-sigma/core`, `graphology`, `zustand`.

---

## Reference Source Declaration Requirement

All implementation and final asset archive work must state that this feature references Python LightRAG:

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend files:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`
  - `LightRAG/lightrag_webui/src/stores/settings.ts`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/EditablePropertyRow.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/MergeDialog.tsx`
  - `LightRAG/lightrag_webui/src/api/lightrag.ts`
- Referenced backend files:
  - `LightRAG/lightrag/api/routers/graph_routes.py`
  - `LightRAG/lightrag/lightrag.py`
  - `LightRAG/lightrag/utils_graph.py`

The final archive must include a `Reference Source Declaration` section with this repository URL and the specific frontend/backend areas above.

## Scope Check

This plan intentionally includes one coherent requirement: graph curation as a user-facing workbench. It touches backend graph mutation APIs and the graph UI together because graph editing cannot be validated through backend-only endpoints. It does not migrate the full Web app to React, does not replace Chat/Documents pages, and does not build an undo-capable graph modeling tool.

## File Structure

### Backend files

- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationOperationResult.cs`
  - Shared operation result and operation summary records.
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationModels.cs`
  - Request records for service-level entity/relation create, edit, merge, and delete.
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationVectorIds.cs`
  - Deterministic entity/relation vector id helpers copied from existing deletion semantics.
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationService.cs`
  - Owns graph curation behavior and cross-store consistency.
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Register `GraphCurationService`.
- Create: `src/LightRAGNet.Share/Models/GraphCurationModels.cs`
  - Public API request/response DTOs consumed by React.
- Create: `src/LightRAGNet.Server/Controllers/GraphController.cs`
  - New `/api/graph/*` route family.
- Modify: `src/LightRAGNet.Server/Controllers/GraphViewController.cs`
  - Keep existing route as compatibility; no new curation logic here.

### Backend test files

- Create: `tests/LightRAGNet.Tests/GraphCuration/GraphCurationServiceTests.cs`
- Create: `tests/LightRAGNet.Server.Tests/GraphControllerTests.cs`
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryGraphStore.cs`
  - Add optional helpers needed by rename/merge assertions.
- Modify: `tests/LightRAGNet.Tests/TestDoubles/InMemoryVectorStore.cs`
  - Reuse existing upsert/delete/get tracking.

### Frontend files

- Create: `src/LightRAGNet.Web/ClientApp/package.json`
- Create: `src/LightRAGNet.Web/ClientApp/vite.config.ts`
- Create: `src/LightRAGNet.Web/ClientApp/tsconfig.json`
- Create: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/graphApi.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/types/graph.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/stores/graphSettingsStore.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/hooks/useGraphWorkbench.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphToolbar.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertyEditDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/MergeDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/ConfirmDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
- Modify: `src/LightRAGNet.Web/Components/Pages/GraphView.razor`
  - Replace primary page content with React mount host.
- Modify: `src/LightRAGNet.Web/Components/Routes.razor`
  - No route change expected; verify the existing page route still resolves.
- Modify: `src/LightRAGNet.Web/wwwroot/app.css`
  - Add only host-level sizing if needed.

### Frontend test files

- Create: `src/LightRAGNet.Web/ClientApp/src/api/graphApi.test.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`
- Create: `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`

### Documentation and asset files

- Modify: `README.md`
  - Add graph workbench frontend build commands.
- Modify: `README.CN.md`
  - Add the same commands in Chinese.
- Create after implementation: `docs/superpowers/archives/2026-05/2026-05-21-graph-curation-react-workbench-archives.md`
  - Must include `Reference Source Declaration`.
- Modify after implementation: `docs/superpowers/archives/INDEX.md`
  - Add archive entry.

---

### Task 1: Backend Curation Models and Vector Id Helpers

**Files:**
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationOperationResult.cs`
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationModels.cs`
- Create: `src/LightRAGNet/Services/GraphCuration/GraphCurationVectorIds.cs`
- Test: `tests/LightRAGNet.Tests/GraphCuration/GraphCurationServiceTests.cs`

- [ ] **Step 1: Write failing tests for deterministic vector ids and validation shape**

Add this initial test file:

```csharp
using FluentAssertions;
using LightRAGNet.Services.GraphCuration;

namespace LightRAGNet.Tests.GraphCuration;

public sealed class GraphCurationServiceTests
{
    [Fact]
    public void GraphCurationVectorIds_EntityId_UsesPythonStyleHashPrefix()
    {
        var id = GraphCurationVectorIds.Entity("ALPHA");

        id.Should().StartWith("ent-");
        id.Should().HaveLength("ent-".Length + 32);
    }

    [Fact]
    public void GraphCurationVectorIds_RelationIds_ReturnsCanonicalAndLegacyIds()
    {
        var ids = GraphCurationVectorIds.RelationIds("BETA", "ALPHA").ToList();

        ids.Should().HaveCount(2);
        ids[0].Should().Be(GraphCurationVectorIds.Relation("ALPHA", "BETA"));
        ids[1].Should().Be(GraphCurationVectorIds.Relation("BETA", "ALPHA"));
    }

    [Fact]
    public void EntityEditRequest_WhenDescriptionIsBlank_IsInvalid()
    {
        var request = new GraphEntityEditRequest(
            EntityName: "ALPHA",
            UpdatedData: new Dictionary<string, object> { ["description"] = " " },
            AllowRename: true,
            AllowMerge: false);

        request.HasBlankDescription().Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: fail because `LightRAGNet.Services.GraphCuration` types do not exist.

- [ ] **Step 3: Add operation result records**

Create `src/LightRAGNet/Services/GraphCuration/GraphCurationOperationResult.cs`:

```csharp
namespace LightRAGNet.Services.GraphCuration;

public sealed record GraphCurationOperationSummary(
    bool Merged,
    string MergeStatus,
    string? MergeError,
    string OperationStatus,
    string? TargetEntity,
    string FinalEntity,
    bool Renamed);

public sealed record GraphCurationOperationResult(
    bool Succeeded,
    string Status,
    string Message,
    Dictionary<string, object>? Data = null,
    GraphCurationOperationSummary? OperationSummary = null,
    string? FailureStage = null)
{
    public static GraphCurationOperationResult Success(
        string message,
        Dictionary<string, object>? data = null,
        GraphCurationOperationSummary? summary = null) =>
        new(true, "success", message, data, summary);

    public static GraphCurationOperationResult Failure(
        string message,
        string failureStage,
        string status = "failure",
        GraphCurationOperationSummary? summary = null) =>
        new(false, status, message, null, summary, failureStage);
}
```

- [ ] **Step 4: Add service request records**

Create `src/LightRAGNet/Services/GraphCuration/GraphCurationModels.cs`:

```csharp
namespace LightRAGNet.Services.GraphCuration;

public sealed record GraphEntityCreateRequest(
    string EntityName,
    Dictionary<string, object> EntityData);

public sealed record GraphEntityEditRequest(
    string EntityName,
    Dictionary<string, object> UpdatedData,
    bool AllowRename,
    bool AllowMerge)
{
    public bool HasBlankDescription() =>
        UpdatedData.TryGetValue("description", out var value) &&
        string.IsNullOrWhiteSpace(value?.ToString());
}

public sealed record GraphRelationCreateRequest(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> RelationData);

public sealed record GraphRelationEditRequest(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> UpdatedData)
{
    public bool HasBlankDescription() =>
        UpdatedData.TryGetValue("description", out var value) &&
        string.IsNullOrWhiteSpace(value?.ToString());
}

public sealed record GraphEntityMergeRequest(
    IReadOnlyList<string> SourceEntities,
    string TargetEntity);
```

- [ ] **Step 5: Add vector id helper**

Create `src/LightRAGNet/Services/GraphCuration/GraphCurationVectorIds.cs`:

```csharp
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.GraphCuration;

public static class GraphCurationVectorIds
{
    public static string Entity(string entityName) =>
        HashUtils.ComputeMd5Hash(entityName, "ent-");

    public static string Relation(string sourceEntity, string targetEntity) =>
        HashUtils.ComputeMd5Hash(sourceEntity + targetEntity, "rel-");

    public static IEnumerable<string> RelationIds(string sourceEntity, string targetEntity)
    {
        var ordered = NormalizePair(sourceEntity, targetEntity);
        yield return Relation(ordered.Source, ordered.Target);

        var legacy = Relation(ordered.Target, ordered.Source);
        if (!string.Equals(legacy, Relation(ordered.Source, ordered.Target), StringComparison.Ordinal))
        {
            yield return legacy;
        }
    }

    public static (string Source, string Target) NormalizePair(string sourceEntity, string targetEntity) =>
        string.Compare(sourceEntity, targetEntity, StringComparison.Ordinal) <= 0
            ? (sourceEntity, targetEntity)
            : (targetEntity, sourceEntity);
}
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: pass for the three new tests.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet/Services/GraphCuration tests/LightRAGNet.Tests/GraphCuration
git commit -m "feat: add graph curation contracts"
```

### Task 2: Backend Entity Create and Edit Service

**Files:**
- Create/modify: `src/LightRAGNet/Services/GraphCuration/GraphCurationService.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Test: `tests/LightRAGNet.Tests/GraphCuration/GraphCurationServiceTests.cs`

- [ ] **Step 1: Add failing tests for entity create and edit**

Append these tests to `GraphCurationServiceTests`:

```csharp
[Fact]
public async Task CreateEntityAsync_WhenEntityIsNew_WritesGraphVectorAndTracking()
{
    var fixture = GraphCurationFixture.Create();

    var result = await fixture.Service.CreateEntityAsync(new GraphEntityCreateRequest(
        "ALPHA",
        new Dictionary<string, object>
        {
            ["description"] = "Alpha description",
            ["entity_type"] = "Concept",
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md"
        }));

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededNode("ALPHA")!.Properties["description"].Should().Be("Alpha description");
    fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Content
        .Should().Be("ALPHA\nAlpha description");
    fixture.EntityChunks.Items["ALPHA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task EditEntityAsync_WhenDescriptionChanges_UpdatesGraphAndEntityVector()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new()
    {
        ["entity_id"] = "ALPHA",
        ["entity_type"] = "Concept",
        ["description"] = "old",
        ["source_id"] = "chunk-a",
        ["file_path"] = "doc.md"
    });

    var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
        "ALPHA",
        new Dictionary<string, object> { ["description"] = "new" },
        AllowRename: true,
        AllowMerge: false));

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededNode("ALPHA")!.Properties["description"].Should().Be("new");
    fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA"))!.Content
        .Should().Be("ALPHA\nnew");
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task EditEntityAsync_WhenRenameConflictsAndMergeDisabled_ReturnsConflict()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });

    var result = await fixture.Service.EditEntityAsync(new GraphEntityEditRequest(
        "ALPHA",
        new Dictionary<string, object> { ["entity_name"] = "BETA" },
        AllowRename: true,
        AllowMerge: false));

    result.Succeeded.Should().BeFalse();
    result.Status.Should().Be("conflict");
    fixture.Graph.GetSeededNode("ALPHA").Should().NotBeNull();
    fixture.QueryRevisionBumps.Should().Be(0);
}
```

Add this fixture at the bottom of the same file:

```csharp
private sealed class GraphCurationFixture
{
    public InMemoryGraphStore Graph { get; } = new();
    public InMemoryVectorStore VectorStore { get; } = new();
    public InMemoryKvStore FullEntities { get; } = new();
    public InMemoryKvStore FullRelations { get; } = new();
    public InMemoryKvStore EntityChunks { get; } = new();
    public InMemoryKvStore RelationChunks { get; } = new();
    public int QueryRevisionBumps { get; private set; }
    public GraphCurationService Service { get; }

    private GraphCurationFixture()
    {
        Service = new GraphCurationService(
            Graph,
            VectorStore,
            FullEntities,
            FullRelations,
            EntityChunks,
            RelationChunks,
            () =>
            {
                QueryRevisionBumps++;
                return Task.CompletedTask;
            },
            NullLogger<GraphCurationService>.Instance);
    }

    public static GraphCurationFixture Create() => new();
}
```

Add these usings:

```csharp
using LightRAGNet.Services.GraphCuration;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: fail because `GraphCurationService` does not exist and `InMemoryKvStore` namespace may need imports.

- [ ] **Step 3: Implement `GraphCurationService` for create/edit**

Create `src/LightRAGNet/Services/GraphCuration/GraphCurationService.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Storage;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.GraphCuration;

public sealed class GraphCurationService(
    IGraphStore graphStore,
    IVectorStore vectorStore,
    IKVStore fullEntitiesStore,
    IKVStore fullRelationsStore,
    IKVStore entityChunksStore,
    IKVStore relationChunksStore,
    Func<Task> bumpQueryRevisionAsync,
    ILogger<GraphCurationService> logger)
{
    public async Task<bool> EntityExistsAsync(string entityName, CancellationToken cancellationToken = default) =>
        await graphStore.HasNodeAsync(entityName, cancellationToken);

    public async Task<GraphCurationOperationResult> CreateEntityAsync(
        GraphEntityCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return GraphCurationOperationResult.Failure("Entity name is required.", "validate", "validation_error");
        }

        if (!request.EntityData.TryGetValue("description", out var descriptionValue) ||
            string.IsNullOrWhiteSpace(descriptionValue?.ToString()))
        {
            return GraphCurationOperationResult.Failure("Entity description is required.", "validate", "validation_error");
        }

        if (await graphStore.HasNodeAsync(request.EntityName, cancellationToken))
        {
            return GraphCurationOperationResult.Failure($"Entity '{request.EntityName}' already exists.", "validate", "conflict");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nodeData = new Dictionary<string, object>(request.EntityData, StringComparer.Ordinal)
        {
            ["entity_id"] = request.EntityName,
            ["entity_type"] = GetString(request.EntityData, "entity_type", "UNKNOWN"),
            ["description"] = descriptionValue!.ToString()!,
            ["source_id"] = GetString(request.EntityData, "source_id", "manual_creation"),
            ["file_path"] = GetString(request.EntityData, "file_path", "manual_creation"),
            ["created_at"] = now
        };

        await graphStore.UpsertNodeAsync(request.EntityName, nodeData, cancellationToken);
        await UpsertEntityVectorAsync(request.EntityName, nodeData, cancellationToken);
        await UpsertEntityTrackingAsync(request.EntityName, GetChunkIds(nodeData), cancellationToken);
        await fullEntitiesStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [request.EntityName] = new(nodeData, StringComparer.Ordinal)
        }, cancellationToken);

        await bumpQueryRevisionAsync();
        return GraphCurationOperationResult.Success("Entity created successfully.", new(nodeData, StringComparer.Ordinal));
    }

    public async Task<GraphCurationOperationResult> EditEntityAsync(
        GraphEntityEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.HasBlankDescription())
        {
            return GraphCurationOperationResult.Failure("Entity description is required.", "validate", "validation_error");
        }

        var current = await graphStore.GetNodeAsync(request.EntityName, cancellationToken);
        if (current is null)
        {
            return GraphCurationOperationResult.Failure($"Entity '{request.EntityName}' does not exist.", "load_entity", "not_found");
        }

        var newName = GetString(request.UpdatedData, "entity_name", request.EntityName);
        var isRenaming = !string.Equals(newName, request.EntityName, StringComparison.Ordinal);
        if (isRenaming && !request.AllowRename)
        {
            return GraphCurationOperationResult.Failure("Entity renaming is not allowed.", "validate", "validation_error");
        }

        if (isRenaming && await graphStore.HasNodeAsync(newName, cancellationToken))
        {
            if (!request.AllowMerge)
            {
                return GraphCurationOperationResult.Failure($"Entity '{newName}' already exists.", "validate", "conflict");
            }

            return await MergeEntitiesAsync(new GraphEntityMergeRequest([request.EntityName], newName), cancellationToken);
        }

        var updated = new Dictionary<string, object>(current.Properties, StringComparer.Ordinal);
        foreach (var pair in request.UpdatedData)
        {
            if (!string.Equals(pair.Key, "entity_name", StringComparison.Ordinal))
            {
                updated[pair.Key] = pair.Value;
            }
        }

        updated["entity_id"] = newName;

        if (isRenaming)
        {
            await graphStore.DeleteNodeAsync(request.EntityName, cancellationToken);
        }

        await graphStore.UpsertNodeAsync(newName, updated, cancellationToken);
        if (isRenaming)
        {
            await vectorStore.DeleteAsync("entities", [GraphCurationVectorIds.Entity(request.EntityName)], cancellationToken);
        }

        await UpsertEntityVectorAsync(newName, updated, cancellationToken);
        await UpsertEntityTrackingAsync(newName, GetChunkIds(updated), cancellationToken);
        await fullEntitiesStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [newName] = new(updated, StringComparer.Ordinal)
        }, cancellationToken);

        await bumpQueryRevisionAsync();

        var summary = new GraphCurationOperationSummary(
            Merged: false,
            MergeStatus: "not_attempted",
            MergeError: null,
            OperationStatus: "success",
            TargetEntity: isRenaming ? newName : null,
            FinalEntity: newName,
            Renamed: isRenaming);

        return GraphCurationOperationResult.Success("Entity updated successfully.", updated, summary);
    }

    public Task<GraphCurationOperationResult> MergeEntitiesAsync(
        GraphEntityMergeRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GraphCurationOperationResult.Failure("Entity merge requires the merge workflow.", "merge", "merge_required"));

    private async Task UpsertEntityVectorAsync(
        string entityName,
        Dictionary<string, object> nodeData,
        CancellationToken cancellationToken)
    {
        var description = GetString(nodeData, "description", string.Empty);
        var document = new VectorDocument
        {
            Id = GraphCurationVectorIds.Entity(entityName),
            Content = $"{entityName}\n{description}",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = entityName,
                ["source_id"] = GetString(nodeData, "source_id", string.Empty),
                ["description"] = description,
                ["entity_type"] = GetString(nodeData, "entity_type", "UNKNOWN"),
                ["file_path"] = GetString(nodeData, "file_path", "manual_creation")
            }
        };

        await vectorStore.UpsertAsync("entities", [document], cancellationToken);
    }

    private async Task UpsertEntityTrackingAsync(
        string entityName,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken)
    {
        await entityChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
        {
            [entityName] = new()
            {
                ["chunk_ids"] = chunkIds.Cast<object>().ToList(),
                ["count"] = chunkIds.Count
            }
        }, cancellationToken);
    }

    private static IReadOnlyList<string> GetChunkIds(Dictionary<string, object> data) =>
        GetString(data, "source_id", string.Empty)
            .Split("<SEP>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetString(Dictionary<string, object> data, string key, string fallback) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value!.ToString()!
            : fallback;
}
```

- [ ] **Step 4: Register service**

Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`:

```csharp
using LightRAGNet.Services.GraphCuration;
```

Register after `DocumentDeletionService`:

```csharp
services.AddSingleton(sp => new GraphCurationService(
    sp.GetRequiredService<IGraphStore>(),
    sp.GetRequiredService<IVectorStore>(),
    sp.GetRequiredKeyedService<IKVStore>(KVContracts.FullEntities),
    sp.GetRequiredKeyedService<IKVStore>(KVContracts.FullRelations),
    sp.GetRequiredKeyedService<IKVStore>(KVContracts.EntityChunks),
    sp.GetRequiredKeyedService<IKVStore>(KVContracts.RelationChunks),
    () => sp.GetRequiredService<LightRagLlmCacheService>()
        .BumpWorkspaceQueryRevisionAsync(
            sp.GetRequiredService<IOptions<LightRAGOptions>>().Value.Workspace,
            CancellationToken.None),
    sp.GetRequiredService<ILogger<GraphCurationService>>()));
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: all current GraphCuration tests pass except merge-specific behavior remains untested in this task.

- [ ] **Step 6: Build core projects**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet/Services/GraphCuration src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs tests/LightRAGNet.Tests/GraphCuration
git commit -m "feat: add graph entity curation service"
```

### Task 3: Relation Create and Edit Service

**Files:**
- Modify: `src/LightRAGNet/Services/GraphCuration/GraphCurationService.cs`
- Test: `tests/LightRAGNet.Tests/GraphCuration/GraphCurationServiceTests.cs`

- [ ] **Step 1: Add failing relation tests**

Append:

```csharp
[Fact]
public async Task CreateRelationAsync_WhenEndpointsExist_WritesGraphVectorAndTracking()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });

    var result = await fixture.Service.CreateRelationAsync(new GraphRelationCreateRequest(
        "BETA",
        "ALPHA",
        new Dictionary<string, object>
        {
            ["description"] = "Alpha relates to beta",
            ["keywords"] = "related",
            ["weight"] = 2.5,
            ["source_id"] = "chunk-a",
            ["file_path"] = "doc.md"
        }));

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["description"].Should().Be("Alpha relates to beta");
    fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA"))!.Content
        .Should().Contain("related");
    fixture.RelationChunks.Items["ALPHA<SEP>BETA"]["chunk_ids"].Should().BeEquivalentTo(new[] { "chunk-a" });
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task EditRelationAsync_WhenDescriptionChanges_UpdatesGraphAndRelationVector()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
    fixture.Graph.SeedEdge("ALPHA", "BETA", new()
    {
        ["description"] = "old",
        ["keywords"] = "old-keyword",
        ["source_id"] = "chunk-a",
        ["weight"] = 1.0
    });

    var result = await fixture.Service.EditRelationAsync(new GraphRelationEditRequest(
        "BETA",
        "ALPHA",
        new Dictionary<string, object> { ["description"] = "new", ["keywords"] = "new-keyword" }));

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededEdge("ALPHA", "BETA")!.Properties["description"].Should().Be("new");
    fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA"))!.Content
        .Should().Contain("new-keyword");
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task CreateRelationAsync_WhenEndpointMissing_ReturnsValidationError()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });

    var result = await fixture.Service.CreateRelationAsync(new GraphRelationCreateRequest(
        "ALPHA",
        "BETA",
        new Dictionary<string, object> { ["description"] = "rel" }));

    result.Succeeded.Should().BeFalse();
    result.Status.Should().Be("validation_error");
    fixture.QueryRevisionBumps.Should().Be(0);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: fail because relation methods are missing.

- [ ] **Step 3: Implement relation methods**

Add these methods to `GraphCurationService`:

```csharp
public async Task<GraphCurationOperationResult> CreateRelationAsync(
    GraphRelationCreateRequest request,
    CancellationToken cancellationToken = default)
{
    if (!await graphStore.HasNodeAsync(request.SourceEntity, cancellationToken) ||
        !await graphStore.HasNodeAsync(request.TargetEntity, cancellationToken))
    {
        return GraphCurationOperationResult.Failure("Both relation endpoints must exist.", "validate", "validation_error");
    }

    var pair = GraphCurationVectorIds.NormalizePair(request.SourceEntity, request.TargetEntity);
    if (await graphStore.HasEdgeAsync(pair.Source, pair.Target, cancellationToken))
    {
        return GraphCurationOperationResult.Failure("Relation already exists.", "validate", "conflict");
    }

    if (!request.RelationData.TryGetValue("description", out var descriptionValue) ||
        string.IsNullOrWhiteSpace(descriptionValue?.ToString()))
    {
        return GraphCurationOperationResult.Failure("Relation description is required.", "validate", "validation_error");
    }

    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var edgeData = new Dictionary<string, object>(request.RelationData, StringComparer.Ordinal)
    {
        ["description"] = descriptionValue!.ToString()!,
        ["keywords"] = GetString(request.RelationData, "keywords", string.Empty),
        ["source_id"] = GetString(request.RelationData, "source_id", "manual_creation"),
        ["file_path"] = GetString(request.RelationData, "file_path", "manual_creation"),
        ["weight"] = GetDouble(request.RelationData, "weight", 1.0),
        ["created_at"] = now
    };

    await graphStore.UpsertEdgeAsync(pair.Source, pair.Target, edgeData, cancellationToken);
    await UpsertRelationVectorAsync(pair.Source, pair.Target, edgeData, cancellationToken);
    await UpsertRelationTrackingAsync(pair.Source, pair.Target, GetChunkIds(edgeData), cancellationToken);
    await bumpQueryRevisionAsync();

    return GraphCurationOperationResult.Success("Relation created successfully.", new(edgeData, StringComparer.Ordinal));
}

public async Task<GraphCurationOperationResult> EditRelationAsync(
    GraphRelationEditRequest request,
    CancellationToken cancellationToken = default)
{
    if (request.HasBlankDescription())
    {
        return GraphCurationOperationResult.Failure("Relation description is required.", "validate", "validation_error");
    }

    var pair = GraphCurationVectorIds.NormalizePair(request.SourceEntity, request.TargetEntity);
    var current = await graphStore.GetEdgeAsync(pair.Source, pair.Target, cancellationToken);
    if (current is null)
    {
        return GraphCurationOperationResult.Failure("Relation does not exist.", "load_relation", "not_found");
    }

    var updated = new Dictionary<string, object>(current.Properties, StringComparer.Ordinal);
    foreach (var pairValue in request.UpdatedData)
    {
        updated[pairValue.Key] = pairValue.Value;
    }

    await graphStore.UpsertEdgeAsync(pair.Source, pair.Target, updated, cancellationToken);
    await vectorStore.DeleteAsync("relationships", GraphCurationVectorIds.RelationIds(pair.Source, pair.Target), cancellationToken);
    await UpsertRelationVectorAsync(pair.Source, pair.Target, updated, cancellationToken);
    await UpsertRelationTrackingAsync(pair.Source, pair.Target, GetChunkIds(updated), cancellationToken);
    await bumpQueryRevisionAsync();

    return GraphCurationOperationResult.Success("Relation updated successfully.", new(updated, StringComparer.Ordinal));
}

private async Task UpsertRelationVectorAsync(
    string sourceEntity,
    string targetEntity,
    Dictionary<string, object> edgeData,
    CancellationToken cancellationToken)
{
    var description = GetString(edgeData, "description", string.Empty);
    var keywords = GetString(edgeData, "keywords", string.Empty);
    var weight = GetDouble(edgeData, "weight", 1.0);

    var document = new VectorDocument
    {
        Id = GraphCurationVectorIds.Relation(sourceEntity, targetEntity),
        Content = $"{keywords}\t{sourceEntity}\n{targetEntity}\n{description}",
        Metadata = new Dictionary<string, object>
        {
            ["src_id"] = sourceEntity,
            ["tgt_id"] = targetEntity,
            ["source_id"] = GetString(edgeData, "source_id", string.Empty),
            ["description"] = description,
            ["keywords"] = keywords,
            ["weight"] = weight,
            ["file_path"] = GetString(edgeData, "file_path", "manual_creation")
        }
    };

    await vectorStore.UpsertAsync("relationships", [document], cancellationToken);
}

private async Task UpsertRelationTrackingAsync(
    string sourceEntity,
    string targetEntity,
    IReadOnlyList<string> chunkIds,
    CancellationToken cancellationToken)
{
    var key = $"{sourceEntity}<SEP>{targetEntity}";
    await relationChunksStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
    {
        [key] = new()
        {
            ["chunk_ids"] = chunkIds.Cast<object>().ToList(),
            ["count"] = chunkIds.Count
        }
    }, cancellationToken);
}

private static double GetDouble(Dictionary<string, object> data, string key, double fallback) =>
    data.TryGetValue(key, out var value) && double.TryParse(value?.ToString(), out var parsed)
        ? parsed
        : fallback;
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/Services/GraphCuration tests/LightRAGNet.Tests/GraphCuration
git commit -m "feat: add relation curation service"
```

### Task 4: Entity Merge and Delete Service

**Files:**
- Modify: `src/LightRAGNet/Services/GraphCuration/GraphCurationService.cs`
- Test: `tests/LightRAGNet.Tests/GraphCuration/GraphCurationServiceTests.cs`

- [ ] **Step 1: Add failing merge and delete tests**

Append:

```csharp
[Fact]
public async Task MergeEntitiesAsync_TransfersRelationsAndDeletesSourceVector()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha", ["source_id"] = "chunk-a" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta", ["source_id"] = "chunk-b" });
    fixture.Graph.SeedNode("GAMMA", new() { ["entity_id"] = "GAMMA", ["description"] = "gamma" });
    fixture.Graph.SeedEdge("ALPHA", "GAMMA", new() { ["description"] = "alpha gamma", ["keywords"] = "ag", ["source_id"] = "chunk-a" });
    fixture.VectorStore.Seed("entities", new VectorDocument { Id = GraphCurationVectorIds.Entity("ALPHA"), Content = "ALPHA\nalpha" });

    var result = await fixture.Service.MergeEntitiesAsync(new GraphEntityMergeRequest(["ALPHA"], "BETA"));

    result.Succeeded.Should().BeTrue();
    result.OperationSummary!.Merged.Should().BeTrue();
    fixture.Graph.GetSeededNode("ALPHA").Should().BeNull();
    fixture.Graph.GetSeededEdge("BETA", "GAMMA").Should().NotBeNull();
    fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA")).Should().BeNull();
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task DeleteRelationAsync_RemovesGraphVectorAndTracking()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
    fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["description"] = "rel", ["keywords"] = "k", ["source_id"] = "chunk-a" });
    fixture.RelationChunks.Seed("ALPHA<SEP>BETA", new() { ["chunk_ids"] = new List<object> { "chunk-a" }, ["count"] = 1 });
    fixture.VectorStore.Seed("relationships", new VectorDocument { Id = GraphCurationVectorIds.Relation("ALPHA", "BETA"), Content = "rel" });

    var result = await fixture.Service.DeleteRelationAsync("BETA", "ALPHA");

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededEdge("ALPHA", "BETA").Should().BeNull();
    fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA")).Should().BeNull();
    fixture.RelationChunks.Items.Should().NotContainKey("ALPHA<SEP>BETA");
    fixture.QueryRevisionBumps.Should().Be(1);
}

[Fact]
public async Task DeleteEntityAsync_RemovesNodeAndConnectedRelationVectors()
{
    var fixture = GraphCurationFixture.Create();
    fixture.Graph.SeedNode("ALPHA", new() { ["entity_id"] = "ALPHA", ["description"] = "alpha" });
    fixture.Graph.SeedNode("BETA", new() { ["entity_id"] = "BETA", ["description"] = "beta" });
    fixture.Graph.SeedEdge("ALPHA", "BETA", new() { ["description"] = "rel", ["keywords"] = "k" });
    fixture.VectorStore.Seed("entities", new VectorDocument { Id = GraphCurationVectorIds.Entity("ALPHA"), Content = "ALPHA\nalpha" });
    fixture.VectorStore.Seed("relationships", new VectorDocument { Id = GraphCurationVectorIds.Relation("ALPHA", "BETA"), Content = "rel" });

    var result = await fixture.Service.DeleteEntityAsync("ALPHA");

    result.Succeeded.Should().BeTrue();
    fixture.Graph.GetSeededNode("ALPHA").Should().BeNull();
    fixture.VectorStore.Get("entities", GraphCurationVectorIds.Entity("ALPHA")).Should().BeNull();
    fixture.VectorStore.Get("relationships", GraphCurationVectorIds.Relation("ALPHA", "BETA")).Should().BeNull();
    fixture.QueryRevisionBumps.Should().Be(1);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: fail because merge/delete methods are missing or still return `merge_required`.

- [ ] **Step 3: Implement merge/delete methods**

Replace the `MergeEntitiesAsync` placeholder and add delete methods:

```csharp
public async Task<GraphCurationOperationResult> MergeEntitiesAsync(
    GraphEntityMergeRequest request,
    CancellationToken cancellationToken = default)
{
    if (!await graphStore.HasNodeAsync(request.TargetEntity, cancellationToken))
    {
        return GraphCurationOperationResult.Failure("Target entity does not exist.", "validate", "validation_error");
    }

    var target = await graphStore.GetNodeAsync(request.TargetEntity, cancellationToken);
    var mergedData = new Dictionary<string, object>(target!.Properties, StringComparer.Ordinal);
    var transferred = 0;

    foreach (var sourceEntity in request.SourceEntities.Distinct(StringComparer.Ordinal))
    {
        if (string.Equals(sourceEntity, request.TargetEntity, StringComparison.Ordinal))
        {
            return GraphCurationOperationResult.Failure("Source entities cannot include target entity.", "validate", "validation_error");
        }

        var source = await graphStore.GetNodeAsync(sourceEntity, cancellationToken);
        if (source is null)
        {
            return GraphCurationOperationResult.Failure($"Source entity '{sourceEntity}' does not exist.", "validate", "validation_error");
        }

        mergedData["description"] = JoinUnique(GetString(mergedData, "description", string.Empty), GetString(source.Properties, "description", string.Empty));
        mergedData["source_id"] = JoinUnique(GetString(mergedData, "source_id", string.Empty), GetString(source.Properties, "source_id", string.Empty));

        var edges = await graphStore.GetNodeEdgesAsync(sourceEntity, cancellationToken);
        foreach (var edge in edges)
        {
            var other = string.Equals(edge.SourceId, sourceEntity, StringComparison.Ordinal)
                ? edge.TargetId
                : edge.SourceId;
            if (string.Equals(other, request.TargetEntity, StringComparison.Ordinal))
            {
                continue;
            }

            var oldPair = GraphCurationVectorIds.NormalizePair(edge.SourceId, edge.TargetId);
            var oldEdge = await graphStore.GetEdgeAsync(oldPair.Source, oldPair.Target, cancellationToken);
            if (oldEdge is null)
            {
                continue;
            }

            var newPair = GraphCurationVectorIds.NormalizePair(request.TargetEntity, other);
            var existing = await graphStore.GetEdgeAsync(newPair.Source, newPair.Target, cancellationToken);
            var edgeData = existing is null
                ? new Dictionary<string, object>(oldEdge.Properties, StringComparer.Ordinal)
                : MergeRelationData(existing.Properties, oldEdge.Properties);

            await graphStore.UpsertEdgeAsync(newPair.Source, newPair.Target, edgeData, cancellationToken);
            await vectorStore.DeleteAsync("relationships", GraphCurationVectorIds.RelationIds(oldPair.Source, oldPair.Target), cancellationToken);
            await UpsertRelationVectorAsync(newPair.Source, newPair.Target, edgeData, cancellationToken);
            await UpsertRelationTrackingAsync(newPair.Source, newPair.Target, GetChunkIds(edgeData), cancellationToken);
            transferred++;
        }

        await graphStore.DeleteNodeAsync(sourceEntity, cancellationToken);
        await vectorStore.DeleteAsync("entities", [GraphCurationVectorIds.Entity(sourceEntity)], cancellationToken);
        await entityChunksStore.DeleteAsync([sourceEntity], cancellationToken);
    }

    await graphStore.UpsertNodeAsync(request.TargetEntity, mergedData, cancellationToken);
    await UpsertEntityVectorAsync(request.TargetEntity, mergedData, cancellationToken);
    await UpsertEntityTrackingAsync(request.TargetEntity, GetChunkIds(mergedData), cancellationToken);
    await bumpQueryRevisionAsync();

    var summary = new GraphCurationOperationSummary(
        Merged: true,
        MergeStatus: "success",
        MergeError: null,
        OperationStatus: "success",
        TargetEntity: request.TargetEntity,
        FinalEntity: request.TargetEntity,
        Renamed: true);

    var data = new Dictionary<string, object>(mergedData, StringComparer.Ordinal)
    {
        ["relationships_transferred"] = transferred,
        ["deleted_entities"] = request.SourceEntities.ToList()
    };
    return GraphCurationOperationResult.Success("Entities merged successfully.", data, summary);
}

public async Task<GraphCurationOperationResult> DeleteRelationAsync(
    string sourceEntity,
    string targetEntity,
    CancellationToken cancellationToken = default)
{
    var pair = GraphCurationVectorIds.NormalizePair(sourceEntity, targetEntity);
    if (!await graphStore.HasEdgeAsync(pair.Source, pair.Target, cancellationToken))
    {
        return GraphCurationOperationResult.Failure("Relation does not exist.", "load_relation", "not_found");
    }

    await graphStore.RemoveEdgesAsync([(pair.Source, pair.Target)], cancellationToken);
    await vectorStore.DeleteAsync("relationships", GraphCurationVectorIds.RelationIds(pair.Source, pair.Target), cancellationToken);
    await relationChunksStore.DeleteAsync([$"{pair.Source}<SEP>{pair.Target}"], cancellationToken);
    await bumpQueryRevisionAsync();

    return GraphCurationOperationResult.Success("Relation deleted successfully.");
}

public async Task<GraphCurationOperationResult> DeleteEntityAsync(
    string entityName,
    CancellationToken cancellationToken = default)
{
    if (!await graphStore.HasNodeAsync(entityName, cancellationToken))
    {
        return GraphCurationOperationResult.Failure("Entity does not exist.", "load_entity", "not_found");
    }

    var edges = await graphStore.GetNodeEdgesAsync(entityName, cancellationToken);
    foreach (var edge in edges)
    {
        var pair = GraphCurationVectorIds.NormalizePair(edge.SourceId, edge.TargetId);
        await vectorStore.DeleteAsync("relationships", GraphCurationVectorIds.RelationIds(pair.Source, pair.Target), cancellationToken);
        await relationChunksStore.DeleteAsync([$"{pair.Source}<SEP>{pair.Target}"], cancellationToken);
    }

    await graphStore.DeleteNodeAsync(entityName, cancellationToken);
    await vectorStore.DeleteAsync("entities", [GraphCurationVectorIds.Entity(entityName)], cancellationToken);
    await entityChunksStore.DeleteAsync([entityName], cancellationToken);
    await bumpQueryRevisionAsync();

    return GraphCurationOperationResult.Success("Entity deleted successfully.");
}

private static Dictionary<string, object> MergeRelationData(
    Dictionary<string, object> target,
    Dictionary<string, object> source) =>
    new(target, StringComparer.Ordinal)
    {
        ["description"] = JoinUnique(GetString(target, "description", string.Empty), GetString(source, "description", string.Empty)),
        ["keywords"] = JoinUnique(GetString(target, "keywords", string.Empty), GetString(source, "keywords", string.Empty)),
        ["source_id"] = JoinUnique(GetString(target, "source_id", string.Empty), GetString(source, "source_id", string.Empty)),
        ["file_path"] = JoinUnique(GetString(target, "file_path", string.Empty), GetString(source, "file_path", string.Empty)),
        ["weight"] = Math.Max(GetDouble(target, "weight", 1.0), GetDouble(source, "weight", 1.0))
    };

private static string JoinUnique(string left, string right)
{
    var values = left.Split("<SEP>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Concat(right.Split("<SEP>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.Ordinal)
        .ToList();
    return string.Join("<SEP>", values);
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src/LightRAGNet/Services/GraphCuration tests/LightRAGNet.Tests/GraphCuration
git commit -m "feat: add graph merge and delete curation"
```

### Task 5: Server Graph API Contracts

**Files:**
- Create: `src/LightRAGNet.Share/Models/GraphCurationModels.cs`
- Create: `src/LightRAGNet.Server/Controllers/GraphController.cs`
- Test: `tests/LightRAGNet.Server.Tests/GraphControllerTests.cs`

- [ ] **Step 1: Add failing API tests**

Create `tests/LightRAGNet.Server.Tests/GraphControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class GraphControllerTests
{
    [Fact]
    public async Task EntityExists_WhenEntityMissing_ReturnsFalse()
    {
        await using var factory = new LightRagServerFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GraphEntityExistsResponse>("/api/graph/entity/exists?name=ALPHA");

        result!.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task CreateEntity_WhenDescriptionMissing_ReturnsBadRequest()
    {
        await using var factory = new LightRagServerFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/graph/entity", new GraphEntityCreateDto(
            EntityName: "ALPHA",
            EntityData: new Dictionary<string, object>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run API tests and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphController" --verbosity minimal
```

Expected: fail because DTOs and controller do not exist.

- [ ] **Step 3: Add shared DTOs**

Create `src/LightRAGNet.Share/Models/GraphCurationModels.cs`:

```csharp
namespace LightRAGNet.Share.Models;

public sealed record GraphEntityExistsResponse(bool Exists);

public sealed record GraphEntityCreateDto(
    string EntityName,
    Dictionary<string, object> EntityData);

public sealed record GraphEntityEditDto(
    Dictionary<string, object> UpdatedData,
    bool AllowRename = true,
    bool AllowMerge = false);

public sealed record GraphRelationCreateDto(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> RelationData);

public sealed record GraphRelationEditDto(
    string SourceEntity,
    string TargetEntity,
    Dictionary<string, object> UpdatedData);

public sealed record GraphEntityMergeDto(
    IReadOnlyList<string> SourceEntities,
    string TargetEntity);

public sealed record GraphCurationResponse(
    bool Succeeded,
    string Status,
    string Message,
    Dictionary<string, object>? Data,
    GraphCurationSummaryDto? OperationSummary,
    string? FailureStage);

public sealed record GraphCurationSummaryDto(
    bool Merged,
    string MergeStatus,
    string? MergeError,
    string OperationStatus,
    string? TargetEntity,
    string FinalEntity,
    bool Renamed);
```

- [ ] **Step 4: Add controller**

Create `src/LightRAGNet.Server/Controllers/GraphController.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Share.Models;
using LightRAGNet.Services.GraphCuration;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/graph")]
public sealed class GraphController(
    IGraphStore graphStore,
    GraphCurationService curationService,
    ILogger<GraphController> logger) : ControllerBase
{
    [HttpGet("entity/exists")]
    public async Task<ActionResult<GraphEntityExistsResponse>> EntityExists(
        [FromQuery] string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "name is required" });
        }

        return Ok(new GraphEntityExistsResponse(await curationService.EntityExistsAsync(name, cancellationToken)));
    }

    [HttpPost("entity")]
    public async Task<ActionResult<GraphCurationResponse>> CreateEntity(
        [FromBody] GraphEntityCreateDto request,
        CancellationToken cancellationToken)
    {
        var result = await curationService.CreateEntityAsync(
            new GraphEntityCreateRequest(request.EntityName, request.EntityData),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("entity/{name}")]
    public async Task<ActionResult<GraphCurationResponse>> EditEntity(
        string name,
        [FromBody] GraphEntityEditDto request,
        CancellationToken cancellationToken)
    {
        var result = await curationService.EditEntityAsync(
            new GraphEntityEditRequest(name, request.UpdatedData, request.AllowRename, request.AllowMerge),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("relation")]
    public async Task<ActionResult<GraphCurationResponse>> CreateRelation(
        [FromBody] GraphRelationCreateDto request,
        CancellationToken cancellationToken)
    {
        var result = await curationService.CreateRelationAsync(
            new GraphRelationCreateRequest(request.SourceEntity, request.TargetEntity, request.RelationData),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("relation")]
    public async Task<ActionResult<GraphCurationResponse>> EditRelation(
        [FromBody] GraphRelationEditDto request,
        CancellationToken cancellationToken)
    {
        var result = await curationService.EditRelationAsync(
            new GraphRelationEditRequest(request.SourceEntity, request.TargetEntity, request.UpdatedData),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("entities/merge")]
    public async Task<ActionResult<GraphCurationResponse>> MergeEntities(
        [FromBody] GraphEntityMergeDto request,
        CancellationToken cancellationToken)
    {
        var result = await curationService.MergeEntitiesAsync(
            new GraphEntityMergeRequest(request.SourceEntities, request.TargetEntity),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("entity/{name}")]
    public async Task<ActionResult<GraphCurationResponse>> DeleteEntity(
        string name,
        CancellationToken cancellationToken)
    {
        var result = await curationService.DeleteEntityAsync(name, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("relation")]
    public async Task<ActionResult<GraphCurationResponse>> DeleteRelation(
        [FromQuery] string source,
        [FromQuery] string target,
        CancellationToken cancellationToken)
    {
        var result = await curationService.DeleteRelationAsync(source, target, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("labels")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetLabels(CancellationToken cancellationToken)
    {
        return Ok(await graphStore.GetPopularLabelsAsync(300, cancellationToken));
    }

    private ActionResult<GraphCurationResponse> ToActionResult(GraphCurationOperationResult result)
    {
        var response = new GraphCurationResponse(
            result.Succeeded,
            result.Status,
            result.Message,
            result.Data,
            result.OperationSummary is null
                ? null
                : new GraphCurationSummaryDto(
                    result.OperationSummary.Merged,
                    result.OperationSummary.MergeStatus,
                    result.OperationSummary.MergeError,
                    result.OperationSummary.OperationStatus,
                    result.OperationSummary.TargetEntity,
                    result.OperationSummary.FinalEntity,
                    result.OperationSummary.Renamed),
            result.FailureStage);

        if (result.Succeeded)
        {
            return Ok(response);
        }

        return result.Status switch
        {
            "not_found" => NotFound(response),
            "conflict" => Conflict(response),
            "validation_error" => BadRequest(response),
            _ => StatusCode(StatusCodes.Status500InternalServerError, response)
        };
    }
}
```

- [ ] **Step 5: Run server API tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphController" --verbosity minimal
```

Expected: pass. If `LightRagServerFactory` does not currently override `GraphCurationService` dependencies, update the factory to use the same test doubles already used for graph view tests.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet.Share/Models/GraphCurationModels.cs src/LightRAGNet.Server/Controllers/GraphController.cs tests/LightRAGNet.Server.Tests/GraphControllerTests.cs
git commit -m "feat: expose graph curation APIs"
```

### Task 6: React/Vite Workbench Scaffold

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/package.json`
- Create: `src/LightRAGNet.Web/ClientApp/vite.config.ts`
- Create: `src/LightRAGNet.Web/ClientApp/tsconfig.json`
- Create: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
- Modify: `src/LightRAGNet.Web/Components/Pages/GraphView.razor`
- Test: `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`

- [ ] **Step 1: Add failing host source test**

Create `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Web.Tests;

public sealed class GraphWorkbenchHostSourceTests
{
    [Fact]
    public void GraphView_HostsReactWorkbenchMountPoint()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LightRAGNet.Web",
            "Components",
            "Pages",
            "GraphView.razor"));

        source.Should().Contain("graph-workbench-root");
        source.Should().Contain("graph-workbench");
    }

    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(current, "LightRAGNet.slnx")))
        {
            current = Directory.GetParent(current)!.FullName;
        }

        return current;
    }
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal
```

Expected: fail because GraphView does not host React bundle.

- [ ] **Step 3: Create Vite package**

Create `src/LightRAGNet.Web/ClientApp/package.json`:

```json
{
  "name": "lightragnet-web-client",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite --host 127.0.0.1",
    "build": "vite build",
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "dependencies": {
    "@react-sigma/core": "^5.0.6",
    "@react-sigma/graph-search": "^5.0.6",
    "@react-sigma/layout-circular": "^5.0.6",
    "@react-sigma/layout-forceatlas2": "^5.0.6",
    "graphology": "^0.26.0",
    "lucide-react": "^1.14.0",
    "react": "^19.2.6",
    "react-dom": "^19.2.6",
    "sigma": "^3.0.3",
    "zustand": "^5.0.13"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^6.0.1",
    "typescript": "~5.9.3",
    "vite": "^8.0.12",
    "vitest": "^4.0.17",
    "@types/react": "^19.2.14",
    "@types/react-dom": "^19.2.3"
  }
}
```

Create `vite.config.ts`:

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { resolve } from 'node:path'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot/graph-workbench',
    emptyOutDir: true,
    manifest: true,
    assetsDir: 'assets',
    rollupOptions: {
      input: {
        graphWorkbench: resolve(__dirname, 'src/graph-workbench/main.tsx')
      },
      output: {
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]'
      }
    }
  }
})
```

Create `tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "useDefineForClassFields": true,
    "lib": ["DOM", "DOM.Iterable", "ES2022"],
    "allowJs": false,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "allowSyntheticDefaultImports": true,
    "strict": true,
    "forceConsistentCasingInFileNames": true,
    "module": "ESNext",
    "moduleResolution": "Node",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx"
  },
  "include": ["src"]
}
```

- [ ] **Step 4: Add minimal React workbench**

Create `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`:

```typescript
import React from 'react'
import { createRoot } from 'react-dom/client'
import { GraphWorkbench } from './GraphWorkbench'
import '../styles/graph-workbench.css'

const root = document.getElementById('graph-workbench-root')

if (root) {
  const apiBase = root.dataset.apiBase ?? ''
  createRoot(root).render(
    <React.StrictMode>
      <GraphWorkbench apiBase={apiBase} />
    </React.StrictMode>
  )
}
```

Create `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`:

```typescript
type GraphWorkbenchProps = {
  apiBase: string
}

export function GraphWorkbench({ apiBase }: GraphWorkbenchProps) {
  return (
    <main className="graph-workbench" data-api-base={apiBase}>
      <aside className="graph-workbench__sidebar">
        <h1>Knowledge Graph</h1>
        <p>React graph workbench is ready.</p>
      </aside>
      <section className="graph-workbench__canvas">
        <div className="graph-workbench__empty">Load a graph to begin curation.</div>
      </section>
    </main>
  )
}
```

Create `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`:

```css
.graph-workbench {
  display: grid;
  grid-template-columns: 320px minmax(0, 1fr);
  height: calc(100vh - 80px);
  min-height: 560px;
  background: #f8fafc;
  color: #111827;
}

.graph-workbench__sidebar {
  border-right: 1px solid #d1d5db;
  padding: 16px;
  background: #ffffff;
  overflow: auto;
}

.graph-workbench__canvas {
  position: relative;
  min-width: 0;
  min-height: 0;
}

.graph-workbench__empty {
  display: grid;
  height: 100%;
  place-items: center;
  color: #64748b;
}
```

- [ ] **Step 5: Replace Blazor graph page host**

Modify `src/LightRAGNet.Web/Components/Pages/GraphView.razor` to:

```razor
@page "/graph-view"

<PageTitle>Knowledge Graph Workbench</PageTitle>

<div id="graph-workbench-root" data-api-base=""></div>

<link rel="stylesheet" href="graph-workbench/assets/graphWorkbench.css" />
<script type="module" src="graph-workbench/assets/graphWorkbench.js"></script>
```

The Vite config uses stable `graphWorkbench.js` and `graph-workbench.css` output names so the Blazor host can load deterministic asset paths.

- [ ] **Step 6: Run host test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Install and build React app**

Run:

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm install
npm run build
Set-Location ..\..\..
```

Expected: Vite emits files under `src/LightRAGNet.Web/wwwroot/graph-workbench`.

- [ ] **Step 8: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp src/LightRAGNet.Web/Components/Pages/GraphView.razor src/LightRAGNet.Web/wwwroot/graph-workbench tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs
git commit -m "feat: scaffold react graph workbench"
```

### Task 7: React API Client and Store

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/types/graph.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/graphApi.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/stores/graphSettingsStore.ts`
- Test: `src/LightRAGNet.Web/ClientApp/src/api/graphApi.test.ts`
- Test: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`

- [ ] **Step 1: Add frontend API tests**

Create `src/LightRAGNet.Web/ClientApp/src/api/graphApi.test.ts`:

```typescript
import { describe, expect, it, vi } from 'vitest'
import { editEntity, editRelation } from './graphApi'

describe('graphApi', () => {
  it('serializes entity edit requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ succeeded: true, status: 'success', message: 'ok' })
    })
    globalThis.fetch = fetchMock as unknown as typeof fetch

    await editEntity('', 'ALPHA', { description: 'new' }, true, false)

    expect(fetchMock).toHaveBeenCalledWith('/api/graph/entity/ALPHA', expect.objectContaining({
      method: 'PATCH',
      body: JSON.stringify({
        updatedData: { description: 'new' },
        allowRename: true,
        allowMerge: false
      })
    }))
  })

  it('serializes relation edit requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ succeeded: true, status: 'success', message: 'ok' })
    })
    globalThis.fetch = fetchMock as unknown as typeof fetch

    await editRelation('', 'ALPHA', 'BETA', { keywords: 'new' })

    expect(fetchMock).toHaveBeenCalledWith('/api/graph/relation', expect.objectContaining({
      method: 'PATCH',
      body: JSON.stringify({
        sourceEntity: 'ALPHA',
        targetEntity: 'BETA',
        updatedData: { keywords: 'new' }
      })
    }))
  })
})
```

- [ ] **Step 2: Add graph store tests**

Create `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`:

```typescript
import { describe, expect, it } from 'vitest'
import { createGraphStoreState } from './graphStore'

describe('graphStore', () => {
  it('updates selected node property after entity edit', () => {
    const state = createGraphStoreState()
    state.setGraph({
      nodes: [{ id: 'ALPHA', label: 'ALPHA', properties: { entity_id: 'ALPHA', description: 'old' } }],
      edges: [],
      isTruncated: false
    })
    state.setSelectedNode('ALPHA')

    state.updateNodeProperty('ALPHA', 'description', 'new')

    expect(state.rawGraph.nodes[0].properties.description).toBe('new')
    expect(state.selectedNodeId).toBe('ALPHA')
  })
})
```

- [ ] **Step 3: Run frontend tests and verify failure**

Run:

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm test
Set-Location ..\..\..
```

Expected: fail because modules do not exist.

- [ ] **Step 4: Add graph types**

Create `src/LightRAGNet.Web/ClientApp/src/types/graph.ts`:

```typescript
export type GraphNodeDto = {
  id: string
  label: string
  type?: string | null
  color?: string
  size?: number
  properties: Record<string, unknown>
}

export type GraphEdgeDto = {
  id: string
  source: string
  target: string
  type?: string | null
  color?: string
  size?: number
  properties?: Record<string, unknown>
}

export type GraphViewDto = {
  nodes: GraphNodeDto[]
  edges: GraphEdgeDto[]
  isTruncated: boolean
}

export type GraphCurationResponse = {
  succeeded: boolean
  status: string
  message: string
  data?: Record<string, unknown>
  operationSummary?: {
    merged: boolean
    mergeStatus: string
    mergeError?: string | null
    operationStatus: string
    targetEntity?: string | null
    finalEntity: string
    renamed: boolean
  } | null
  failureStage?: string | null
}
```

- [ ] **Step 5: Add API client**

Create `src/LightRAGNet.Web/ClientApp/src/api/graphApi.ts`:

```typescript
import type { GraphCurationResponse, GraphViewDto } from '../types/graph'

const jsonHeaders = { 'Content-Type': 'application/json' }

function joinUrl(apiBase: string, path: string) {
  return `${apiBase}${path}`
}

async function readJson<T>(response: Response): Promise<T> {
  const body = await response.json()
  if (!response.ok) {
    throw new Error(body?.message ?? body?.error ?? `Request failed with ${response.status}`)
  }
  return body as T
}

export async function queryGraph(apiBase: string, label: string, maxDepth: number, maxNodes: number) {
  const params = new URLSearchParams({
    nodeLabel: label,
    maxDepth: String(maxDepth),
    maxNodes: String(maxNodes)
  })
  const response = await fetch(joinUrl(apiBase, `/api/GraphView?${params}`))
  return readJson<GraphViewDto>(response)
}

export async function getGraphLabels(apiBase: string) {
  const response = await fetch(joinUrl(apiBase, '/api/graph/labels'))
  return readJson<string[]>(response)
}

export async function checkEntityNameExists(apiBase: string, name: string) {
  const response = await fetch(joinUrl(apiBase, `/api/graph/entity/exists?name=${encodeURIComponent(name)}`))
  const data = await readJson<{ exists: boolean }>(response)
  return data.exists
}

export async function editEntity(
  apiBase: string,
  entityName: string,
  updatedData: Record<string, unknown>,
  allowRename: boolean,
  allowMerge: boolean
) {
  const response = await fetch(joinUrl(apiBase, `/api/graph/entity/${encodeURIComponent(entityName)}`), {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify({ updatedData, allowRename, allowMerge })
  })
  return readJson<GraphCurationResponse>(response)
}

export async function editRelation(
  apiBase: string,
  sourceEntity: string,
  targetEntity: string,
  updatedData: Record<string, unknown>
) {
  const response = await fetch(joinUrl(apiBase, '/api/graph/relation'), {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify({ sourceEntity, targetEntity, updatedData })
  })
  return readJson<GraphCurationResponse>(response)
}
```

- [ ] **Step 6: Add graph stores**

Create `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`:

```typescript
import { create } from 'zustand'
import type { GraphViewDto } from '../types/graph'

export type GraphStoreState = {
  rawGraph: GraphViewDto
  selectedNodeId: string | null
  selectedEdgeId: string | null
  isFetching: boolean
  setGraph: (graph: GraphViewDto) => void
  setSelectedNode: (nodeId: string | null) => void
  setSelectedEdge: (edgeId: string | null) => void
  setFetching: (isFetching: boolean) => void
  updateNodeProperty: (nodeId: string, propertyName: string, value: unknown) => void
}

export function createGraphStoreState(): GraphStoreState {
  const state: GraphStoreState = {
    rawGraph: { nodes: [], edges: [], isTruncated: false },
    selectedNodeId: null,
    selectedEdgeId: null,
    isFetching: false,
    setGraph: (graph) => {
      state.rawGraph = graph
    },
    setSelectedNode: (nodeId) => {
      state.selectedNodeId = nodeId
      state.selectedEdgeId = null
    },
    setSelectedEdge: (edgeId) => {
      state.selectedEdgeId = edgeId
      state.selectedNodeId = null
    },
    setFetching: (isFetching) => {
      state.isFetching = isFetching
    },
    updateNodeProperty: (nodeId, propertyName, value) => {
      state.rawGraph = {
        ...state.rawGraph,
        nodes: state.rawGraph.nodes.map((node) =>
          node.id === nodeId
            ? { ...node, properties: { ...node.properties, [propertyName]: value } }
            : node
        )
      }
    }
  }

  return state
}

export const useGraphStore = create<GraphStoreState>(() => createGraphStoreState())
```

Create `src/LightRAGNet.Web/ClientApp/src/stores/graphSettingsStore.ts`:

```typescript
import { create } from 'zustand'

type GraphSettingsStore = {
  queryLabel: string
  maxDepth: number
  maxNodes: number
  setQueryLabel: (queryLabel: string) => void
  setMaxDepth: (maxDepth: number) => void
  setMaxNodes: (maxNodes: number) => void
}

export const useGraphSettingsStore = create<GraphSettingsStore>((set) => ({
  queryLabel: '*',
  maxDepth: 2,
  maxNodes: 100,
  setQueryLabel: (queryLabel) => set({ queryLabel }),
  setMaxDepth: (maxDepth) => set({ maxDepth }),
  setMaxNodes: (maxNodes) => set({ maxNodes })
}))
```

- [ ] **Step 7: Run frontend tests**

Run:

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm test
Set-Location ..\..\..
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/api src/LightRAGNet.Web/ClientApp/src/stores src/LightRAGNet.Web/ClientApp/src/types
git commit -m "feat: add graph workbench client state"
```

### Task 8: Graph Canvas, Selection, and Property Editing UI

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphToolbar.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertyEditDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/MergeDialog.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`

- [ ] **Step 1: Add GraphCanvas**

Create `GraphCanvas.tsx`:

```typescript
import { useMemo } from 'react'
import { SigmaContainer, useRegisterEvents } from '@react-sigma/core'
import Graph from 'graphology'
import type { GraphViewDto } from '../../types/graph'
import { useGraphStore } from '../../stores/graphStore'
import '@react-sigma/core/lib/style.css'

type GraphCanvasProps = {
  graph: GraphViewDto
}

function buildGraph(data: GraphViewDto) {
  const graph = new Graph({ type: 'undirected' })
  data.nodes.forEach((node, index) => {
    const angle = (index / Math.max(data.nodes.length, 1)) * Math.PI * 2
    graph.addNode(node.id, {
      label: node.label,
      x: Math.cos(angle) * 100,
      y: Math.sin(angle) * 100,
      size: node.size ?? 8,
      color: node.color ?? '#64748b',
      properties: node.properties
    })
  })
  data.edges.forEach((edge) => {
    if (graph.hasNode(edge.source) && graph.hasNode(edge.target) && !graph.hasEdge(edge.id)) {
      graph.addEdgeWithKey(edge.id, edge.source, edge.target, {
        label: edge.type ?? '',
        size: edge.size ?? 1,
        color: edge.color ?? '#cbd5e1',
        properties: edge.properties ?? {}
      })
    }
  })
  return graph
}

function GraphEvents() {
  const registerEvents = useRegisterEvents()
  const setSelectedNode = useGraphStore((state) => state.setSelectedNode)
  const setSelectedEdge = useGraphStore((state) => state.setSelectedEdge)

  registerEvents({
    clickNode: (event) => setSelectedNode(event.node),
    clickEdge: (event) => setSelectedEdge(event.edge),
    clickStage: () => {
      setSelectedNode(null)
      setSelectedEdge(null)
    }
  })

  return null
}

export function GraphCanvas({ graph }: GraphCanvasProps) {
  const sigmaGraph = useMemo(() => buildGraph(graph), [graph])

  return (
    <SigmaContainer graph={sigmaGraph} className="graph-canvas">
      <GraphEvents />
    </SigmaContainer>
  )
}
```

- [ ] **Step 2: Add toolbar**

Create `GraphToolbar.tsx`:

```typescript
type GraphToolbarProps = {
  label: string
  maxDepth: number
  maxNodes: number
  isLoading: boolean
  onLabelChange: (value: string) => void
  onMaxDepthChange: (value: number) => void
  onMaxNodesChange: (value: number) => void
  onLoad: () => void
}

export function GraphToolbar({
  label,
  maxDepth,
  maxNodes,
  isLoading,
  onLabelChange,
  onMaxDepthChange,
  onMaxNodesChange,
  onLoad
}: GraphToolbarProps) {
  return (
    <div className="graph-toolbar">
      <label>
        <span>Start label</span>
        <input value={label} onChange={(event) => onLabelChange(event.target.value)} />
      </label>
      <label>
        <span>Depth</span>
        <input type="number" min={1} max={5} value={maxDepth} onChange={(event) => onMaxDepthChange(Number(event.target.value))} />
      </label>
      <label>
        <span>Max nodes</span>
        <input type="number" min={10} max={1000} value={maxNodes} onChange={(event) => onMaxNodesChange(Number(event.target.value))} />
      </label>
      <button type="button" onClick={onLoad} disabled={isLoading}>
        {isLoading ? 'Loading...' : 'Load Graph'}
      </button>
    </div>
  )
}
```

- [ ] **Step 3: Add property edit dialog and merge dialog**

Create `PropertyEditDialog.tsx`:

```typescript
type PropertyEditDialogProps = {
  title: string
  value: string
  allowMerge?: boolean
  isSaving: boolean
  error?: string | null
  onValueChange: (value: string) => void
  onAllowMergeChange?: (value: boolean) => void
  onCancel: () => void
  onSave: () => void
}

export function PropertyEditDialog({
  title,
  value,
  allowMerge,
  isSaving,
  error,
  onValueChange,
  onAllowMergeChange,
  onCancel,
  onSave
}: PropertyEditDialogProps) {
  return (
    <div className="graph-dialog__backdrop">
      <section className="graph-dialog">
        <h2>{title}</h2>
        <textarea value={value} onChange={(event) => onValueChange(event.target.value)} />
        {onAllowMergeChange && (
          <label className="graph-dialog__check">
            <input type="checkbox" checked={allowMerge ?? false} onChange={(event) => onAllowMergeChange(event.target.checked)} />
            Allow merge if the new entity name already exists
          </label>
        )}
        {error && <div className="graph-dialog__error">{error}</div>}
        <footer>
          <button type="button" onClick={onCancel} disabled={isSaving}>Cancel</button>
          <button type="button" onClick={onSave} disabled={isSaving || value.trim().length === 0}>Save</button>
        </footer>
      </section>
    </div>
  )
}
```

Create `MergeDialog.tsx`:

```typescript
type MergeDialogProps = {
  sourceEntity: string
  targetEntity: string
  onUseMergedStart: () => void
  onKeepCurrentStart: () => void
}

export function MergeDialog({
  sourceEntity,
  targetEntity,
  onUseMergedStart,
  onKeepCurrentStart
}: MergeDialogProps) {
  return (
    <div className="graph-dialog__backdrop">
      <section className="graph-dialog">
        <h2>Entity merged</h2>
        <p>{sourceEntity} was merged into {targetEntity}.</p>
        <p>Refresh the graph to avoid editing stale nodes.</p>
        <footer>
          <button type="button" onClick={onKeepCurrentStart}>Refresh current graph</button>
          <button type="button" onClick={onUseMergedStart}>Use merged entity</button>
        </footer>
      </section>
    </div>
  )
}
```

- [ ] **Step 4: Add properties panel**

Create `PropertiesPanel.tsx`:

```typescript
import { useState } from 'react'
import { editEntity, editRelation } from '../../api/graphApi'
import { useGraphStore } from '../../stores/graphStore'
import type { GraphEdgeDto, GraphNodeDto } from '../../types/graph'
import { PropertyEditDialog } from './PropertyEditDialog'

type PropertiesPanelProps = {
  apiBase: string
}

export function PropertiesPanel({ apiBase }: PropertiesPanelProps) {
  const graph = useGraphStore((state) => state.rawGraph)
  const selectedNodeId = useGraphStore((state) => state.selectedNodeId)
  const selectedEdgeId = useGraphStore((state) => state.selectedEdgeId)
  const updateNodeProperty = useGraphStore((state) => state.updateNodeProperty)
  const [editing, setEditing] = useState<{ property: string; value: string; kind: 'node' | 'edge' } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)

  const node = selectedNodeId ? graph.nodes.find((item) => item.id === selectedNodeId) : null
  const edge = selectedEdgeId ? graph.edges.find((item) => item.id === selectedEdgeId) : null

  const save = async () => {
    if (!editing) return
    setIsSaving(true)
    setError(null)
    try {
      if (editing.kind === 'node' && node) {
        const field = editing.property === 'entity_id' ? 'entity_name' : editing.property
        const response = await editEntity(apiBase, String(node.properties.entity_id ?? node.id), { [field]: editing.value }, true, false)
        if (!response.succeeded) throw new Error(response.message)
        updateNodeProperty(node.id, editing.property, editing.value)
      }
      if (editing.kind === 'edge' && edge) {
        const response = await editRelation(apiBase, edge.source, edge.target, { [editing.property]: editing.value })
        if (!response.succeeded) throw new Error(response.message)
      }
      setEditing(null)
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'Save failed')
    } finally {
      setIsSaving(false)
    }
  }

  if (!node && !edge) {
    return <aside className="properties-panel properties-panel--empty">Select a node or edge.</aside>
  }

  return (
    <aside className="properties-panel">
      {node && <NodeProperties node={node} onEdit={(property, value) => setEditing({ property, value, kind: 'node' })} />}
      {edge && <EdgeProperties edge={edge} onEdit={(property, value) => setEditing({ property, value, kind: 'edge' })} />}
      {editing && (
        <PropertyEditDialog
          title={`Edit ${editing.property}`}
          value={editing.value}
          isSaving={isSaving}
          error={error}
          onValueChange={(value) => setEditing({ ...editing, value })}
          onCancel={() => setEditing(null)}
          onSave={save}
        />
      )}
    </aside>
  )
}

function NodeProperties({ node, onEdit }: { node: GraphNodeDto; onEdit: (property: string, value: string) => void }) {
  return (
    <>
      <h2>Entity</h2>
      {['entity_id', 'entity_type', 'description'].map((property) => (
        <PropertyRow
          key={property}
          name={property}
          value={String(node.properties[property] ?? '')}
          editable
          onEdit={() => onEdit(property, String(node.properties[property] ?? ''))}
        />
      ))}
    </>
  )
}

function EdgeProperties({ edge, onEdit }: { edge: GraphEdgeDto; onEdit: (property: string, value: string) => void }) {
  const props = edge.properties ?? {}
  return (
    <>
      <h2>Relation</h2>
      <PropertyRow name="source" value={edge.source} />
      <PropertyRow name="target" value={edge.target} />
      {['description', 'keywords', 'weight'].map((property) => (
        <PropertyRow
          key={property}
          name={property}
          value={String(props[property] ?? '')}
          editable
          onEdit={() => onEdit(property, String(props[property] ?? ''))}
        />
      ))}
    </>
  )
}

function PropertyRow({ name, value, editable, onEdit }: { name: string; value: string; editable?: boolean; onEdit?: () => void }) {
  return (
    <div className="property-row">
      <span>{name}</span>
      <button type="button" onClick={editable ? onEdit : undefined} disabled={!editable}>
        {value || '-'}
      </button>
    </div>
  )
}
```

- [ ] **Step 5: Wire workbench**

Modify `GraphWorkbench.tsx`:

```typescript
import { queryGraph } from '../api/graphApi'
import { GraphCanvas } from '../components/graph/GraphCanvas'
import { GraphToolbar } from '../components/graph/GraphToolbar'
import { PropertiesPanel } from '../components/graph/PropertiesPanel'
import { useGraphStore } from '../stores/graphStore'
import { useGraphSettingsStore } from '../stores/graphSettingsStore'

type GraphWorkbenchProps = {
  apiBase: string
}

export function GraphWorkbench({ apiBase }: GraphWorkbenchProps) {
  const graph = useGraphStore((state) => state.rawGraph)
  const isFetching = useGraphStore((state) => state.isFetching)
  const setGraph = useGraphStore((state) => state.setGraph)
  const setFetching = useGraphStore((state) => state.setFetching)
  const label = useGraphSettingsStore((state) => state.queryLabel)
  const maxDepth = useGraphSettingsStore((state) => state.maxDepth)
  const maxNodes = useGraphSettingsStore((state) => state.maxNodes)
  const setLabel = useGraphSettingsStore((state) => state.setQueryLabel)
  const setMaxDepth = useGraphSettingsStore((state) => state.setMaxDepth)
  const setMaxNodes = useGraphSettingsStore((state) => state.setMaxNodes)

  const loadGraph = async () => {
    setFetching(true)
    try {
      setGraph(await queryGraph(apiBase, label || '*', maxDepth, maxNodes))
    } finally {
      setFetching(false)
    }
  }

  return (
    <main className="graph-workbench">
      <aside className="graph-workbench__sidebar">
        <GraphToolbar
          label={label}
          maxDepth={maxDepth}
          maxNodes={maxNodes}
          isLoading={isFetching}
          onLabelChange={setLabel}
          onMaxDepthChange={setMaxDepth}
          onMaxNodesChange={setMaxNodes}
          onLoad={loadGraph}
        />
      </aside>
      <section className="graph-workbench__canvas">
        {graph.nodes.length === 0 ? <div className="graph-workbench__empty">Load a graph to begin curation.</div> : <GraphCanvas graph={graph} />}
      </section>
      <PropertiesPanel apiBase={apiBase} />
    </main>
  )
}
```

- [ ] **Step 6: Expand CSS**

Append to `graph-workbench.css`:

```css
.graph-canvas {
  height: 100%;
  width: 100%;
}

.graph-toolbar {
  display: grid;
  gap: 12px;
}

.graph-toolbar label {
  display: grid;
  gap: 4px;
  font-size: 13px;
}

.graph-toolbar input,
.graph-toolbar button,
.property-row button,
.graph-dialog textarea {
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  padding: 8px;
}

.properties-panel {
  position: absolute;
  right: 16px;
  top: 16px;
  z-index: 10;
  width: 340px;
  max-height: calc(100vh - 120px);
  overflow: auto;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.92);
  padding: 12px;
  backdrop-filter: blur(10px);
}

.property-row {
  display: grid;
  grid-template-columns: 110px minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  margin: 6px 0;
}

.property-row button {
  overflow: hidden;
  text-align: left;
  text-overflow: ellipsis;
  white-space: nowrap;
  background: #f8fafc;
}

.graph-dialog__backdrop {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: grid;
  place-items: center;
  background: rgba(15, 23, 42, 0.35);
}

.graph-dialog {
  width: min(560px, calc(100vw - 32px));
  border-radius: 8px;
  background: #fff;
  padding: 16px;
  box-shadow: 0 20px 48px rgba(15, 23, 42, 0.24);
}

.graph-dialog textarea {
  min-height: 180px;
  width: 100%;
}

.graph-dialog footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}

.graph-dialog__error {
  margin-top: 8px;
  color: #b91c1c;
}
```

- [ ] **Step 7: Build React app**

Run:

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm test
npm run build
Set-Location ..\..\..
```

Expected: tests and build pass.

- [ ] **Step 8: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp
git commit -m "feat: add graph workbench editing UI"
```

### Task 9: Destructive Actions and Merge Refresh UX

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/api/graphApi.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/MergeDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/ConfirmDialog.tsx`

- [ ] **Step 1: Add delete/create/merge API functions**

Append to `graphApi.ts`:

```typescript
export async function createEntity(apiBase: string, entityName: string, entityData: Record<string, unknown>) {
  const response = await fetch(joinUrl(apiBase, '/api/graph/entity'), {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ entityName, entityData })
  })
  return readJson<GraphCurationResponse>(response)
}

export async function createRelation(
  apiBase: string,
  sourceEntity: string,
  targetEntity: string,
  relationData: Record<string, unknown>
) {
  const response = await fetch(joinUrl(apiBase, '/api/graph/relation'), {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ sourceEntity, targetEntity, relationData })
  })
  return readJson<GraphCurationResponse>(response)
}

export async function mergeEntities(apiBase: string, sourceEntities: string[], targetEntity: string) {
  const response = await fetch(joinUrl(apiBase, '/api/graph/entities/merge'), {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ sourceEntities, targetEntity })
  })
  return readJson<GraphCurationResponse>(response)
}

export async function deleteEntity(apiBase: string, entityName: string) {
  const response = await fetch(joinUrl(apiBase, `/api/graph/entity/${encodeURIComponent(entityName)}`), {
    method: 'DELETE'
  })
  return readJson<GraphCurationResponse>(response)
}

export async function deleteRelation(apiBase: string, sourceEntity: string, targetEntity: string) {
  const params = new URLSearchParams({ source: sourceEntity, target: targetEntity })
  const response = await fetch(joinUrl(apiBase, `/api/graph/relation?${params}`), {
    method: 'DELETE'
  })
  return readJson<GraphCurationResponse>(response)
}
```

- [ ] **Step 2: Add confirm dialog**

Create `ConfirmDialog.tsx`:

```typescript
type ConfirmDialogProps = {
  title: string
  message: string
  confirmText: string
  onCancel: () => void
  onConfirm: () => void
}

export function ConfirmDialog({ title, message, confirmText, onCancel, onConfirm }: ConfirmDialogProps) {
  return (
    <div className="graph-dialog__backdrop">
      <section className="graph-dialog">
        <h2>{title}</h2>
        <p>{message}</p>
        <footer>
          <button type="button" onClick={onCancel}>Cancel</button>
          <button type="button" className="danger" onClick={onConfirm}>{confirmText}</button>
        </footer>
      </section>
    </div>
  )
}
```

- [ ] **Step 3: Wire delete buttons in `PropertiesPanel`**

In `PropertiesPanel.tsx`, import delete functions and `ConfirmDialog`:

```typescript
import { deleteEntity, deleteRelation, editEntity, editRelation } from '../../api/graphApi'
import { ConfirmDialog } from './ConfirmDialog'
```

Add local state:

```typescript
const [confirming, setConfirming] = useState<{ kind: 'node' | 'edge'; title: string; message: string } | null>(null)
```

Add delete handler:

```typescript
const confirmDelete = async () => {
  if (!confirming) return
  if (confirming.kind === 'node' && node) {
    const entityName = String(node.properties.entity_id ?? node.id)
    const response = await deleteEntity(apiBase, entityName)
    if (!response.succeeded) throw new Error(response.message)
    useGraphStore.getState().setGraph({
      ...graph,
      nodes: graph.nodes.filter((item) => item.id !== node.id),
      edges: graph.edges.filter((item) => item.source !== node.id && item.target !== node.id)
    })
  }
  if (confirming.kind === 'edge' && edge) {
    const response = await deleteRelation(apiBase, edge.source, edge.target)
    if (!response.succeeded) throw new Error(response.message)
    useGraphStore.getState().setGraph({
      ...graph,
      edges: graph.edges.filter((item) => item.id !== edge.id)
    })
  }
  setConfirming(null)
}
```

Render delete actions and dialog:

```tsx
{node && (
  <button
    type="button"
    className="danger"
    onClick={() => setConfirming({
      kind: 'node',
      title: 'Delete entity',
      message: `Delete entity ${String(node.properties.entity_id ?? node.id)} and related relations? This cannot be undone.`
    })}
  >
    Delete entity
  </button>
)}
{edge && (
  <button
    type="button"
    className="danger"
    onClick={() => setConfirming({
      kind: 'edge',
      title: 'Delete relation',
      message: `Delete relation ${edge.source} - ${edge.target}? This cannot be undone.`
    })}
  >
    Delete relation
  </button>
)}
{confirming && (
  <ConfirmDialog
    title={confirming.title}
    message={confirming.message}
    confirmText="Delete"
    onCancel={() => setConfirming(null)}
    onConfirm={() => void confirmDelete()}
  />
)}
```

- [ ] **Step 4: Add danger styles**

Append CSS:

```css
button.danger {
  border-color: #fca5a5;
  background: #fee2e2;
  color: #991b1b;
}
```

- [ ] **Step 5: Run frontend build**

Run:

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm test
npm run build
Set-Location ..\..\..
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp
git commit -m "feat: add graph destructive actions"
```

### Task 10: Documentation, Verification, and Archive Source Declaration

**Files:**
- Modify: `README.md`
- Modify: `README.CN.md`
- Create: `docs/superpowers/archives/2026-05/2026-05-21-graph-curation-react-workbench-archives.md`
- Modify: `docs/superpowers/archives/INDEX.md`

- [ ] **Step 1: Update README build commands**

Add to `README.md` under development commands:

```markdown
### React Graph Workbench

The graph workbench is a React/Vite island hosted by the Blazor web app.

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm install
npm run build
Set-Location ..\..\..
dotnet run --project .\src\LightRAGNet.Web
```
```

Add to `README.CN.md`:

```markdown
### React 图谱工作台

图谱工作台是由 Blazor Web 临时承载的 React/Vite island，后续可迁移为主 React 前端模块。

```powershell
Set-Location .\src\LightRAGNet.Web\ClientApp
npm install
npm run build
Set-Location ..\..\..
dotnet run --project .\src\LightRAGNet.Web
```
```

- [ ] **Step 2: Run backend, frontend, and solution checks**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphController" --verbosity minimal
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal
Set-Location .\src\LightRAGNet.Web\ClientApp
npm test
npm run build
Set-Location ..\..\..
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: all commands pass. If full solution tests fail due to an existing unrelated test, capture the failing test name and rerun the focused test groups above before deciding whether the issue belongs to this feature.

- [ ] **Step 3: Manual UI verification**

Run server and web:

```powershell
dotnet run --project .\src\LightRAGNet.Server
dotnet run --project .\src\LightRAGNet.Web
```

Open the Web app and verify:

```text
1. Navigate to /graph-view.
2. Load graph with label "*", depth 2, max nodes 100.
3. Select a node and confirm the property panel opens.
4. Edit node description and confirm refresh preserves the new value.
5. Select an edge and edit keywords.
6. Delete a relation through the confirmation dialog.
7. Rename an entity to an existing entity with merge enabled and choose the merged entity refresh path.
```

- [ ] **Step 4: Create archive with source declaration**

Create `docs/superpowers/archives/2026-05/2026-05-21-graph-curation-react-workbench-archives.md`:

```markdown
# Graph Curation React Workbench Archives

- Date: `2026-05-21`
- Topic slug: `graph-curation-react-workbench`
- Status: `Completed`

## Summary

Implemented a React/Vite graph workbench hosted by the existing Blazor Web app and added graph curation APIs for entity/relation edit, create, merge, and delete.

## Reference Source Declaration

This feature intentionally references the Python LightRAG source repository:

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`
  - `LightRAG/lightrag_webui/src/stores/settings.ts`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/EditablePropertyRow.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/MergeDialog.tsx`
  - `LightRAG/lightrag_webui/src/api/lightrag.ts`
- Referenced backend areas:
  - `LightRAG/lightrag/api/routers/graph_routes.py`
  - `LightRAG/lightrag/lightrag.py`
  - `LightRAG/lightrag/utils_graph.py`

The implementation ports the product semantics and UI structure rather than claiming the graph workbench design as original to this repository.

## Delivered

- Added backend graph curation service and API contracts.
- Added React/Vite graph workbench island for future React migration.
- Added node and edge selection, property editing, merge decision flow, and destructive action confirmations.
- Preserved Blazor as temporary host only.

## Verification

- `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal`
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphController" --verbosity minimal`
- `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal`
- `npm test` from `src/LightRAGNet.Web/ClientApp`
- `npm run build` from `src/LightRAGNet.Web/ClientApp`
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`
- `dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal`

## Notes

The React graph workbench is intentionally kept independent from Blazor component internals so it can be lifted into a future full React frontend.
```

- [ ] **Step 5: Update archive index**

Add to `docs/superpowers/archives/INDEX.md` under `2026-05`:

```markdown
- [2026-05-21-graph-curation-react-workbench-archives.md](./2026-05/2026-05-21-graph-curation-react-workbench-archives.md): 引入 React/Vite 图谱工作台和图谱治理 API，对齐 Python LightRAG 图谱编辑、属性面板和实体合并语义，并保留参考来源声明。
```

- [ ] **Step 6: Run archive/index checks**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_indexes.py . --json
```

Expected: archive index is valid. If the script path has moved, locate `check_indexes.py` under `C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding` and rerun the same command.

- [ ] **Step 7: Commit documentation and archive**

```powershell
git add README.md README.CN.md docs/superpowers/archives
git commit -m "docs: archive react graph curation workbench"
```

---

## Self-Review

### Spec Coverage

- Python-style graph workbench: Tasks 6-9.
- React migration bridge: Tasks 6, 7, 8, and README updates in Task 10.
- Backend curation APIs: Tasks 1-5.
- Entity/relation create/edit/delete/merge: Tasks 2-5 and 9.
- Store/vector/KV/tracking consistency: Tasks 2-4.
- Query revision bump: Tasks 2-4.
- UI validation of editing flow: Tasks 8-10.
- Reference source declaration: Task 10, plus the explicit requirement at the top of this plan.

### Placeholder Scan

This plan intentionally avoids placeholder markers and vague deferred-work steps. Each task names exact files, commands, and expected outcomes.

### Type Consistency

Service-level request records use `GraphEntityCreateRequest`, `GraphEntityEditRequest`, `GraphRelationCreateRequest`, `GraphRelationEditRequest`, and `GraphEntityMergeRequest`. API DTOs use the `Dto` suffix and map explicitly to service request records. React client request property names match the shared DTO names in camelCase JSON.
