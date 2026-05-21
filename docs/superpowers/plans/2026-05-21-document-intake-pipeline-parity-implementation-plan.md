# Document Intake Pipeline Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Python-style document intake pipeline semantics to the existing Markdown document flow: submit text/files, return a `track_id`, process through the existing single RAG task worker, expose status/retry/cancel APIs, and show basic operations in the Web document list.

**Architecture:** Extend the existing `MarkdownDocuments` model/controller instead of creating a parallel document system. SQLite `MarkdownDocuments` remains the user-visible status source; `IRagTaskQueueService` remains the single-worker execution queue; API and Web changes read/write pipeline status through the Server database and task queue boundary.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core SQLite migrations, Blazor Server, MudBlazor, xUnit, FluentAssertions, existing `LightRagServerFactory` test isolation.

---

## Scope Check

This plan implements one subsystem: document intake pipeline parity for existing Markdown/text documents. It does not implement PDF/Office/image multimodal parsing, multi-worker concurrency, distributed queues, prompt/query changes, external storage integration tests, or full upload wizard UI.

## File Structure

- Modify: `src/LightRAGNet.Server/Models/MarkdownDocument.cs`
  - Add SQLite-backed pipeline metadata: `TrackId`, `RagCurrentStage`, `ActiveRagTaskId`, `PipelineStartedAt`, `PipelineCompletedAt`, `PipelineCancelledAt`, `RagRetryCount`.
- Modify: `src/LightRAGNet.Server/Data/AppDbContext.cs`
  - Configure new fields and indexes for `TrackId`, `RagStatus`, and `ActiveRagTaskId`.
- Create: `src/LightRAGNet.Server/Migrations/<timestamp>_AddDocumentIntakePipelineFields.cs`
  - EF migration for the new SQLite columns and indexes.
- Modify: `src/LightRAGNet.Share/Models/MarkdownDocumentDto.cs`
  - Surface status fields to API/Web.
- Create: `src/LightRAGNet.Share/Models/DocumentIntakeModels.cs`
  - Request/response models for text submission, track status, retry, and cancel operations.
- Modify: `src/LightRAGNet.Server/Extensions/MarkdownModelMapper.cs`
  - Map new metadata fields.
- Create: `src/LightRAGNet.Server/Services/DocumentIntakeStatus.cs`
  - Central string constants for user-visible statuses.
- Create: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
  - Owns validation, row creation, track creation, enqueue, retry, cancel, and track aggregation.
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
  - Add text intake, batch upload, track status, retry, cancel, status filtering.
  - Keep existing upload/delete/clear endpoints compatible.
- Modify: `src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs`
  - Map task status to document pipeline status and persist current stage/timestamps.
- Modify: `src/LightRAGNet/Models/RagTask.cs`
  - Add `Cancelled` status and optional `TrackId`.
- Modify: `src/LightRAGNet/Services/TaskQueue/IRagTaskQueueService.cs`
  - Add overloads/methods needed by document-level retry/cancel and track metadata.
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskQueueService.cs`
  - Support `Cancelled`, document-level cancellation, retry of cancelled/failed persisted document tasks, and track id propagation.
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`
  - Restore interrupted `Processing` tasks to `Failed`, not `Pending`.
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Register `DocumentIntakeService`.
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
  - Add status filtering, track status, retry, and cancel client calls.
- Modify: `src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor`
  - Add status filter and retry/cancel actions for the existing document table.
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`
  - API contract tests.
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`
  - Queue retry/cancel/status behavior.
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`
  - Startup recovery behavior.
- Test: `tests/LightRAGNet.Tests/Web/MarkdownDocumentsSourceTests.cs`
  - Source-level Web behavior checks, matching existing Web test style.

## Status Vocabulary

Use these user-visible document statuses in `MarkdownDocument.RagStatus`:

```csharp
namespace LightRAGNet.Server.Services;

public static class DocumentIntakeStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Deleting = "Deleting";
    public const string DeletionFailed = "DeletionFailed";

    public static bool IsRetryable(string? status)
    {
        return status is Failed or Cancelled;
    }

    public static bool IsCancellable(string? status)
    {
        return status is Queued or Processing;
    }

    public static bool IsActive(string? status)
    {
        return status is Queued or Processing or "Pending" or Deleting;
    }
}
```

`RagTaskStatus.Pending` remains an internal queue status. Server/API maps it to user-visible `Queued`.

## Task 1: Add Pipeline Metadata And DTO Contract

**Files:**
- Modify: `src/LightRAGNet.Server/Models/MarkdownDocument.cs`
- Modify: `src/LightRAGNet.Server/Data/AppDbContext.cs`
- Modify: `src/LightRAGNet.Share/Models/MarkdownDocumentDto.cs`
- Create: `src/LightRAGNet.Share/Models/DocumentIntakeModels.cs`
- Modify: `src/LightRAGNet.Server/Extensions/MarkdownModelMapper.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentIntakeStatus.cs`
- Create migration with `dotnet ef migrations add AddDocumentIntakePipelineFields --project src/LightRAGNet.Server --startup-project src/LightRAGNet.Server`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Write the failing API metadata test**

Create `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentIntakePipelineApiTests
{
    [Fact]
    public async Task GetMarkdownDocuments_WhenStatusAndTrackExist_ReturnsPipelineMetadata()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 101,
            FileName = "alpha.md",
            Content = "alpha",
            FileSize = 5,
            TrackId = "track-alpha",
            RagStatus = "Queued",
            RagCurrentStage = "Accepted",
            ActiveRagTaskId = "task-alpha",
            RagRetryCount = 2
        });
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
            "/api/MarkdownDocuments?page=1&pageSize=10");

        result.Should().NotBeNull();
        var document = result!.Items.Should().ContainSingle(d => d.Id == 101).Subject;
        document.TrackId.Should().Be("track-alpha");
        document.RagStatus.Should().Be("Queued");
        document.RagCurrentStage.Should().Be("Accepted");
        document.ActiveRagTaskId.Should().Be("task-alpha");
        document.RagRetryCount.Should().Be(2);
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Run the metadata test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: FAIL with compile errors because metadata properties do not exist.

