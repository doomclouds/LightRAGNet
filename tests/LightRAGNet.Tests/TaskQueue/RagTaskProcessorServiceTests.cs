using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Storage;
using LightRAGNet.Tests.TestDoubles;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.TaskQueue;

public sealed class RagTaskProcessorServiceTests
{
    [Fact]
    public async Task ProcessTaskAsync_WhenProgressArrivesQuickly_SerializesProgressBeforeCompleted()
    {
        var task = new RagTask
        {
            TaskId = "task-progress-ordering",
            DocumentId = 101,
            RagDocumentId = "doc-progress-ordering",
            Content = "alpha beta gamma delta epsilon zeta eta theta",
            FilePath = "progress-ordering.md",
            Status = RagTaskStatus.Pending
        };
        var taskQueue = new RecordingRagTaskQueueService(task);
        var statusStore = await CreateProcessedStatusStoreAsync(
            task.RagDocumentId!,
            task.Content,
            task.FilePath);
        var scopeFactory = new SingleServiceScopeFactory(CreateLightRag(statusStore));
        var processor = new RagTaskProcessorService(
            taskQueue,
            new RagTaskCancellationRegistry(),
            scopeFactory,
            NullLogger<RagTaskProcessorService>.Instance);
        processor.AfterProgressHandlerSubscribedForTesting = (lightRag, currentTask, _) =>
        {
            RaiseTaskStateChanged(lightRag, CreateProgressState(currentTask));
            return Task.CompletedTask;
        };
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await processor.StartAsync(stopCts.Token);
        await taskQueue.WaitForProgressWriteStartedAsync(TimeSpan.FromSeconds(2));
        var completedBeforeProgressWritesWereReleased = await taskQueue.WaitForStatusAsync(
            RagTaskStatus.Completed,
            TimeSpan.FromMilliseconds(200));
        taskQueue.ReleaseProgressWrites();

        if (!completedBeforeProgressWritesWereReleased)
        {
            (await taskQueue.WaitForStatusAsync(RagTaskStatus.Completed, TimeSpan.FromSeconds(2)))
                .Should()
                .BeTrue();
        }

        await taskQueue.WaitForProgressWritesToCompleteAsync(TimeSpan.FromSeconds(2));
        await processor.StopAsync(CancellationToken.None);

        taskQueue.ProgressWrites.Should().OnlyContain(write => write.TaskId == task.TaskId);
        taskQueue.Events.Should().NotBeEmpty();
        taskQueue.Events[^1].Should().Be("status:Completed");

        var completedIndex = taskQueue.Events.IndexOf("status:Completed");
        completedIndex.Should().BeGreaterThanOrEqualTo(0);
        taskQueue.Events
            .Skip(completedIndex + 1)
            .Should()
            .NotContain(evt => evt.StartsWith("progress:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessTaskAsync_WhenProgressWriteNeverCompletes_StillWritesCompletedStatus()
    {
        var task = new RagTask
        {
            TaskId = "task-progress-never-completes",
            DocumentId = 102,
            RagDocumentId = "doc-progress-never-completes",
            Content = "alpha beta gamma delta epsilon zeta eta theta",
            FilePath = "progress-never-completes.md",
            Status = RagTaskStatus.Pending
        };
        var taskQueue = new RecordingRagTaskQueueService(task);
        var statusStore = await CreateProcessedStatusStoreAsync(
            task.RagDocumentId!,
            task.Content,
            task.FilePath);
        var processor = new RagTaskProcessorService(
            taskQueue,
            new RagTaskCancellationRegistry(),
            new SingleServiceScopeFactory(CreateLightRag(statusStore)),
            NullLogger<RagTaskProcessorService>.Instance);
        processor.TerminalProgressDrainTimeoutForTesting = TimeSpan.FromMilliseconds(50);
        processor.AfterProgressHandlerSubscribedForTesting = (lightRag, currentTask, _) =>
        {
            RaiseTaskStateChanged(lightRag, CreateProgressState(currentTask));
            return Task.CompletedTask;
        };
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completed = false;

        try
        {
            await processor.StartAsync(stopCts.Token);
            await taskQueue.WaitForProgressWriteStartedAsync(TimeSpan.FromSeconds(2));

            completed = await taskQueue.WaitForStatusAsync(RagTaskStatus.Completed, TimeSpan.FromSeconds(1));
            if (!completed)
            {
                taskQueue.ReleaseProgressWrites();
            }

            completed.Should().BeTrue();

            taskQueue.Events.Should().Contain("status:Completed");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ProcessDeleteTaskAsync_MissingRagDocument_CompletesTask()
    {
        var stateStore = new InMemoryRagTaskStateStore();
        var statusStore = new InMemoryDocumentStatusStore();
        var notifications = new List<RagTask>();
        var taskQueue = CreateTaskQueue(stateStore, notifications);
        var taskId = await taskQueue.EnqueueDeletionTaskAsync(
            42,
            "doc-missing",
            "/uploads/missing.md",
            deleteLlmCache: false);
        var task = await taskQueue.GetTaskAsync(taskId!);
        var processor = new RagTaskProcessorService(
            taskQueue,
            new RagTaskCancellationRegistry(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<RagTaskProcessorService>.Instance);
        var lightRag = CreateLightRag(statusStore);

        await processor.ProcessDeleteTaskAsync(task!, lightRag, CancellationToken.None);

        task!.Status.Should().Be(RagTaskStatus.Completed);
        task.CurrentStage.Should().Be(TaskStage.Completed);
        task.ErrorMessage.Should().BeNull();
        notifications.Should().Contain(notification =>
            notification.TaskId == taskId &&
            notification.OperationType == RagTaskOperationType.DeleteDocument &&
            notification.Status == RagTaskStatus.Completed);
        (await stateStore.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    private static RagTaskQueueService CreateTaskQueue(
        InMemoryRagTaskStateStore stateStore,
        List<RagTask> notifications)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var notification = call.Arg<RagTaskStatusChangedEvent>();
                notifications.Add(CloneTask(notification.Task));
                return Task.CompletedTask;
            });

        return new RagTaskQueueService(
            stateStore,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
    }

    private static RagTask CloneTask(RagTask task)
    {
        return new RagTask
        {
            TaskId = task.TaskId,
            DocumentId = task.DocumentId,
            RagDocumentId = task.RagDocumentId,
            OperationType = task.OperationType,
            DeleteLlmCache = task.DeleteLlmCache,
            DeleteFilePath = task.DeleteFilePath,
            Content = task.Content,
            FilePath = task.FilePath,
            Status = task.Status,
            CurrentStage = task.CurrentStage,
            Progress = task.Progress,
            ErrorMessage = task.ErrorMessage,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            Priority = task.Priority,
            RetryCount = task.RetryCount,
            MaxRetries = task.MaxRetries
        };
    }

    private static LightRAG CreateLightRag(InMemoryDocumentStatusStore statusStore)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        var tokenizer = new FakeTokenizer();
        var llmService = Substitute.For<ILLMService>();
        llmService.ExtractEntitiesAsync(
                Arg.Any<string>(),
                Arg.Any<List<string>>(),
                Arg.Any<float>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new EntityExtractionResult());
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        var fullEntitiesStore = new InMemoryKvStore();
        var fullRelationsStore = new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            options,
            NullLogger<DocumentLifecycleService>.Instance);
        var cacheKeyBuilder = new LightRagCacheKeyBuilder();
        var llmCacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            cacheKeyBuilder,
            NullLogger<LightRagLlmCacheService>.Instance);
        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheService,
            cacheKeyBuilder,
            options,
            NullLogger<DocumentProcessingService>.Instance);
        var loggerFactory = NullLoggerFactory.Instance;
        var knowledgeGraphMergeService = new KnowledgeGraphMergeService(
            graphStore,
            vectorStore,
            embeddingService,
            llmService,
            tokenizer,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            options,
            llmCacheService,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService,
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);
        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankCoordinator,
            tokenizer,
            textChunksStore,
            options,
            loggerFactory);
        var documentDeletionService = new DocumentDeletionService(
            vectorStore,
            graphStore,
            embeddingService,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            NullLogger<DocumentDeletionService>.Instance);
        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(
                vectorStore,
                rerankCoordinator,
                tokenizer),
            llmCacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);
    }

