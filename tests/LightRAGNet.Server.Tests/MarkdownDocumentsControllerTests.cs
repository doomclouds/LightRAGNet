using System.Net;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.DocumentConversion;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class MarkdownDocumentsControllerTests
{
    [Fact]
    public async Task AddToRagSystem_WhenQueueRejectsTask_ReturnsConflictAndDoesNotMarkPending()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new RejectingRagTaskQueueService());
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 42,
            FileName = "alpha.md",
            Content = "alpha beta",
            FileUrl = "/uploads/alpha.md"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/42/add-to-rag", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(42);
        document.Should().NotBeNull();
        document!.RagStatus.Should().BeNull();
    }

    [Fact]
    public async Task ClearAllData_WithTraversalUploadsPath_DoesNotDeleteFileOutsideUploads()
    {
        var outsideFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"clear-all-outside-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outsideFile, "outside");
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 43,
                FileName = "outside.md",
                Content = "content",
                FileUrl = $"/uploads/../{Path.GetFileName(outsideFile)}"
            });
            using var client = factory.CreateClient();

            var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            File.Exists(outsideFile).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outsideFile))
            {
                File.Delete(outsideFile);
            }
        }
    }

    [Fact]
    public async Task ClearAllData_WithDocumentArtifacts_RemovesArtifactDirectory()
    {
        using var factory = new LightRagServerFactory();
        string artifactPath;
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
            await using var original = new MemoryStream("pdf bytes"u8.ToArray());
            await store.SaveOriginalAsync(45, original, "clear.pdf", CancellationToken.None);
            var converted = await store.SaveConvertedMarkdownAsync(45, "# Converted", CancellationToken.None);
            artifactPath = store.GetFileInfo(converted.RelativePath).Directory!.FullName;

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = 45,
                FileName = "clear.pdf",
                Content = "# Converted",
                OriginalFileName = "clear.pdf",
                OriginalFilePath = Path.Combine("documents", "45", "original.pdf"),
                ConvertedMarkdownPath = converted.RelativePath,
                ConversionStatus = DocumentConversionStatus.Completed
            });
            await context.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Directory.Exists(artifactPath).Should().BeFalse();
    }

    [Fact]
    public async Task ClearAllData_WhenConversionIsRunning_WaitsBeforeRemovingRowsAndArtifacts()
    {
        var converter = new BlockingDocumentMarkdownConverter();
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IDocumentMarkdownConverter>();
            services.AddSingleton<IDocumentMarkdownConverter>(converter);
        });
        string documentDirectory;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
            var document = new MarkdownDocument
            {
                Id = 46,
                FileName = "running.pdf",
                OriginalFileName = "running.pdf",
                OriginalContentType = "application/pdf",
                Content = string.Empty,
                ConversionStatus = DocumentConversionStatus.Queued,
                RagStatus = DocumentIntakeStatus.Queued,
                RagCurrentStage = "Accepted",
                UploadTime = DateTime.UtcNow
            };
            context.MarkdownDocuments.Add(document);
            await context.SaveChangesAsync();

            await using var original = new MemoryStream("pdf bytes"u8.ToArray());
            var saved = await store.SaveOriginalAsync(document.Id, original, "running.pdf", CancellationToken.None);
            document.OriginalFilePath = saved.RelativePath;
            await context.SaveChangesAsync();
            documentDirectory = store.GetFileInfo(saved.RelativePath).Directory!.FullName;
        }

        using var processorScope = factory.Services.CreateScope();
        var processor = processorScope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();
        var processingTask = processor.ProcessNextBatchAsync(1, CancellationToken.None);
        await converter.WaitForCallAsync();
        using var client = factory.CreateClient();

        var clearTask = client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);
        var earlyCompletion = await Task.WhenAny(clearTask, Task.Delay(200));

        earlyCompletion.Should().NotBe(clearTask);
        converter.Release();
        var response = await clearTask;
        await processingTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Directory.Exists(documentDirectory).Should().BeFalse();
        using var verificationScope = factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        verificationContext.MarkdownDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAllData_StopsActiveTasksBeforeDeletingDocumentRows()
    {
        LightRagServerFactory? factory = null;
        var queue = new InspectingRagTaskQueueService(() =>
        {
            using var scope = factory!.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return context.MarkdownDocuments.Any();
        });
        factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
        await using (factory)
        {
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 44,
                FileName = "active.md",
                Content = "content",
                FileUrl = "/uploads/active.md"
            });
            using var client = factory.CreateClient();

            var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            queue.RowsWerePresentWhenStopWasCalled.Should().BeTrue();
            queue.StopAllTasksCallOrder.Should().BeLessThan(queue.ClearAllTasksCallOrder);
        }
    }

    [Fact]
    public async Task ClearAllData_WhenClearSucceeds_BumpsWorkspaceQueryRevision()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(responseStream);
        json.RootElement
            .GetProperty("details")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Contain("Bumped query cache revision");
        using var scope = factory.Services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<LightRagLlmCacheService>();
        (await cacheService.GetWorkspaceQueryRevisionAsync("_")).Should().Be(1);
    }

    [Fact]
    public async Task ClearAllData_WhenWorkspaceHasWhitespace_BumpsTrimmedWorkspaceRevision()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.PostConfigure<LightRAGOptions>(options => options.Workspace = " workspace-a ");
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<LightRagLlmCacheService>();
        (await cacheService.GetWorkspaceQueryRevisionAsync("workspace-a")).Should().Be(1);
        (await cacheService.GetWorkspaceQueryRevisionAsync(" workspace-a ")).Should().Be(0);
    }

    [Fact]
    public async Task ClearAllData_UsesInjectedExternalStorageCleaner()
    {
        var cleaner = new RecordingExternalStorageCleaner();
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagExternalStorageCleaner>();
            services.AddSingleton<IRagExternalStorageCleaner>(cleaner);
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/clear-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cleaner.CallCount.Should().Be(1);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(responseStream);
        json.RootElement
            .GetProperty("details")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Contain("External cleaner invoked");
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }

    private class RejectingRagTaskQueueService : IRagTaskQueueService
    {
        public Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
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

        public Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(
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

        public Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public virtual Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public virtual Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class InspectingRagTaskQueueService(Func<bool> rowsExistAtStop) : RejectingRagTaskQueueService
    {
        private int callOrder;

        public bool RowsWerePresentWhenStopWasCalled { get; private set; }
        public int StopAllTasksCallOrder { get; private set; }
        public int ClearAllTasksCallOrder { get; private set; }

        public override Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            ClearAllTasksCallOrder = ++callOrder;
            return Task.CompletedTask;
        }

        public override Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default)
        {
            StopAllTasksCallOrder = ++callOrder;
            RowsWerePresentWhenStopWasCalled = rowsExistAtStop();
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingExternalStorageCleaner : IRagExternalStorageCleaner
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult<IReadOnlyList<string>>(["External cleaner invoked"]);
        }
    }

    private sealed class BlockingDocumentMarkdownConverter : IDocumentMarkdownConverter
    {
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DocumentMarkdownConversionResult> ConvertAsync(
            FileInfo sourceFile,
            string fileName,
            string? contentType,
            CancellationToken cancellationToken)
        {
            called.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new DocumentMarkdownConversionResult("# Converted");
        }

        public Task WaitForCallAsync()
        {
            return called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Release()
        {
            release.TrySetResult();
        }
    }
}