- [ ] **Step 3: Add model and DTO fields**

Add to `src/LightRAGNet.Server/Models/MarkdownDocument.cs`:

```csharp
public string? TrackId { get; set; }

public string? RagCurrentStage { get; set; }

public string? ActiveRagTaskId { get; set; }

public DateTime? PipelineStartedAt { get; set; }

public DateTime? PipelineCompletedAt { get; set; }

public DateTime? PipelineCancelledAt { get; set; }

public int RagRetryCount { get; set; }
```

Add matching properties to `src/LightRAGNet.Share/Models/MarkdownDocumentDto.cs`:

```csharp
public string? TrackId { get; set; }

public string? ActiveRagTaskId { get; set; }

public DateTime? PipelineStartedAt { get; set; }

public DateTime? PipelineCompletedAt { get; set; }

public DateTime? PipelineCancelledAt { get; set; }

public int RagRetryCount { get; set; }
```

Keep the existing `RagCurrentStage` property in the DTO and make it map from the database field.

- [ ] **Step 4: Configure EF fields and mapper**

Add to the `MarkdownDocument` entity block in `src/LightRAGNet.Server/Data/AppDbContext.cs`:

```csharp
entity.Property(e => e.TrackId).HasMaxLength(100);
entity.Property(e => e.RagCurrentStage).HasMaxLength(100);
entity.Property(e => e.ActiveRagTaskId).HasMaxLength(100);
entity.Property(e => e.RagRetryCount).IsRequired().HasDefaultValue(0);
entity.HasIndex(e => e.TrackId);
entity.HasIndex(e => e.RagStatus);
entity.HasIndex(e => e.ActiveRagTaskId);
```

Update `src/LightRAGNet.Server/Extensions/MarkdownModelMapper.cs` so `ToDto` assigns:

```csharp
TrackId = model.TrackId,
RagCurrentStage = model.RagCurrentStage,
ActiveRagTaskId = model.ActiveRagTaskId,
PipelineStartedAt = model.PipelineStartedAt,
PipelineCompletedAt = model.PipelineCompletedAt,
PipelineCancelledAt = model.PipelineCancelledAt,
RagRetryCount = model.RagRetryCount,
```

- [ ] **Step 5: Add shared intake models**

Create `src/LightRAGNet.Share/Models/DocumentIntakeModels.cs`:

```csharp
namespace LightRAGNet.Share.Models;

public sealed class SubmitTextDocumentsRequest
{
    public List<TextDocumentInput> Documents { get; set; } = [];
}

public sealed class TextDocumentInput
{
    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class DocumentSubmissionResponse
{
    public string TrackId { get; set; } = string.Empty;

    public List<MarkdownDocumentDto> Documents { get; set; } = [];
}

public sealed class DocumentTrackStatusResponse
{
    public string TrackId { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public int QueuedCount { get; set; }

    public int ProcessingCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public List<MarkdownDocumentDto> Documents { get; set; } = [];
}

public sealed class DocumentPipelineActionResult
{
    public bool Accepted { get; set; }

    public int DocumentId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}
```

- [ ] **Step 6: Add status constants**

Create `src/LightRAGNet.Server/Services/DocumentIntakeStatus.cs` using the exact code from the `Status Vocabulary` section.

- [ ] **Step 7: Create and inspect EF migration**

Run:

```powershell
dotnet ef migrations add AddDocumentIntakePipelineFields --project .\src\LightRAGNet.Server --startup-project .\src\LightRAGNet.Server
```

Expected: migration adds nullable text/datetime columns and integer `RagRetryCount` with default `0`. Inspect the migration and `AppDbContextModelSnapshot`; it must not drop or rename existing columns.

- [ ] **Step 8: Run metadata test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add document pipeline metadata"
```

## Task 2: Add Text Intake Service And Track Status API

**Files:**
- Create: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Add failing text submission and track tests**

Append these tests to `DocumentIntakePipelineApiTests`:

```csharp
[Fact]
public async Task SubmitTextDocuments_CreatesSingleTrackAndQueuedDocuments()
{
    using var factory = new LightRagServerFactory();
    using var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/MarkdownDocuments/text", new SubmitTextDocumentsRequest
    {
        Documents =
        [
            new TextDocumentInput { FileName = "a.md", Content = "alpha" },
            new TextDocumentInput { FileName = "b.md", Content = "beta" }
        ]
    });

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
    body.Should().NotBeNull();
    body!.TrackId.Should().NotBeNullOrWhiteSpace();
    body.Documents.Should().HaveCount(2);
    body.Documents.Select(d => d.TrackId).Should().OnlyContain(id => id == body.TrackId);
    body.Documents.Select(d => d.RagStatus).Should().OnlyContain(status => status == "Queued");
}