    private static async Task<InMemoryDocumentStatusStore> CreateProcessedStatusStoreAsync(
        string docId,
        string content,
        string filePath)
    {
        var statusStore = new InMemoryDocumentStatusStore();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            Options.Create(new LightRAGOptions
            {
                Workspace = "workspace-a"
            }),
            NullLogger<DocumentLifecycleService>.Instance);

        await lifecycleService.PrepareIngestionAsync(content, docId, filePath);
        await lifecycleService.MarkProcessedAsync("workspace-a", docId);

        return statusStore;
    }

    private static TaskState CreateProgressState(RagTask task)
    {
        return new TaskState
        {
            Stage = TaskStage.DocumentChunking,
            Current = 0,
            Total = 0,
            Description = "test progress",
            DocId = task.RagDocumentId
        };
    }

    private static void RaiseTaskStateChanged(LightRAG lightRag, TaskState state)
    {
        var eventField = typeof(LightRAG).GetField(
            "TaskStateChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var handler = eventField?.GetValue(lightRag) as EventHandler<TaskState>;
        handler?.Invoke(lightRag, state);
    }

    private sealed class RecordingRagTaskQueueService(RagTask pendingTask) : IRagTaskQueueService
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource completedStatus =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource progressWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource progressWritesReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource progressWritesCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int returned;
        private int startedProgressWrites;
        private int completedProgressWrites;

        public List<string> Events { get; } = [];

        public List<(string TaskId, TaskStage? Stage, int? Progress)> ProgressWrites { get; } = [];

        public Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> EnqueueDeletionTaskAsync(
            int documentId,
            string ragDocumentId,
            string filePath,
            bool deleteLlmCache,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Interlocked.Exchange(ref returned, 1) == 0 ? pendingTask : null);
        }

        public Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<RagTask> { pendingTask });

        public Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(taskId, pendingTask.TaskId, StringComparison.Ordinal) ? pendingTask : null);

        public Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(documentId == pendingTask.DocumentId ? pendingTask : null);

        public Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(
            IEnumerable<int> documentIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(documentIds.Contains(pendingTask.DocumentId)
                ? new Dictionary<int, RagTask> { [pendingTask.DocumentId] = pendingTask }
                : []);

        public Task UpdateTaskStatusAsync(
            string taskId,
            RagTaskStatus status,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                Events.Add($"status:{status}");
            }

            pendingTask.Status = status;
            pendingTask.ErrorMessage = errorMessage;

            if (status == RagTaskStatus.Completed)
            {
                completedStatus.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public async Task UpdateTaskProgressAsync(
            string taskId,
            TaskStage? stage,
            int? progress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                ProgressWrites.Add((taskId, stage, progress));
            }

            Interlocked.Increment(ref startedProgressWrites);
            progressWriteStarted.TrySetResult();

            await progressWritesReleased.Task.WaitAsync(cancellationToken);

            lock (gate)
            {
                Events.Add($"progress:{stage}");
            }

            if (Interlocked.Increment(ref completedProgressWrites) >= Volatile.Read(ref startedProgressWrites))
            {
                progressWritesCompleted.TrySetResult();
            }
        }

        public Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public async Task WaitForProgressWriteStartedAsync(TimeSpan timeout)
        {
            await progressWriteStarted.Task.WaitAsync(timeout);
        }

        public async Task<bool> WaitForStatusAsync(RagTaskStatus status, TimeSpan timeout)
        {
            var statusTask = status switch
            {
                RagTaskStatus.Completed => completedStatus.Task,
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported status wait.")
            };

            var completed = await Task.WhenAny(statusTask, Task.Delay(timeout)) == statusTask;
            return completed;
        }

        public void ReleaseProgressWrites()
        {
            progressWritesReleased.TrySetResult();
        }

        public async Task WaitForProgressWritesToCompleteAsync(TimeSpan timeout)
        {
            if (Volatile.Read(ref startedProgressWrites) == Volatile.Read(ref completedProgressWrites))
            {
                return;
            }

            await progressWritesCompleted.Task.WaitAsync(timeout);
        }
    }

    private sealed class SingleServiceScopeFactory(LightRAG lightRag) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new Scope(lightRag);
        }

        private sealed class Scope(LightRAG lightRag) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                return serviceType == typeof(LightRAG) ? lightRag : null;
            }

            public void Dispose()
            {
            }
        }
    }

}
