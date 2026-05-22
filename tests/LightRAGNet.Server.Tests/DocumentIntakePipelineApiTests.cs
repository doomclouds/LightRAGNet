using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LightRAGNet.Hosting;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Migrations;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
using MediatR;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentIntakePipelineApiTests
{
    [Fact]
    public void AddLightRag_DoesNotRegisterServerDocumentIntakeService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LLM:ApiKey"] = "test-key",
                ["Embedding:ApiKey"] = "test-key",
                ["Rerank:ApiKey"] = "test-key"
            })
            .Build();

        services.AddLightRAG(configuration);

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName == "LightRAGNet.Server.Services.DocumentIntakeService");
    }

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
        body.Documents.Select(d => d.ConversionStatus).Should().OnlyContain(status => status == DocumentConversionStatus.NotRequired);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments
            .Where(d => d.TrackId == body.TrackId)
            .Select(d => d.ConversionStatus)
            .Should()
            .OnlyContain(status => status == DocumentConversionStatus.NotRequired);
    }

    [Fact]
    public async Task SubmitTextDocuments_EnqueuesTextUriSourceAndReturnsFileUrl()
    {
        var queue = new RecordingRagTaskQueueService();
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/MarkdownDocuments/text", new SubmitTextDocumentsRequest
        {
            Documents =
            [
                new TextDocumentInput { FileName = "source file.md", Content = "alpha" }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        var document = body!.Documents.Should().ContainSingle().Subject;
        document.FileUrl.Should().StartWith($"text://{body.TrackId}/");
        queue.EnqueueCalls.Should().ContainSingle().Which.FilePath.Should().Be(document.FileUrl);
    }

    [Fact]
    public async Task SubmitTextDocuments_WhenQueueThrows_MarksDocumentFailedWithError()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new ThrowingRagTaskQueueService());
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/MarkdownDocuments/text", new SubmitTextDocumentsRequest
        {
            Documents =
            [
                new TextDocumentInput { FileName = "throw.md", Content = "boom" }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        var document = body!.Documents.Should().ContainSingle().Subject;
        document.RagStatus.Should().Be("Failed");
        document.RagErrorMessage.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.MarkdownDocuments.FindAsync(document.Id);
        persisted.Should().NotBeNull();
        persisted!.RagStatus.Should().Be("Failed");
        persisted.RagErrorMessage.Should().NotBeNullOrWhiteSpace();
        persisted.ActiveRagTaskId.Should().BeNull();
    }

    [Fact]
    public async Task SubmitTextDocuments_WhenQueueCancels_MarksDocumentFailedWithError()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new CancellingRagTaskQueueService());
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/MarkdownDocuments/text", new SubmitTextDocumentsRequest
        {
            Documents =
            [
                new TextDocumentInput { FileName = "cancel.md", Content = "cancel" }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        var document = body!.Documents.Should().ContainSingle().Subject;
        document.RagStatus.Should().Be("Failed");
        document.RagErrorMessage.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.MarkdownDocuments.FindAsync(document.Id);
        persisted.Should().NotBeNull();
        persisted!.RagStatus.Should().Be("Failed");
        persisted.RagErrorMessage.Should().NotBeNullOrWhiteSpace();
        persisted.ActiveRagTaskId.Should().BeNull();
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

    [Fact]
    public async Task GetTrackStatus_CountsPendingDocumentsAsQueued()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 203,
            FileName = "pending.md",
            Content = "pending",
            TrackId = "track-pending",
            RagStatus = "Pending"
        });
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<DocumentTrackStatusResponse>(
            "/api/MarkdownDocuments/tracks/track-pending");

        body.Should().NotBeNull();
        body!.QueuedCount.Should().Be(1);
        body.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTrackStatus_WhenTrackDoesNotExist_ReturnsNotFound()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/MarkdownDocuments/tracks/missing-track");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RagTaskStatusChangedHandler_WhenPendingIndexTask_SetsQueuedStatusAndActiveTask()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 701,
            FileName = "handler-pending.md",
            Content = "handler pending",
            RagStatus = "Pending"
        });
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

        await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
        {
            TaskId = "task-handler-pending",
            DocumentId = 701,
            RagDocumentId = "rag-handler-pending",
            Status = RagTaskStatus.Pending,
            CurrentStage = TaskStage.DocumentChunking,
            OperationType = RagTaskOperationType.IndexDocument
        }), CancellationToken.None);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(701);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
        document.ActiveRagTaskId.Should().Be("task-handler-pending");
        document.RagCurrentStage.Should().Be(TaskStage.DocumentChunking.ToString());
        document.RagDocumentId.Should().Be("rag-handler-pending");
    }

    [Fact]
    public async Task RagTaskStatusChangedHandler_WhenProcessingIndexTask_SetsPipelineStartedAtOnce()
    {
        using var factory = new LightRagServerFactory();
        var existingStartedAt = DateTime.UtcNow.AddMinutes(-10);
        var taskStartedAt = DateTime.UtcNow.AddMinutes(-3);
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 702,
            FileName = "handler-processing.md",
            Content = "handler processing",
            RagStatus = DocumentIntakeStatus.Queued,
            PipelineStartedAt = existingStartedAt
        });
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

        await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
        {
            TaskId = "task-handler-processing",
            DocumentId = 702,
            RagDocumentId = "rag-handler-processing",
            Status = RagTaskStatus.Processing,
            CurrentStage = TaskStage.ProcessingChunks,
            Progress = 25,
            StartedAt = taskStartedAt,
            OperationType = RagTaskOperationType.IndexDocument
        }), CancellationToken.None);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(702);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be(DocumentIntakeStatus.Processing);
        document.ActiveRagTaskId.Should().Be("task-handler-processing");
        document.PipelineStartedAt.Should().Be(existingStartedAt);
        document.RagProgress.Should().Be(25);
    }

    [Fact]
    public async Task RagTaskStatusChangedHandler_WhenProcessingIndexTaskHasTaskStartedAt_UsesTaskStartedAt()
    {
        using var factory = new LightRagServerFactory();
        var taskStartedAt = DateTime.UtcNow.AddMinutes(-2);
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 704,
            FileName = "handler-processing-started.md",
            Content = "handler processing started",
            RagStatus = DocumentIntakeStatus.Queued
        });
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

        await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
        {
            TaskId = "task-handler-processing-started",
            DocumentId = 704,
            Status = RagTaskStatus.Processing,
            StartedAt = taskStartedAt,
            OperationType = RagTaskOperationType.IndexDocument
        }), CancellationToken.None);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(704);
        document.Should().NotBeNull();
        document!.PipelineStartedAt.Should().Be(taskStartedAt);
    }

    [Theory]
    [InlineData(RagTaskStatus.Completed)]
    [InlineData(RagTaskStatus.Failed)]
    [InlineData(RagTaskStatus.Cancelled)]
    public async Task RagTaskStatusChangedHandler_WhenTerminalIndexTask_ClearsActiveTaskAndSetsPipelineTimestamp(
        RagTaskStatus status)
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 703,
            FileName = "handler-terminal.md",
            Content = "handler terminal",
            RagStatus = DocumentIntakeStatus.Processing,
            ActiveRagTaskId = "task-stale",
            PipelineStartedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

        await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
        {
            TaskId = $"task-handler-{status}",
            DocumentId = 703,
            RagDocumentId = "rag-handler-terminal",
            Status = status,
            CurrentStage = status == RagTaskStatus.Completed ? TaskStage.Completed : TaskStage.DocumentChunking,
            ErrorMessage = status == RagTaskStatus.Failed ? "handler failed" : null,
            OperationType = RagTaskOperationType.IndexDocument
        }), CancellationToken.None);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(703);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be(status.ToString());
        document.ActiveRagTaskId.Should().BeNull();
        document.RagDocumentId.Should().Be("rag-handler-terminal");

        switch (status)
        {
            case RagTaskStatus.Completed:
                document.IsInRagSystem.Should().BeTrue();
                document.RagAddedTime.Should().NotBeNull();
                document.PipelineCompletedAt.Should().NotBeNull();
                document.PipelineCancelledAt.Should().BeNull();
                break;
            case RagTaskStatus.Failed:
                document.RagErrorMessage.Should().Be("handler failed");
                document.PipelineCompletedAt.Should().NotBeNull();
                document.PipelineCancelledAt.Should().BeNull();
                break;
            case RagTaskStatus.Cancelled:
                document.PipelineCompletedAt.Should().BeNull();
                document.PipelineCancelledAt.Should().NotBeNull();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

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

    [Fact]
    public async Task GetMarkdownDocuments_ReturnsSafeConversionMetadataWithoutLocalPaths()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 801,
            FileName = "合同.pdf",
            Content = "# Converted",
            FileSize = 128,
            TrackId = "track-conversion-metadata",
            OriginalFileName = "合同.pdf",
            OriginalFilePath = "documents/801/original.pdf",
            OriginalContentType = "application/pdf",
            OriginalContentHash = "original-hash",
            ConvertedMarkdownPath = "documents/801/converted.md",
            ConvertedMarkdownHash = "markdown-hash",
            ConversionStatus = DocumentConversionStatus.Completed,
            ConversionErrorMessage = @"Failed to convert C:\WorkSpace\secret\original.pdf from documents/801/original.pdf to documents/801/converted.md; retry documents\801\original.pdf via /documents/801/original.pdf; inspect C:\Users\x\My Documents\secret.pdf, C:\Users\x\My Documents, /var/app/uploads/secret.pdf, /var/app/uploads, /tmp/lightrag/documents/801/original.pdf, \\server\share\secret.pdf, and \\server\share\folder; public URL https://example.com/documents/801/original.pdf; see documents/README.md",
            ConversionStartedAt = new DateTime(2026, 5, 22, 1, 2, 3, DateTimeKind.Utc),
            ConversionCompletedAt = new DateTime(2026, 5, 22, 1, 2, 8, DateTimeKind.Utc),
            ConversionTool = "ManagedCode.MarkItDown",
            ConversionToolVersion = "10.0.7"
        });
        using var client = factory.CreateClient();

        var rawJson = await client.GetStringAsync(
            "/api/MarkdownDocuments?page=1&pageSize=10&trackId=track-conversion-metadata");
        var result = JsonSerializer.Deserialize<PagedResult<MarkdownDocumentDto>>(
            rawJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        result.Should().NotBeNull();
        rawJson.Should().NotContain(@"C:\\WorkSpace\\secret\\original.pdf");
        rawJson.Should().NotContain("from documents/801/original.pdf");
        rawJson.Should().NotContain("documents/801/converted.md");
        rawJson.Should().NotContain(@"documents\\801\\original.pdf");
        rawJson.Should().NotContain("via /documents/801/original.pdf");
        rawJson.Should().NotContain(@"C:\\Users\\x\\My Documents\\secret.pdf");
        rawJson.Should().NotContain(@"Documents\\secret.pdf");
        rawJson.Should().NotContain(@"C:\\Users\\x\\My Documents");
        rawJson.Should().NotContain("My Documents");
        rawJson.Should().NotContain("/var/app/uploads/secret.pdf");
        rawJson.Should().NotContain("/var/app/uploads");
        rawJson.Should().NotContain("/tmp/lightrag");
        rawJson.Should().NotContain(@"\\\\server\\share\\secret.pdf");
        rawJson.Should().NotContain(@"\\\\server\\share\\folder");
        rawJson.Should().Contain("https://example.com/documents/801/original.pdf");
        rawJson.Should().Contain("documents/README.md");
        var document = result!.Items.Should().ContainSingle(d => d.Id == 801).Subject;
        document.FileName.Should().Be("合同.pdf");
        document.OriginalFileName.Should().Be("合同.pdf");
        document.OriginalContentType.Should().Be("application/pdf");
        document.OriginalContentHash.Should().Be("original-hash");
        document.ConvertedMarkdownHash.Should().Be("markdown-hash");
        document.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
        document.ConversionErrorMessage.Should().Contain("[path]");
        document.ConversionErrorMessage.Should().NotContain(@"C:\WorkSpace\secret\original.pdf");
        document.ConversionErrorMessage.Should().NotContain("from documents/801/original.pdf");
        document.ConversionErrorMessage.Should().NotContain("documents/801/converted.md");
        document.ConversionErrorMessage.Should().NotContain(@"documents\801\original.pdf");
        document.ConversionErrorMessage.Should().NotContain("via /documents/801/original.pdf");
        document.ConversionErrorMessage.Should().NotContain(@"C:\Users\x\My Documents\secret.pdf");
        document.ConversionErrorMessage.Should().NotContain(@"Documents\secret.pdf");
        document.ConversionErrorMessage.Should().NotContain(@"C:\Users\x\My Documents");
        document.ConversionErrorMessage.Should().NotContain("My Documents");
        document.ConversionErrorMessage.Should().NotContain("/var/app/uploads/secret.pdf");
        document.ConversionErrorMessage.Should().NotContain("/var/app/uploads");
        document.ConversionErrorMessage.Should().NotContain("/tmp/lightrag");
        document.ConversionErrorMessage.Should().NotContain(@"\\server\share\secret.pdf");
        document.ConversionErrorMessage.Should().NotContain(@"\\server\share\folder");
        document.ConversionErrorMessage.Should().Contain("https://example.com/documents/801/original.pdf");
        document.ConversionErrorMessage.Should().Contain("documents/README.md");
        document.ConversionTool.Should().Be("ManagedCode.MarkItDown");
        document.ConversionToolVersion.Should().Be("10.0.7");
        document.ConversionStartedAt.Should().Be(new DateTime(2026, 5, 22, 1, 2, 3, DateTimeKind.Utc));
        document.ConversionCompletedAt.Should().Be(new DateTime(2026, 5, 22, 1, 2, 8, DateTimeKind.Utc));
        typeof(MarkdownDocumentDto).GetProperty("OriginalFilePath").Should().BeNull();
        typeof(MarkdownDocumentDto).GetProperty("ConvertedMarkdownPath").Should().BeNull();
    }

    [Fact]
    public void AddDocumentConversionArtifacts_BackfillsExistingMarkdownRowsAsNotRequired()
    {
        var migration = new AddDocumentConversionArtifacts();

        var sqlOperation = migration.UpOperations
            .OfType<SqlOperation>()
            .SingleOrDefault(operation => operation.Sql.Contains(DocumentConversionStatus.NotRequired, StringComparison.Ordinal));

        sqlOperation.Should().NotBeNull();
        sqlOperation!.Sql.Should().Contain("UPDATE MarkdownDocuments");
        sqlOperation.Sql.Should().Contain("ConversionStatus IS NULL");
        sqlOperation.Sql.Should().Contain("OriginalFilePath IS NULL");
        sqlOperation.Sql.Should().Contain(DocumentConversionStatus.NotRequired);
    }

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
    public async Task RetryDocument_WhenCompleted_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 405,
            FileName = "completed.md",
            Content = "completed content",
            TrackId = "track-retry-completed",
            RagStatus = "Completed"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/405/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CancelDocument_WhenQueued_MarksCancelledAndDoesNotProcess()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new RecordingRagTaskQueueService());
        });
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
    public async Task CancelDocument_WhenCompleted_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 406,
            FileName = "completed.md",
            Content = "completed content",
            TrackId = "track-cancel-completed",
            RagStatus = "Completed"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/406/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CancelDocument_WhenPending_MarksCancelled()
    {
        var queue = new LookupCancelRagTaskQueueService(new RagTask
        {
            TaskId = "task-pending",
            DocumentId = 407,
            Status = RagTaskStatus.Pending
        });
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 407,
            FileName = "pending.md",
            Content = "pending content",
            TrackId = "track-cancel-pending",
            RagStatus = "Pending"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/407/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        queue.CancelCalls.Should().ContainSingle().Which.Should().Be("task-pending");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(407);
        document!.RagStatus.Should().Be("Cancelled");
        document.PipelineCancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelDocument_WhenQueueRejectsCancel_ReturnsConflictAndKeepsOriginalStatus()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new RejectingCancelRagTaskQueueService());
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 408,
            FileName = "stale.md",
            Content = "stale content",
            TrackId = "track-cancel-stale",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-stale"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/408/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(408);
        document!.RagStatus.Should().Be("Queued");
        document.ActiveRagTaskId.Should().Be("task-stale");
        document.PipelineCancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task CancelDocument_WhenPendingTaskLookupRejectsCancel_ReturnsConflictAndKeepsOriginalStatus()
    {
        var queue = new LookupCancelRagTaskQueueService(new RagTask
        {
            TaskId = "task-pending-reject",
            DocumentId = 411,
            Status = RagTaskStatus.Pending
        }, cancelResult: false);
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 411,
            FileName = "pending-reject.md",
            Content = "pending reject",
            TrackId = "track-cancel-pending-reject",
            RagStatus = "Pending"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/411/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        queue.CancelCalls.Should().ContainSingle().Which.Should().Be("task-pending-reject");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(411);
        document!.RagStatus.Should().Be("Pending");
        document.PipelineCancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task CancelTrack_CancelsAllQueuedDocumentsInTrack()
    {
        var queue = new LookupCancelRagTaskQueueService(new RagTask
        {
            TaskId = "task-track-queued",
            DocumentId = 403,
            Status = RagTaskStatus.Pending
        });
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
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
        queue.CancelCalls.Should().ContainSingle().Which.Should().Be("task-track-queued");
        var track = await client.GetFromJsonAsync<DocumentTrackStatusResponse>(
            "/api/MarkdownDocuments/tracks/track-batch-cancel");
        track!.CancelledCount.Should().Be(1);
        track.CompletedCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelTrack_WhenOneQueueCancelFails_CountsOnlyActuallyCancelledDocuments()
    {
        var queue = new SelectiveCancelRagTaskQueueService("task-cancel-ok");
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 409,
            FileName = "one.md",
            Content = "one",
            TrackId = "track-partial-cancel",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-cancel-ok"
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 410,
            FileName = "two.md",
            Content = "two",
            TrackId = "track-partial-cancel",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-cancel-fail"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/tracks/track-partial-cancel/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<CancelTrackResult>();
        body.Should().NotBeNull();
        body!.CancelledCount.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await context.MarkdownDocuments.FindAsync(409);
        var second = await context.MarkdownDocuments.FindAsync(410);
        first!.RagStatus.Should().Be("Cancelled");
        first.ActiveRagTaskId.Should().BeNull();
        first.PipelineCancelledAt.Should().NotBeNull();
        second!.RagStatus.Should().Be("Queued");
        second.ActiveRagTaskId.Should().Be("task-cancel-fail");
        second.PipelineCancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task UploadDocumentsBatch_WhenPdfAndDocx_SavesOriginalsButDoesNotQueueRag()
    {
        var queue = new RecordingRagTaskQueueService();
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("pdf bytes"u8.ToArray()), "files", "合同.pdf");
        content.Add(new ByteArrayContent("docx bytes"u8.ToArray()), "files", "说明书.docx");

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        body!.TrackId.Should().NotBeNullOrWhiteSpace();
        body.Documents.Should().HaveCount(2);
        body.Documents.Select(d => d.FileName).Should().BeEquivalentTo(["合同.pdf", "说明书.docx"]);
        body.Documents.Select(d => d.RagStatus).Should().OnlyContain(status => status == null);
        body.Documents.Select(d => d.RagCurrentStage).Should().OnlyContain(stage => stage == null);
        body.Documents.Select(d => d.ConversionStatus).Should().OnlyContain(status => status == DocumentConversionStatus.NotStarted);
        body.Documents.Select(d => d.IsInRagSystem).Should().OnlyContain(value => value == false);
        queue.EnqueueCalls.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var documents = context.MarkdownDocuments.OrderBy(d => d.FileName).ToList();
        documents.Should().HaveCount(2);
        documents[0].OriginalFilePath.Should().EndWith(Path.Combine("documents", documents[0].Id.ToString(), "original.pdf"));
        documents[1].OriginalFilePath.Should().EndWith(Path.Combine("documents", documents[1].Id.ToString(), "original.docx"));
        documents.Select(d => d.OriginalContentHash).Should().OnlyContain(hash => !string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public async Task UploadDocumentsBatch_WhenFileNameContainsPathAndContentTypeSpoofed_StoresSafeMetadata()
    {
        var artifactStore = new FailingDocumentArtifactStore(failOnSaveCall: 0);
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IDocumentArtifactStore>();
            services.AddSingleton<IDocumentArtifactStore>(artifactStore);
        });
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var pdfContent = new ByteArrayContent("pdf bytes"u8.ToArray());
        pdfContent.Headers.ContentType = new("text/plain");
        content.Add(pdfContent, "files", @"C:\fake\folder\合同.pdf");
        using var docxContent = new ByteArrayContent("docx bytes"u8.ToArray());
        docxContent.Headers.ContentType = new("application/pdf");
        content.Add(docxContent, "files", "../说明书.docx");

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        body!.Documents.Select(d => d.FileName).Should().BeEquivalentTo(["合同.pdf", "说明书.docx"]);
        body.Documents.Should().ContainSingle(d =>
            d.FileName == "合同.pdf" &&
            d.OriginalFileName == "合同.pdf" &&
            d.FileUrl == $"upload://{body.TrackId}/%E5%90%88%E5%90%8C.pdf" &&
            d.OriginalContentType == "application/pdf");
        body.Documents.Should().ContainSingle(d =>
            d.FileName == "说明书.docx" &&
            d.OriginalFileName == "说明书.docx" &&
            d.FileUrl == $"upload://{body.TrackId}/%E8%AF%B4%E6%98%8E%E4%B9%A6.docx" &&
            d.OriginalContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        artifactStore.SavedOriginalFileNames.Should().BeEquivalentTo(["合同.pdf", "说明书.docx"]);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = context.MarkdownDocuments.ToList();
        persisted.Should().HaveCount(2);
        persisted.Should().ContainSingle(d =>
            d.FileName == "合同.pdf" &&
            d.OriginalFileName == "合同.pdf" &&
            d.FileUrl == $"upload://{body.TrackId}/%E5%90%88%E5%90%8C.pdf" &&
            d.OriginalContentType == "application/pdf");
        persisted.Should().ContainSingle(d =>
            d.FileName == "说明书.docx" &&
            d.OriginalFileName == "说明书.docx" &&
            d.FileUrl == $"upload://{body.TrackId}/%E8%AF%B4%E6%98%8E%E4%B9%A6.docx" &&
            d.OriginalContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    [Fact]
    public async Task UploadDocumentsBatch_WhenSecondArtifactSaveFails_RollsBackRowsAndDeletesSavedArtifacts()
    {
        var queue = new RecordingRagTaskQueueService();
        var artifactStore = new FailingDocumentArtifactStore(failOnSaveCall: 2);
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
            services.RemoveAll<IDocumentArtifactStore>();
            services.AddSingleton<IDocumentArtifactStore>(artifactStore);
        });
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("pdf bytes"u8.ToArray()), "files", "first.pdf");
        content.Add(new ByteArrayContent("docx bytes"u8.ToArray()), "files", "second.docx");

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        queue.EnqueueCalls.Should().BeEmpty();
        artifactStore.DeletedDocumentIds.Should().Contain(artifactStore.SavedDocumentIds.Single());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadDocumentsBatch_WhenArtifactDeleteFailsStillRollsBackRows()
    {
        var artifactStore = new FailingDocumentArtifactStore(failOnSaveCall: 2, failDelete: true);
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IDocumentArtifactStore>();
            services.AddSingleton<IDocumentArtifactStore>(artifactStore);
        });
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("pdf bytes"u8.ToArray()), "files", "first.pdf");
        content.Add(new ByteArrayContent("docx bytes"u8.ToArray()), "files", "second.docx");

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        artifactStore.DeleteAttempts.Should().NotBeEmpty();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("notes.txt")]
    [InlineData("slides.pptx")]
    [InlineData("legacy.doc")]
    [InlineData("program.exe")]
    public async Task UploadDocumentsBatch_WhenExtensionUnsupported_ReturnsBadRequest(string fileName)
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("unsupported"u8.ToArray()), "files", fileName);

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Only .pdf and .docx files are supported.");
    }

    [Fact]
    public async Task UploadMarkdownDocument_SetsConversionStatusNotRequired()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("# legacy"), "file", "legacy.md");

        var response = await client.PostAsync("/api/MarkdownDocuments", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = await response.Content.ReadFromJsonAsync<MarkdownDocumentDto>();
        document.Should().NotBeNull();
        document!.ConversionStatus.Should().Be(DocumentConversionStatus.NotRequired);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.MarkdownDocuments.FindAsync(document.Id);
        persisted.Should().NotBeNull();
        persisted!.ConversionStatus.Should().Be(DocumentConversionStatus.NotRequired);
    }

    [Fact]
    public async Task UploadMarkdownDocumentsBatch_WhenFileExceedsLimit_ReturnsBadRequest()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var oversizedBytes = new byte[(10 * 1024 * 1024) + 1];
        using var fileContent = new ByteArrayContent(oversizedBytes);
        content.Add(fileContent, "files", "large.pdf");

        var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMarkdownDocuments_WhenQueuedDocumentHasActiveTask_RefreshesTaskProgressAndKeepsQueuedStatus()
    {
        var queue = new StatusReportingRagTaskQueueService(new RagTask
        {
            DocumentId = 303,
            TaskId = "task-queued",
            Status = RagTaskStatus.Pending,
            Progress = 42,
            CurrentStage = TaskStage.ProcessingChunks
        });
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 303,
            FileName = "queued-active.md",
            Content = "queued active",
            TrackId = "track-active",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-queued",
            RagProgress = 0
        });
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
            "/api/MarkdownDocuments?page=1&pageSize=10&status=Queued");

        result.Should().NotBeNull();
        var document = result!.Items.Should().ContainSingle(d => d.Id == 303).Subject;
        document.RagStatus.Should().Be("Queued");
        document.RagProgress.Should().Be(42);
        document.RagCurrentStage.Should().Be(TaskStage.ProcessingChunks.ToString());
    }

    [Fact]
    public async Task GetMarkdownDocuments_WithQueuedStatusFilter_ExcludesQueuedDbRowWhenTaskIsProcessing()
    {
        var queue = new StatusReportingRagTaskQueueService(new RagTask
        {
            DocumentId = 304,
            TaskId = "task-processing",
            Status = RagTaskStatus.Processing
        });
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 304,
            FileName = "queued-processing.md",
            Content = "queued processing",
            TrackId = "track-status-filter",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-processing"
        });
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
            "/api/MarkdownDocuments?page=1&pageSize=10&status=Queued");

        result.Should().NotBeNull();
        result!.Items.Should().NotContain(d => d.Id == 304);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMarkdownDocuments_WithProcessingStatusFilter_ReturnsQueuedDbRowWhenTaskIsProcessing()
    {
        var queue = new StatusReportingRagTaskQueueService(new RagTask
        {
            DocumentId = 305,
            TaskId = "task-processing",
            Status = RagTaskStatus.Processing
        });
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 305,
            FileName = "queued-processing.md",
            Content = "queued processing",
            TrackId = "track-status-filter",
            RagStatus = "Queued",
            ActiveRagTaskId = "task-processing"
        });
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
            "/api/MarkdownDocuments?page=1&pageSize=10&status=Processing");

        result.Should().NotBeNull();
        var document = result!.Items.Should().ContainSingle(d => d.Id == 305).Subject;
        document.RagStatus.Should().Be("Processing");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task AddToRagSystem_WhenDocumentIsQueued_ReturnsBadRequestAndDoesNotEnqueue()
    {
        var queue = new RecordingRagTaskQueueService();
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 306,
            FileName = "queued.md",
            Content = "queued",
            RagStatus = "Queued"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/306/add-to-rag", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        queue.EnqueueCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AddToRagSystem_WhenQueueAcceptsTask_StoresActiveTaskId()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new RecordingRagTaskQueueService());
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 307,
            FileName = "legacy-add.md",
            Content = "legacy add",
            FileUrl = "/uploads/legacy-add.md"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/307/add-to-rag", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(307);
        document!.RagStatus.Should().Be("Pending");
        document.ActiveRagTaskId.Should().Be("task-1");
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }

    private class RecordingRagTaskQueueService : IRagTaskQueueService
    {
        private int nextTaskId;

        public List<EnqueueCall> EnqueueCalls { get; } = [];
        public List<string> CancelCalls { get; } = [];

        public virtual Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            EnqueueCalls.Add(new EnqueueCall(documentId, content, filePath));
            return Task.FromResult<string?>($"task-{++nextTaskId}");
        }

        public Task<string?> EnqueueDeletionTaskAsync(
            int documentId,
            string ragDocumentId,
            string filePath,
            bool deleteLlmCache,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<RagTask>());
        }

        public Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public virtual Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public virtual Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(
            IEnumerable<int> documentIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Dictionary<int, RagTask>());
        }

        public Task UpdateTaskStatusAsync(
            string taskId,
            RagTaskStatus status,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTaskProgressAsync(
            string taskId,
            TaskStage? stage,
            int? progress,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public virtual Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            CancelCalls.Add(taskId);
            return Task.FromResult(true);
        }

        public Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingRagTaskQueueService : RecordingRagTaskQueueService
    {
        public override Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("queue unavailable");
        }
    }

    private sealed class CancellingRagTaskQueueService : RecordingRagTaskQueueService
    {
        public override Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("queue cancelled");
        }
    }

    private sealed class StatusReportingRagTaskQueueService(RagTask task) : RecordingRagTaskQueueService
    {
        public override Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(
            IEnumerable<int> documentIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(documentIds.Contains(task.DocumentId)
                ? new Dictionary<int, RagTask> { [task.DocumentId] = task }
                : new Dictionary<int, RagTask>());
        }
    }

    private sealed class LookupCancelRagTaskQueueService(RagTask task, bool cancelResult = true) : RecordingRagTaskQueueService
    {
        public override Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(documentId == task.DocumentId ? task : null);
        }

        public override Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            CancelCalls.Add(taskId);
            return Task.FromResult(cancelResult);
        }
    }

    private sealed class RejectingCancelRagTaskQueueService : RecordingRagTaskQueueService
    {
        public override Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class SelectiveCancelRagTaskQueueService(string acceptedTaskId) : RecordingRagTaskQueueService
    {
        public override Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Equals(taskId, acceptedTaskId, StringComparison.Ordinal));
        }
    }

    private sealed record CancelTrackResult(
        [property: JsonPropertyName("trackId")] string TrackId,
        [property: JsonPropertyName("cancelledCount")] int CancelledCount);

    private sealed record EnqueueCall(int DocumentId, string Content, string FilePath);

    private sealed class FailingDocumentArtifactStore(int failOnSaveCall, bool failDelete = false) : IDocumentArtifactStore
    {
        private int saveCalls;

        public List<int> SavedDocumentIds { get; } = [];
        public List<string> SavedOriginalFileNames { get; } = [];
        public List<int> DeleteAttempts { get; } = [];
        public List<int> DeletedDocumentIds { get; } = [];

        public Task<DocumentArtifactWriteResult> SaveOriginalAsync(
            int documentId,
            Stream source,
            string originalFileName,
            CancellationToken cancellationToken)
        {
            saveCalls++;
            if (saveCalls == failOnSaveCall)
            {
                throw new InvalidOperationException("artifact save failed");
            }

            SavedDocumentIds.Add(documentId);
            SavedOriginalFileNames.Add(originalFileName);
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            return Task.FromResult(new DocumentArtifactWriteResult(
                Path.Combine("root", "documents", documentId.ToString(), $"original{extension}"),
                Path.Combine("documents", documentId.ToString(), $"original{extension}"),
                $"hash-{documentId}",
                source.Length));
        }

        public Task<DocumentArtifactWriteResult> SaveConvertedMarkdownAsync(
            int documentId,
            string markdown,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string> ReadConvertedMarkdownAsync(string relativePath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public FileInfo GetFileInfo(string relativePath)
        {
            throw new NotSupportedException();
        }

        public bool Exists(string? relativePath)
        {
            return false;
        }

        public Task DeleteArtifactsAsync(MarkdownDocument document, CancellationToken cancellationToken)
        {
            DeleteAttempts.Add(document.Id);
            if (failDelete)
            {
                throw new IOException("artifact delete failed");
            }

            DeletedDocumentIds.Add(document.Id);
            return Task.CompletedTask;
        }
    }
}