[Fact]
public async Task GetTrackStatus_ReturnsAllDocumentsAndAggregatesCounts()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 201,
        FileName = "done.md",
        Content = "done",
        TrackId = "track-201",
        RagStatus = "Completed"
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 202,
        FileName = "failed.md",
        Content = "failed",
        TrackId = "track-201",
        RagStatus = "Failed"
    });
    using var client = factory.CreateClient();

    var body = await client.GetFromJsonAsync<DocumentTrackStatusResponse>(
        "/api/MarkdownDocuments/tracks/track-201");

    body.Should().NotBeNull();
    body!.TrackId.Should().Be("track-201");
    body.TotalCount.Should().Be(2);
    body.CompletedCount.Should().Be(1);
    body.FailedCount.Should().Be(1);
    body.Documents.Select(d => d.Id).Should().BeEquivalentTo([201, 202]);
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: FAIL because `/text` and `/tracks/{trackId}` do not exist.

- [ ] **Step 3: Implement `DocumentIntakeService` creation and track aggregation**

Create `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Extensions;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services;

public sealed class DocumentIntakeService(
    AppDbContext context,
    IRagTaskQueueService taskQueueService,
    ILogger<DocumentIntakeService> logger)
{
    public async Task<DocumentSubmissionResponse> SubmitTextDocumentsAsync(
        SubmitTextDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Documents.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(request));
        }

        if (request.Documents.Any(d =>
                string.IsNullOrWhiteSpace(d.FileName) ||
                string.IsNullOrWhiteSpace(d.Content)))
        {
            throw new ArgumentException("Every document requires a file name and content.", nameof(request));
        }

        var trackId = CreateTrackId();
        var now = DateTime.UtcNow;
        var documents = request.Documents.Select(input =>
        {
            var bytes = Encoding.UTF8.GetBytes(input.Content);
            return new MarkdownDocument
            {
                FileName = input.FileName,
                Content = input.Content,
                FileSize = bytes.LongLength,
                UploadTime = now,
                TrackId = trackId,
                RagStatus = DocumentIntakeStatus.Queued,
                RagCurrentStage = "Accepted",
                RagProgress = 0,
                IsInRagSystem = false,
                FileHash = Convert.ToHexStringLower(SHA256.HashData(bytes))
            };
        }).ToList();

        context.MarkdownDocuments.AddRange(documents);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var document in documents)
        {
            var taskId = await taskQueueService.EnqueueTaskAsync(
                document.Id,
                document.Content,
                document.FileUrl ?? string.Empty,
                cancellationToken);

            if (taskId is null)
            {
                document.RagStatus = DocumentIntakeStatus.Failed;
                document.RagErrorMessage = "Document could not be queued because an active task already exists.";
                logger.LogWarning("Document intake queue rejected document {DocumentId}", document.Id);
                continue;
            }

            document.ActiveRagTaskId = taskId;
        }

        await context.SaveChangesAsync(cancellationToken);

        return new DocumentSubmissionResponse
        {
            TrackId = trackId,
            Documents = documents.Select(d => d.ToDto()).ToList()
        };
    }

    public async Task<DocumentTrackStatusResponse?> GetTrackStatusAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        var documents = await context.MarkdownDocuments
            .Where(d => d.TrackId == trackId)
            .OrderBy(d => d.UploadTime)
            .Select(d => d.ToDto())
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return null;
        }

        return new DocumentTrackStatusResponse
        {
            TrackId = trackId,
            TotalCount = documents.Count,
            QueuedCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Queued),
            ProcessingCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Processing),
            CompletedCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Completed),
            FailedCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Failed),
            CancelledCount = documents.Count(d => d.RagStatus == DocumentIntakeStatus.Cancelled),
            Documents = documents
        };
    }

    private static string CreateTrackId()
    {
        return HashUtils.ComputeMd5Hash(DateTime.UtcNow.ToString("O") + Guid.NewGuid().ToString("N"), "track-");
    }
}
```

- [ ] **Step 4: Register service**

In `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`, add:

```csharp
services.AddScoped<DocumentIntakeService>();
```

Add `using LightRAGNet.Server.Services;` if missing.

- [ ] **Step 5: Add controller endpoints**

Add `DocumentIntakeService documentIntakeService` to `MarkdownDocumentsController` constructor.

Add these actions to `MarkdownDocumentsController`:

```csharp
[HttpPost("text")]
[ProducesResponseType(typeof(DocumentSubmissionResponse), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<DocumentSubmissionResponse>> SubmitTextDocuments(
    [FromBody] SubmitTextDocumentsRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        var result = await documentIntakeService.SubmitTextDocumentsAsync(request, cancellationToken);
        return Accepted(result);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

[HttpGet("tracks/{trackId}")]
[ProducesResponseType(typeof(DocumentTrackStatusResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<DocumentTrackStatusResponse>> GetTrackStatus(
    string trackId,
    CancellationToken cancellationToken)
{
    var result = await documentIntakeService.GetTrackStatusAsync(trackId, cancellationToken);
    return result is null ? NotFound() : Ok(result);
}
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add text document intake API"
```

