using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Hosting;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
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

        public Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default)
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

    private sealed record EnqueueCall(int DocumentId, string Content, string FilePath);
}
