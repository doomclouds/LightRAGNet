using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Tests.TestDoubles;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LightRAGNet.Tests.TaskQueue;

public sealed class RagTaskQueueServiceTests
{
    [Fact]
    public async Task EnqueueTaskAsync_CreatesPendingTaskAndPublishesEvent()
    {
        var (service, _, mediator) = CreateService();

        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");

        var task = await service.GetTaskAsync(taskId!);
        task.Should().NotBeNull();
        task!.Status.Should().Be(RagTaskStatus.Pending);
        task.DocumentId.Should().Be(7);
        await mediator.Received(1).Publish(
            Arg.Any<RagTaskStatusChangedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_WhenIndexTaskPendingForDocument_ReturnsNullAndDoesNotCreateTask()
    {
        var (service, _, _) = CreateService();
        await service.EnqueueTaskAsync(42, "alpha beta", "alpha.md");

        var taskId = await service.EnqueueDeletionTaskAsync(
            42,
            "doc-alpha",
            "alpha.md",
            deleteLlmCache: false);

        taskId.Should().BeNull();
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().ContainSingle();
        tasks[0].OperationType.Should().Be(RagTaskOperationType.IndexDocument);
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_WhenNoActiveTask_CreatesDeleteTask()
    {
        var (service, _, _) = CreateService();

        var taskId = await service.EnqueueDeletionTaskAsync(
            42,
            "doc-alpha",
            "alpha.md",
            deleteLlmCache: true);

        taskId.Should().NotBeNullOrWhiteSpace();
        var task = await service.GetTaskAsync(taskId!);
        task.Should().NotBeNull();
        task!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
        task.RagDocumentId.Should().Be("doc-alpha");
        task.DeleteLlmCache.Should().BeTrue();
        task.DeleteFilePath.Should().Be("alpha.md");
        task.Status.Should().Be(RagTaskStatus.Pending);
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNull()
    {
        var (service, _, _) = CreateService();
        await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

        var duplicate = await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

        duplicate.Should().BeNull();
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().ContainSingle(t => t.OperationType == RagTaskOperationType.DeleteDocument);
    }

    [Fact]
    public async Task EnqueueTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNullAndDoesNotCreateIndexTask()
    {
        var (service, _, _) = CreateService();
        await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

        var indexTaskId = await service.EnqueueTaskAsync(42, "alpha beta", "alpha.md");

        indexTaskId.Should().BeNull();
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().ContainSingle();
        tasks[0].OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_PublishesDeleteOperationMetadata()
    {
        var (service, _, mediator) = CreateService();

        await service.EnqueueDeletionTaskAsync(
            42,
            "doc-alpha",
            "alpha.md",
            deleteLlmCache: true);

        await mediator.Received(1).Publish(
            Arg.Is<RagTaskStatusChangedEvent>(e =>
                e.Task.OperationType == RagTaskOperationType.DeleteDocument &&
                e.Task.DeleteLlmCache &&
                e.Task.DeleteFilePath == "alpha.md"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNextTaskAsync_WhenDeleteTaskPending_ReturnsDeleteTaskMetadata()
    {
        var (service, _, _) = CreateService();
        await service.EnqueueDeletionTaskAsync(
            42,
            "doc-alpha",
            "alpha.md",
            deleteLlmCache: true);

        var nextTask = await service.GetNextTaskAsync();

        nextTask.Should().NotBeNull();
        nextTask!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
        nextTask.DeleteLlmCache.Should().BeTrue();
        nextTask.DeleteFilePath.Should().Be("alpha.md");
    }

    [Fact]
    public async Task GetNextTaskAsync_ReturnsLowestPriorityPendingTask()
    {
        var (service, _, _) = CreateService();
        var firstTaskId = await service.EnqueueTaskAsync(1, "first", "first.md");
        var secondTaskId = await service.EnqueueTaskAsync(2, "second", "second.md");
        await service.ReorderTaskAsync(firstTaskId!, 10);
        await service.ReorderTaskAsync(secondTaskId!, 1);

        var nextTask = await service.GetNextTaskAsync();

        nextTask.Should().NotBeNull();
        nextTask!.TaskId.Should().Be(secondTaskId);
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_WhenProcessing_SetsStartedAtAndSaves()
    {
        var (service, store, _) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        var saveCountBeforeProcessing = store.GetSaveCount(taskId!);

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);

        var task = await service.GetTaskAsync(taskId!);
        task.Should().NotBeNull();
        task!.Status.Should().Be(RagTaskStatus.Processing);
        task.StartedAt.Should().NotBeNull();
        store.GetSaveCount(taskId!).Should().Be(saveCountBeforeProcessing + 1);
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_WhenCompleted_RemovesPersistentState()
    {
        var (service, store, _) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);

        var task = await service.GetTaskAsync(taskId!);
        task.Should().BeNull();

        var persistedTask = await store.LoadTaskStateAsync(taskId!);
        persistedTask.Should().BeNull();
    }

    [Fact]
    public async Task RetryTaskAsync_WhenFailedAndBelowMaxRetries_RequeuesTask()
    {
        var (service, store, _) = CreateService();
        var task = new RagTask
        {
            TaskId = "task-failed",
            DocumentId = 7,
            RagDocumentId = "doc-7",
            Content = "content",
            FilePath = "file.md",
            Status = RagTaskStatus.Failed,
            CurrentStage = TaskStage.ProcessingChunks,
            Progress = 80,
            ErrorMessage = "boom",
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            CompletedAt = DateTime.UtcNow.AddMinutes(-1),
            RetryCount = 1,
            MaxRetries = 3
        };
        await store.SaveTaskStateAsync(task);

        var retried = await service.RetryTaskAsync(task.TaskId);

        retried.Should().BeTrue();
        var requeuedTask = await service.GetTaskAsync(task.TaskId);
        requeuedTask.Should().NotBeNull();
        requeuedTask!.Status.Should().Be(RagTaskStatus.Pending);
        requeuedTask.RetryCount.Should().Be(2);
        requeuedTask.ErrorMessage.Should().BeNull();
        requeuedTask.StartedAt.Should().BeNull();
        requeuedTask.CompletedAt.Should().BeNull();
        requeuedTask.Progress.Should().Be(0);
    }

    [Fact]
    public async Task StopAllTasksAsync_FailsPendingAndProcessingTasks()
    {
        var (service, _, mediator) = CreateService();
        var pendingTaskId = await service.EnqueueTaskAsync(1, "pending", "pending.md");
        var processingTaskId = await service.EnqueueTaskAsync(2, "processing", "processing.md");
        var completedTaskId = await service.EnqueueTaskAsync(3, "completed", "completed.md");
        await service.UpdateTaskStatusAsync(processingTaskId!, RagTaskStatus.Processing);
        await service.UpdateTaskStatusAsync(completedTaskId!, RagTaskStatus.Completed);
        mediator.ClearReceivedCalls();

        var stoppedCount = await service.StopAllTasksAsync();

        stoppedCount.Should().Be(2);
        var pendingTask = await service.GetTaskAsync(pendingTaskId!);
        var processingTask = await service.GetTaskAsync(processingTaskId!);
        pendingTask.Should().NotBeNull();
        processingTask.Should().NotBeNull();
        pendingTask!.Status.Should().Be(RagTaskStatus.Failed);
        processingTask!.Status.Should().Be(RagTaskStatus.Failed);
        pendingTask.ErrorMessage.Should().Be("Task stopped (when clearing data)");
        processingTask.ErrorMessage.Should().Be("Task stopped (when clearing data)");

        var allTasks = await service.GetAllTasksAsync();
        allTasks.Select(task => task.TaskId).Should().NotContain(completedTaskId);
        await mediator.Received(1).Publish(
            Arg.Is<RagTaskStatusChangedEvent>(e => e.Task.TaskId == pendingTaskId && e.Task.Status == RagTaskStatus.Failed),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Publish(
            Arg.Is<RagTaskStatusChangedEvent>(e => e.Task.TaskId == processingTaskId && e.Task.Status == RagTaskStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAllTasksAsync_RemovesAllTasks()
    {
        var (service, store, _) = CreateService();
        await service.EnqueueTaskAsync(1, "first", "first.md");
        await service.EnqueueTaskAsync(2, "second", "second.md");

        await service.ClearAllTasksAsync();

        var tasks = await service.GetAllTasksAsync();
        tasks.Should().BeEmpty();
        var persistedTasks = await store.LoadAllTasksAsync();
        persistedTasks.Should().BeEmpty();
    }

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
}