## Task 3: Add Status Filtering And Batch Upload Intake

**Files:**
- Modify: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Add failing status filter and batch upload tests**

Append:

```csharp
[Fact]
public async Task GetMarkdownDocuments_WithStatusAndTrackFilters_ReturnsMatchingRowsOnly()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 301,
        FileName = "queued.md",
        Content = "queued",
        TrackId = "track-filter",
        RagStatus = "Queued"
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 302,
        FileName = "failed.md",
        Content = "failed",
        TrackId = "other-track",
        RagStatus = "Failed"
    });
    using var client = factory.CreateClient();

    var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
        "/api/MarkdownDocuments?page=1&pageSize=10&status=Queued&trackId=track-filter");

    result.Should().NotBeNull();
    result!.Items.Should().ContainSingle(d => d.Id == 301);
    result.TotalCount.Should().Be(1);
}

[Fact]
public async Task UploadMarkdownDocumentsBatch_CreatesOneTrackForAllFiles()
{
    using var factory = new LightRagServerFactory();
    using var client = factory.CreateClient();
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent("alpha"), "files", "alpha.md");
    content.Add(new StringContent("beta"), "files", "beta.md");

    var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
    body.Should().NotBeNull();
    body!.Documents.Should().HaveCount(2);
    body.Documents.Select(d => d.TrackId).Should().OnlyContain(id => id == body.TrackId);
    body.Documents.Select(d => d.RagStatus).Should().OnlyContain(status => status == "Queued");
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: FAIL because filters and `/upload` do not exist.

- [ ] **Step 3: Add status and track filtering to list endpoint**

Change `GetMarkdownDocuments` signature in `MarkdownDocumentsController`:

```csharp
public async Task<ActionResult<PagedResult<MarkdownDocumentDto>>> GetMarkdownDocuments(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string? status = null,
    [FromQuery] string? trackId = null,
    CancellationToken cancellationToken = default)
```

Build a query before counting:

```csharp
var query = context.MarkdownDocuments.AsQueryable();

if (!string.IsNullOrWhiteSpace(status))
{
    query = query.Where(d => d.RagStatus == status);
}

if (!string.IsNullOrWhiteSpace(trackId))
{
    query = query.Where(d => d.TrackId == trackId);
}

var totalCount = await query.CountAsync(cancellationToken: cancellationToken);

var documents = await query
    .OrderBy(d => d.RagStatus == DocumentIntakeStatus.Processing ? 0 :
                 d.RagStatus == DocumentIntakeStatus.Queued || d.RagStatus == "Pending" ? 1 :
                 d.RagStatus == DocumentIntakeStatus.Failed ? 2 : 3)
    .ThenByDescending(d => d.UploadTime)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .Select(d => d.ToDto())
    .ToListAsync(cancellationToken: cancellationToken);
