using FluentAssertions;
using LightRAGNet.Core.Interfaces;
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
        var embeddingService = Substitute.For<IEmbeddingService>();
        var vectorStore = Substitute.For<IVectorStore>();
        var graphStore = Substitute.For<IGraphStore>();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = Substitute.For<IKVStore>();
        var fullDocsStore = Substitute.For<IKVStore>();
        var fullEntitiesStore = Substitute.For<IKVStore>();
        var fullRelationsStore = Substitute.For<IKVStore>();
        var entityChunksStore = Substitute.For<IKVStore>();
        var relationChunksStore = Substitute.For<IKVStore>();
        var llmCacheStore = Substitute.For<IKVStore>();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            options,
            NullLogger<DocumentLifecycleService>.Instance);
        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheStore,
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
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);
        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankService,
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
        var llmCacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);

        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(vectorStore, rerankService, tokenizer),
            llmCacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);
    }
}
