using System.Text;
using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.DocumentConversion;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentConversionProcessorTests
{
    [Fact]
    public async Task ProcessNextBatchAsync_WhenQueuedConversionSucceeds_WritesMarkdownAndEnqueuesRag()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Markdown = "# Converted\n\nHello"
        };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedDocumentWithOriginalArtifactAsync(factory, "source.pdf", DocumentConversionStatus.Queued, DocumentIntakeStatus.Queued);

        var processed = await ProcessNextBatchAsync(factory, maxDocuments: 10);

        processed.Should().Be(1);
        converter.CallCount.Should().Be(1);
        queue.EnqueueCalls.Should().ContainSingle().Which.Should().Match<EnqueueCall>(call =>
            call.DocumentId == documentId &&
            call.Content == "# Converted\n\nHello");

        var document = await FindDocumentAsync(factory, documentId);
        document!.FileName.Should().Be("source.pdf");
        document.Content.Should().Be("# Converted\n\nHello");
        document.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
        document.ConvertedMarkdownPath.Should().EndWith(Path.Combine("documents", documentId.ToString(), "converted.md"));
        document.ConvertedMarkdownHash.Should().NotBeNullOrWhiteSpace();
        document.ConversionTool.Should().Be("ManagedCode.MarkItDown");
        document.ConversionToolVersion.Should().Be("10.0.7");
        document.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
        document.RagCurrentStage.Should().Be("Indexing");
        document.ActiveRagTaskId.Should().Be("task-1");
    }

    [Fact]
    public async Task ProcessNextBatchAsync_IgnoresUploadedDocumentBeforeAddToRag()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Markdown = "# Converted"
        };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        await SeedDocumentWithOriginalArtifactAsync(factory, "waiting.docx", DocumentConversionStatus.NotStarted, ragStatus: null);

        var processed = await ProcessNextBatchAsync(factory, maxDocuments: 10);

        processed.Should().Be(0);
        converter.CallCount.Should().Be(0);
        queue.EnqueueCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenConverterReturnsEmpty_MarksFailedWithoutRagTask()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Markdown = "   "
        };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedDocumentWithOriginalArtifactAsync(factory, "empty.pdf", DocumentConversionStatus.Queued, DocumentIntakeStatus.Queued);

        var processed = await ProcessNextBatchAsync(factory, maxDocuments: 10);

        processed.Should().Be(1);
        queue.EnqueueCalls.Should().BeEmpty();
        var document = await FindDocumentAsync(factory, documentId);
        document!.RagStatus.Should().Be(DocumentIntakeStatus.Failed);
        document.RagCurrentStage.Should().Be("Converting");
        document.ConversionStatus.Should().Be(DocumentConversionStatus.Failed);
        document.ConversionErrorMessage.Should().Be("Document conversion produced empty Markdown.");
        document.RagErrorMessage.Should().Be("Document conversion produced empty Markdown.");
        document.ActiveRagTaskId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenConverterThrows_SanitizesUserFacingError()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Exception = new InvalidOperationException(@"failed reading C:\secret\source.pdf")
        };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedDocumentWithOriginalArtifactAsync(factory, "broken.pdf", DocumentConversionStatus.Queued, DocumentIntakeStatus.Queued);

        var processed = await ProcessNextBatchAsync(factory, maxDocuments: 10);

        processed.Should().Be(1);
        queue.EnqueueCalls.Should().BeEmpty();
        var document = await FindDocumentAsync(factory, documentId);
        document!.ConversionStatus.Should().Be(DocumentConversionStatus.Failed);
        document.ConversionErrorMessage.Should().Be("Document conversion failed.");
        document.RagErrorMessage.Should().Be("Document conversion failed.");
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenRagQueueRejects_AfterConversionKeepsConversionCompleted()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Markdown = "# Converted\n\nHello"
        };
        var queue = new RecordingRagTaskQueueService
        {
            RejectEnqueue = true
        };
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedDocumentWithOriginalArtifactAsync(factory, "reject.docx", DocumentConversionStatus.Queued, DocumentIntakeStatus.Queued);

        var processed = await ProcessNextBatchAsync(factory, maxDocuments: 10);

        processed.Should().Be(1);
        queue.EnqueueCalls.Should().ContainSingle();
        var document = await FindDocumentAsync(factory, documentId);
        document!.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
        document.ConvertedMarkdownPath.Should().NotBeNullOrWhiteSpace();
        document.RagStatus.Should().Be(DocumentIntakeStatus.Failed);
        document.RagCurrentStage.Should().Be("Indexing");
        document.RagErrorMessage.Should().Be("Document could not be queued for indexing.");
        document.ActiveRagTaskId.Should().BeNull();
    }

    private static LightRagServerFactory CreateFactory(
        FakeDocumentMarkdownConverter converter,
        RecordingRagTaskQueueService queue)
    {
        return new LightRagServerFactory(services =>
        {
            services.RemoveAll<IDocumentMarkdownConverter>();
            services.AddSingleton<IDocumentMarkdownConverter>(converter);
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
    }

    private static async Task<int> ProcessNextBatchAsync(LightRagServerFactory factory, int maxDocuments)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();
        return await processor.ProcessNextBatchAsync(maxDocuments, CancellationToken.None);
    }

    private static async Task<int> SeedDocumentWithOriginalArtifactAsync(
        LightRagServerFactory factory,
        string fileName,
        string conversionStatus,
        string? ragStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
        var document = new MarkdownDocument
        {
            FileName = fileName,
            OriginalFileName = fileName,
            OriginalContentType = GetContentType(fileName),
            Content = string.Empty,
            ConversionStatus = conversionStatus,
            RagStatus = ragStatus,
            RagCurrentStage = ragStatus == DocumentIntakeStatus.Queued ? "Accepted" : null,
            IsInRagSystem = false,
            UploadTime = DateTime.UtcNow
        };

        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("original bytes"));
        var original = await artifactStore.SaveOriginalAsync(document.Id, stream, fileName, CancellationToken.None);
        document.OriginalFilePath = original.RelativePath;
        document.OriginalContentHash = original.Hash;
        document.FileSize = original.Size;
        await context.SaveChangesAsync();

        return document.Id;
    }

    private static async Task<MarkdownDocument?> FindDocumentAsync(LightRagServerFactory factory, int documentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.MarkdownDocuments.FindAsync(documentId);
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private sealed class FakeDocumentMarkdownConverter : IDocumentMarkdownConverter
    {
        public string Markdown { get; init; } = string.Empty;

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public Task<DocumentMarkdownConversionResult> ConvertAsync(
            FileInfo sourceFile,
            string originalFileName,
            string? contentType,
            CancellationToken cancellationToken)
        {
            CallCount++;
            sourceFile.Exists.Should().BeTrue();

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(new DocumentMarkdownConversionResult(Markdown));
        }
    }

    private sealed class RecordingRagTaskQueueService : IRagTaskQueueService
    {
        private int nextTaskId;

        public bool RejectEnqueue { get; init; }

        public List<EnqueueCall> EnqueueCalls { get; } = [];

        public Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            EnqueueCalls.Add(new EnqueueCall(documentId, content, filePath));
            return Task.FromResult(RejectEnqueue ? null : $"task-{++nextTaskId}");
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

    private sealed record EnqueueCall(int DocumentId, string Content, string FilePath);
}