```

- [ ] **Step 4: Add file batch intake service method**

Add to `DocumentIntakeService`:

```csharp
public async Task<DocumentSubmissionResponse> SubmitUploadedFilesAsync(
    IReadOnlyList<IFormFile> files,
    CancellationToken cancellationToken)
{
    if (files.Count == 0)
    {
        throw new ArgumentException("At least one file is required.", nameof(files));
    }

    var inputs = new List<TextDocumentInput>();
    foreach (var file in files)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("File cannot be empty.", nameof(files));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".md" and not ".markdown" and not ".txt")
        {
            throw new ArgumentException("Only .md, .markdown, or .txt files are supported.", nameof(files));
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        inputs.Add(new TextDocumentInput
        {
            FileName = file.FileName,
            Content = content
        });
    }

    return await SubmitTextDocumentsAsync(new SubmitTextDocumentsRequest { Documents = inputs }, cancellationToken);
}
```

- [ ] **Step 5: Add batch upload endpoint**

Add to `MarkdownDocumentsController`:

```csharp
[HttpPost("upload")]
[Consumes("multipart/form-data")]
[ProducesResponseType(typeof(DocumentSubmissionResponse), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<DocumentSubmissionResponse>> UploadMarkdownDocumentsBatch(
    [FromForm] List<IFormFile> files,
    CancellationToken cancellationToken)
{
    try
    {
        var result = await documentIntakeService.SubmitUploadedFilesAsync(files, cancellationToken);
        return Accepted(result);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add document intake filtering and upload"
```

## Task 4: Add Retry And Cancel Semantics

**Files:**
- Modify: `src/LightRAGNet/Models/RagTask.cs`
- Modify: `src/LightRAGNet/Services/TaskQueue/IRagTaskQueueService.cs`
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskQueueService.cs`
- Modify: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs`

- [ ] **Step 1: Add failing API retry/cancel tests**

Append:

```csharp
[Fact]
public async Task RetryDocument_WhenFailed_RequeuesSameDocumentAndIncrementsRetryCount()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 401,
        FileName = "failed.md",
        Content = "failed content",
        TrackId = "track-retry",
        RagStatus = "Failed",
        RagErrorMessage = "boom",
        RagRetryCount = 1
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/401/retry", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await response.Content.ReadFromJsonAsync<DocumentPipelineActionResult>();
    body.Should().NotBeNull();
    body!.Accepted.Should().BeTrue();
    body.Status.Should().Be("Queued");

    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(401);
    document!.TrackId.Should().Be("track-retry");
    document.RagRetryCount.Should().Be(2);
    document.RagErrorMessage.Should().BeNull();
    document.RagStatus.Should().Be("Queued");
}

[Fact]
public async Task CancelDocument_WhenQueued_MarksCancelledAndDoesNotProcess()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 402,
        FileName = "queued.md",
        Content = "queued content",
        TrackId = "track-cancel",
        RagStatus = "Queued",
        ActiveRagTaskId = "task-queued"
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/402/cancel", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(402);
    document!.RagStatus.Should().Be("Cancelled");
    document.PipelineCancelledAt.Should().NotBeNull();
}

[Fact]
public async Task CancelTrack_CancelsAllQueuedDocumentsInTrack()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 403,
        FileName = "one.md",
        Content = "one",
        TrackId = "track-batch-cancel",
        RagStatus = "Queued"
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 404,
        FileName = "two.md",
        Content = "two",
        TrackId = "track-batch-cancel",
        RagStatus = "Completed"
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/tracks/track-batch-cancel/cancel", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var track = await client.GetFromJsonAsync<DocumentTrackStatusResponse>(
        "/api/MarkdownDocuments/tracks/track-batch-cancel");
    track!.CancelledCount.Should().Be(1);
    track.CompletedCount.Should().Be(1);
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: FAIL because retry/cancel endpoints do not exist.

- [ ] **Step 3: Extend task status and queue interface**

Add to `RagTaskStatus` in `src/LightRAGNet/Models/RagTask.cs`:

```csharp
Cancelled
```

Add to `RagTask`:

```csharp
public string? TrackId { get; set; }
```

Add to `IRagTaskQueueService`:

```csharp
Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement task cancellation**

In `RagTaskQueueService`, add `Cancelled` to terminal checks wherever the code currently checks `Completed or Failed` for stale progress and terminal cleanup.

Add this method:

```csharp
public async Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
{
    await EnsureTasksLoadedAsync(cancellationToken);

    var cancelledActiveTask = cancellationRegistry.CancelTask(taskId);
    if (cancelledActiveTask)
    {
        logger.LogInformation("Cancellation requested for processing task {TaskId}.", taskId);
    }

    await _lock.WaitAsync(cancellationToken);
    RagTask? task;
    try
    {
        if (!_tasks.TryGetValue(taskId, out task))
        {
            task = await stateStore.LoadTaskStateAsync(taskId, cancellationToken);
            if (task is null)
            {
                return false;
            }
            _tasks.TryAdd(taskId, task);
        }

        if (task.Status is RagTaskStatus.Completed or RagTaskStatus.Failed or RagTaskStatus.Cancelled)
        {
            return false;
        }

        task.Status = RagTaskStatus.Cancelled;
        task.ErrorMessage = null;
        task.CompletedAt = DateTime.UtcNow;
        task.CurrentStage = TaskStage.Completed;
        _tasks.TryRemove(taskId, out _);
        _terminalTaskIds.TryAdd(taskId, 0);
        await stateStore.DeleteTaskStateAsync(taskId, cancellationToken);
    }
    finally
    {
        _lock.Release();
    }

    await PublishStatusChangedWithGateAsync(task, cancellationToken);
    return true;
}
```

If `IRagTaskCancellationRegistry` has no `CancelTask` method, add it and implement it by cancelling the tracked CTS for the given task id. Existing `CancelActiveTasks` remains for clear-all.

- [ ] **Step 5: Add service retry/cancel methods**

Add to `DocumentIntakeService`:

```csharp
public async Task<DocumentPipelineActionResult?> RetryDocumentAsync(
    int documentId,
    CancellationToken cancellationToken)
{
    var document = await context.MarkdownDocuments.FindAsync([documentId], cancellationToken);
    if (document is null)
    {
        return null;
    }

    if (!DocumentIntakeStatus.IsRetryable(document.RagStatus))
    {
        return new DocumentPipelineActionResult
        {
            Accepted = false,
            DocumentId = document.Id,
            Status = document.RagStatus ?? string.Empty,
            Message = "Document is not retryable."
        };
    }

    var taskId = await taskQueueService.EnqueueTaskAsync(
        document.Id,
        document.Content,
        document.FileUrl ?? string.Empty,
        cancellationToken);

    if (taskId is null)
    {
        return new DocumentPipelineActionResult
        {
            Accepted = false,
            DocumentId = document.Id,
            Status = document.RagStatus ?? string.Empty,
            Message = "Document has an active task."
        };
    }

    document.ActiveRagTaskId = taskId;
    document.RagStatus = DocumentIntakeStatus.Queued;
    document.RagCurrentStage = "Accepted";
    document.RagErrorMessage = null;
    document.PipelineCancelledAt = null;
    document.PipelineCompletedAt = null;
    document.RagRetryCount++;
    await context.SaveChangesAsync(cancellationToken);

    return new DocumentPipelineActionResult
    {
        Accepted = true,
        DocumentId = document.Id,
        Status = DocumentIntakeStatus.Queued
    };
}

public async Task<DocumentPipelineActionResult?> CancelDocumentAsync(
    int documentId,
    CancellationToken cancellationToken)
{
    var document = await context.MarkdownDocuments.FindAsync([documentId], cancellationToken);
    if (document is null)
    {
        return null;
    }

    if (!DocumentIntakeStatus.IsCancellable(document.RagStatus))
    {
        return new DocumentPipelineActionResult
        {
            Accepted = false,
            DocumentId = document.Id,
            Status = document.RagStatus ?? string.Empty,
            Message = "Document is not cancellable."
        };
    }

    if (!string.IsNullOrWhiteSpace(document.ActiveRagTaskId))
    {
        await taskQueueService.CancelTaskAsync(document.ActiveRagTaskId, cancellationToken);
    }

    document.RagStatus = DocumentIntakeStatus.Cancelled;
    document.RagCurrentStage = DocumentIntakeStatus.Cancelled;
    document.PipelineCancelledAt = DateTime.UtcNow;
    await context.SaveChangesAsync(cancellationToken);

    return new DocumentPipelineActionResult
    {
        Accepted = true,
        DocumentId = document.Id,
        Status = DocumentIntakeStatus.Cancelled
    };
}

public async Task<int> CancelTrackAsync(string trackId, CancellationToken cancellationToken)
{
    var documents = await context.MarkdownDocuments
        .Where(d => d.TrackId == trackId)
        .Where(d => d.RagStatus == DocumentIntakeStatus.Queued || d.RagStatus == DocumentIntakeStatus.Processing)
        .ToListAsync(cancellationToken);

    var count = 0;
    foreach (var document in documents)
    {
        var result = await CancelDocumentAsync(document.Id, cancellationToken);
        if (result?.Accepted == true)
        {
            count++;
        }
    }

    return count;
}
```

- [ ] **Step 6: Add retry/cancel controller endpoints**

Add to `MarkdownDocumentsController`:

```csharp
[HttpPost("{id:int}/retry")]
[ProducesResponseType(typeof(DocumentPipelineActionResult), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public async Task<ActionResult<DocumentPipelineActionResult>> RetryDocument(
    int id,
    CancellationToken cancellationToken)
{
    var result = await documentIntakeService.RetryDocumentAsync(id, cancellationToken);
    if (result is null)
    {
        return NotFound();
    }

    return result.Accepted ? Accepted(result) : Conflict(result);
}

[HttpPost("{id:int}/cancel")]
[ProducesResponseType(typeof(DocumentPipelineActionResult), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public async Task<ActionResult<DocumentPipelineActionResult>> CancelDocument(
    int id,
    CancellationToken cancellationToken)
{
    var result = await documentIntakeService.CancelDocumentAsync(id, cancellationToken);
    if (result is null)
    {
        return NotFound();
    }

    return result.Accepted ? Accepted(result) : Conflict(result);
}

[HttpPost("tracks/{trackId}/cancel")]
[ProducesResponseType(StatusCodes.Status202Accepted)]
public async Task<ActionResult> CancelTrack(
    string trackId,
    CancellationToken cancellationToken)
{
    var cancelledCount = await documentIntakeService.CancelTrackAsync(trackId, cancellationToken);
    return Accepted(new { trackId, cancelledCount });
}
```

- [ ] **Step 7: Run retry/cancel tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagTaskQueueServiceTests --verbosity minimal
```

Expected: PASS. If existing task queue tests fail because they assume terminal statuses are only `Completed` and `Failed`, update expectations to include `Cancelled`.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add document pipeline retry cancel"
```

## Task 5: Align Worker Recovery And Status Handler

**Files:**
- Modify: `src/LightRAGNet/Services/TaskQueue/RagTaskProcessorService.cs`
- Modify: `src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs`
- Test: `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Add failing worker recovery test**

Add to `tests/LightRAGNet.Tests/TaskQueue/RagTaskProcessorServiceTests.cs`:

```csharp
[Fact]
public async Task RestoreTasksAsync_WhenTaskWasProcessing_MarksFailedInsteadOfPending()
{
    var queue = new InMemoryRagTaskQueueService([
        new RagTask
        {
            TaskId = "task-processing",
            DocumentId = 501,
            Content = "alpha",
            Status = RagTaskStatus.Processing
        }
    ]);
    var processor = CreateProcessor(queue);

    await processor.StartAsync(CancellationToken.None);
    await processor.StopAsync(CancellationToken.None);

    var restored = await queue.GetTaskAsync("task-processing");
    restored!.Status.Should().Be(RagTaskStatus.Failed);
    restored.ErrorMessage.Should().Contain("interrupted");
}
```

If the test helper names differ in the current file, keep the same assertion and adapt only construction to the local helper pattern.

- [ ] **Step 2: Run recovery test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagTaskProcessorServiceTests --verbosity minimal
```

Expected: FAIL because current restore resets `Processing` to `Pending`.

- [ ] **Step 3: Update restore behavior**

Change `RestoreTasksAsync` in `RagTaskProcessorService`:

```csharp
foreach (var task in tasks)
{
    if (task.Status != RagTaskStatus.Processing)
    {
        continue;
    }

    logger.LogInformation(
        "Restoring interrupted task {TaskId}, status reset from Processing to Failed",
        task.TaskId);

    await taskQueue.UpdateTaskStatusAsync(
        task.TaskId,
        RagTaskStatus.Failed,
        "Task was interrupted while processing. Retry explicitly to run it again.",
        cancellationToken);
}
```

- [ ] **Step 4: Update status handler mapping**

In `RagTaskStatusChangedHandler`, map index task statuses:

```csharp
document.RagStatus = task.Status switch
{
    RagTaskStatus.Pending => DocumentIntakeStatus.Queued,
    RagTaskStatus.Processing => DocumentIntakeStatus.Processing,
    RagTaskStatus.Completed => DocumentIntakeStatus.Completed,
    RagTaskStatus.Failed => DocumentIntakeStatus.Failed,
    RagTaskStatus.Cancelled => DocumentIntakeStatus.Cancelled,
    _ => task.Status.ToString()
};
document.RagCurrentStage = task.CurrentStage?.ToString();
document.RagErrorMessage = task.ErrorMessage;
document.RagDocumentId = task.RagDocumentId;
document.ActiveRagTaskId = task.Status is RagTaskStatus.Completed or RagTaskStatus.Failed or RagTaskStatus.Cancelled
    ? null
    : task.TaskId;

if (task.Status == RagTaskStatus.Processing && document.PipelineStartedAt is null)
{
    document.PipelineStartedAt = task.StartedAt ?? DateTime.UtcNow;
}

if (task.Status == RagTaskStatus.Completed)
{
    document.IsInRagSystem = true;
    document.RagAddedTime = DateTime.UtcNow;
    document.PipelineCompletedAt = DateTime.UtcNow;
}

if (task.Status == RagTaskStatus.Cancelled)
{
    document.PipelineCancelledAt = DateTime.UtcNow;
}

if (task.Status == RagTaskStatus.Failed)
{
    document.PipelineCompletedAt = DateTime.UtcNow;
}
```

Keep the existing delete-task branch before this index-task mapping.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagTaskProcessorServiceTests --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentIntakePipelineApiTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src tests
git commit -m "fix: fail interrupted document pipeline tasks"
```

## Task 6: Add Basic Web Operations

**Files:**
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
- Modify: `src/LightRAGNet.Web/Components/Pages/MarkdownDocuments.razor`
- Test: `tests/LightRAGNet.Tests/Web/MarkdownDocumentsSourceTests.cs`

- [ ] **Step 1: Add failing Web source tests**

Append to `MarkdownDocumentsSourceTests`:

```csharp
[Fact]
public void MarkdownDocuments_StatusFilter_PassesStatusToApiClient()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "LightRAGNet.Web",
        "Components",
        "Pages",
        "MarkdownDocuments.razor"));

    source.Should().Contain("private string? _selectedStatusFilter");
    source.Should().Contain("GetMarkdownDocumentsAsync(state.Page + 1, state.PageSize, _selectedStatusFilter");
}

[Fact]
public void MarkdownDocuments_RetryAndCancelActions_AreVisibleForPipelineStates()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "LightRAGNet.Web",
        "Components",
        "Pages",
        "MarkdownDocuments.razor"));

    source.Should().Contain("RetryDocument(context)");
    source.Should().Contain("CancelDocumentPipeline(context)");
    source.Should().Contain("IsRetryableStatus");
    source.Should().Contain("IsCancellableStatus");
}
```

Use the existing repository-root helper in the file. If it is named differently, reuse the existing helper instead of adding a duplicate.

- [ ] **Step 2: Run Web source tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~MarkdownDocumentsSourceTests --verbosity minimal
```

Expected: FAIL because Web filter and actions do not exist.

- [ ] **Step 3: Extend `ApiClient`**

Change `GetMarkdownDocumentsAsync` signature in `src/LightRAGNet.Web/ApiClient.cs`:

```csharp
public async Task<PagedResult<MarkdownDocumentDto>?> GetMarkdownDocumentsAsync(
    int page = 1,
    int pageSize = 10,
    string? status = null,
    string? trackId = null,
    CancellationToken cancellationToken = default)
{
    var query = new List<string>
    {
        $"page={page}",
        $"pageSize={pageSize}"
    };

    if (!string.IsNullOrWhiteSpace(status))
    {
        query.Add($"status={Uri.EscapeDataString(status)}");
    }

    if (!string.IsNullOrWhiteSpace(trackId))
    {
        query.Add($"trackId={Uri.EscapeDataString(trackId)}");
    }

    var url = $"api/MarkdownDocuments?{string.Join("&", query)}";
    return await httpClient.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(url, cancellationToken);
}
```

Add:

```csharp
public async Task<DocumentPipelineActionResult?> RetryDocumentAsync(
    int id,
    CancellationToken cancellationToken = default)
{
    var response = await httpClient.PostAsync($"api/MarkdownDocuments/{id}/retry", null, cancellationToken);
    return await response.Content.ReadFromJsonAsync<DocumentPipelineActionResult>(cancellationToken: cancellationToken);
}

public async Task<DocumentPipelineActionResult?> CancelDocumentPipelineAsync(
    int id,
    CancellationToken cancellationToken = default)
{
    var response = await httpClient.PostAsync($"api/MarkdownDocuments/{id}/cancel", null, cancellationToken);
    return await response.Content.ReadFromJsonAsync<DocumentPipelineActionResult>(cancellationToken: cancellationToken);
}
```

- [ ] **Step 4: Add Web status filter and actions**

In `MarkdownDocuments.razor`, add a compact status select near the table toolbar:

```razor
<MudSelect T="string" Label="Status" Value="_selectedStatusFilter" ValueChanged="OnStatusFilterChanged" Dense="true" Clearable="true">
    <MudSelectItem T="string" Value="@("Queued")">Queued</MudSelectItem>
    <MudSelectItem T="string" Value="@("Processing")">Processing</MudSelectItem>
    <MudSelectItem T="string" Value="@("Completed")">Completed</MudSelectItem>
    <MudSelectItem T="string" Value="@("Failed")">Failed</MudSelectItem>
    <MudSelectItem T="string" Value="@("Cancelled")">Cancelled</MudSelectItem>
</MudSelect>
```

Add fields and helpers:

```csharp
private string? _selectedStatusFilter;

private async Task OnStatusFilterChanged(string? status)
{
    _selectedStatusFilter = status;
    await RefreshDocumentsAsync(DocumentRefreshReason.UserAction);
}

private static bool IsRetryableStatus(string? status)
{
    return status is "Failed" or "Cancelled";
}

private static bool IsCancellableStatus(string? status)
{
    return status is "Queued" or "Processing" or "Pending";
}
```

Update `ServerReload` API call:

```csharp
var result = await ApiClient.GetMarkdownDocumentsAsync(
    state.Page + 1,
    state.PageSize,
    _selectedStatusFilter,
    cancellationToken: cancellationToken);
```

Add retry and cancel icon buttons beside the existing RAG action buttons:

```razor
@if (IsRetryableStatus(context.RagStatus))
{
    <MudTooltip Text="Retry">
        <MudIconButton Color="Color.Warning" Icon="@Icons.Material.Filled.Replay"
                       OnClick="@(() => RetryDocument(context))" />
    </MudTooltip>
}
@if (IsCancellableStatus(context.RagStatus))
{
    <MudTooltip Text="Cancel">
        <MudIconButton Color="Color.Default" Icon="@Icons.Material.Filled.Cancel"
                       OnClick="@(() => CancelDocumentPipeline(context))" />
    </MudTooltip>
}
```

Add methods:

```csharp
private async Task RetryDocument(MarkdownDocumentDto document)
{
    var result = await ApiClient.RetryDocumentAsync(document.Id);
    if (result?.Accepted == true)
    {
        document.RagStatus = result.Status;
        await RefreshDocumentsAsync(DocumentRefreshReason.UserAction);
    }
}

private async Task CancelDocumentPipeline(MarkdownDocumentDto document)
{
    var result = await ApiClient.CancelDocumentPipelineAsync(document.Id);
    if (result?.Accepted == true)
    {
        document.RagStatus = result.Status;
        await RefreshDocumentsAsync(DocumentRefreshReason.UserAction);
    }
}
```

If `DocumentRefreshReason.UserAction` does not exist, add it to the local enum.

- [ ] **Step 5: Run Web source tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~MarkdownDocumentsSourceTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add document pipeline web actions"
```

## Task 7: Full Verification And Closeout

**Files:**
- Verify all touched files.
- Update docs only if implementation intentionally changes spec behavior.

- [ ] **Step 1: Run server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal
```

Expected: PASS. Server tests must not touch real Qdrant or Neo4j.

- [ ] **Step 2: Run core tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Run solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 4: Inspect git diff**

Run:

```powershell
git status --short
git diff --check
git diff --stat
```

Expected: only intentional source, test, and migration changes; no whitespace errors.

- [ ] **Step 5: Run asset closeout gate**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_status.py . --topic "document-intake-pipeline-parity" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_closeout.py . --topic "document-intake-pipeline-parity" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "document-intake-pipeline-parity" --json
```

Expected: requirement archive is needed after implementation is accepted; problem route depends on actual implementation findings.

- [ ] **Step 6: Commit final cleanup if needed**

Run only if Step 4 or Step 5 produced intentional follow-up edits:

```powershell
git add docs src tests
git commit -m "docs: archive document intake pipeline parity"
```

## Self-Review Checklist

- Spec coverage:
  - Submit text and files: Tasks 2 and 3.
  - `track_id` and per-document `doc_id`: Tasks 1 and 2.
  - SQLite as status source: Tasks 1 through 5.
  - Background queue and single worker: Tasks 2, 4, and 5 reuse existing `IRagTaskQueueService`.
  - Retry/cancel: Task 4.
  - Track status and paginated status filtering: Tasks 2 and 3.
  - Basic Web operations: Task 6.
  - Verification and asset closeout: Task 7.
- Placeholder scan: plan uses concrete files, commands, snippets, and expected outcomes.
- Type consistency:
  - User-visible statuses are strings centralized by `DocumentIntakeStatus`.
  - Internal queue remains `RagTaskStatus.Pending`; API maps it to `Queued`.
  - `MarkdownDocument.Id` is the document id surfaced as `MarkdownDocumentDto.Id`.
  - `TrackId` is stored on every row and used for aggregation.
