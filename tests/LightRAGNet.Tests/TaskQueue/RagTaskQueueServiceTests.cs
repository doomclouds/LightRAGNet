using System.Reflection;
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
        var (service, _, mediator, _) = CreateService();

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
    public async Task EnqueueTaskAsync_WhenTaskMutatesAfterPublish_PublishedEventKeepsSnapshot()
    {
        var store = new InMemoryRagTaskStateStore();
        var publishedTasks = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                publishedTasks.Add(call.Arg<RagTaskStatusChangedEvent>().Task);
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);

        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        var pendingNotification = publishedTasks.Should().ContainSingle().Subject;

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);

        pendingNotification.Status.Should().Be(RagTaskStatus.Pending);
        pendingNotification.StartedAt.Should().BeNull();
        publishedTasks.Should().HaveCount(2);
        publishedTasks[1].Status.Should().Be(RagTaskStatus.Processing);
    }

    [Fact]
    public async Task EnqueueTaskAsync_WhenStateSaveFails_DoesNotLeavePendingTaskInMemoryOrPublish()
    {
        var store = new ThrowingSaveTaskStateStore();
        var mediator = Substitute.For<IMediator>();
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);

        var act = () => service.EnqueueTaskAsync(7, "content", "file.md");

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("save failed");
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().BeEmpty();
        await mediator.DidNotReceive().Publish(
            Arg.Any<RagTaskStatusChangedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_WhenIndexTaskPendingForDocument_ReturnsNullAndDoesNotCreateTask()
    {
        var (service, _, _, _) = CreateService();
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
        var (service, _, _, _) = CreateService();

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
    public async Task EnqueueDeletionTaskAsync_WhenStateSaveFails_DoesNotLeavePendingTaskInMemoryOrPublish()
    {
        var store = new ThrowingSaveTaskStateStore();
        var mediator = Substitute.For<IMediator>();
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);

        var act = () => service.EnqueueDeletionTaskAsync(
            42,
            "doc-alpha",
            "alpha.md",
            deleteLlmCache: false);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("save failed");
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().BeEmpty();
        await mediator.DidNotReceive().Publish(
            Arg.Any<RagTaskStatusChangedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueDeletionTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();
        await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

        var duplicate = await service.EnqueueDeletionTaskAsync(42, "doc-alpha", "alpha.md", deleteLlmCache: false);

        duplicate.Should().BeNull();
        var tasks = await service.GetAllTasksAsync();
        tasks.Should().ContainSingle(t => t.OperationType == RagTaskOperationType.DeleteDocument);
    }

    [Fact]
    public async Task EnqueueTaskAsync_WhenDeleteTaskPendingForDocument_ReturnsNullAndDoesNotCreateIndexTask()
    {
        var (service, _, _, _) = CreateService();
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
        var (service, _, mediator, _) = CreateService();

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
        var (service, _, _, _) = CreateService();
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
        var (service, _, _, _) = CreateService();
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
        var (service, store, _, _) = CreateService();
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
        var (service, store, _, _) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);

        var task = await service.GetTaskAsync(taskId!);
        task.Should().BeNull();

        var persistedTask = await store.LoadTaskStateAsync(taskId!);
        persistedTask.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_WhenCompleted_CleansTransientPublicationRegistries()
    {
        var (service, _, _, _) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);

        GetTerminalTombstoneCount(service).Should().Be(0);
        GetTaskLifecycleCount(service).Should().Be(0);
        GetPublishLockEntryCount(service).Should().Be(0);
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_WhenTerminalDeleteFails_SavesTerminalSnapshotBeforeCleanup()
    {
        var store = new ThrowingDeleteTaskStateStore();
        var notifications = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                notifications.Add(CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task));
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        store.ThrowDeleteFor(taskId!);
        notifications.Clear();

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);

        var persistedTask = await store.LoadTaskStateAsync(taskId!);
        persistedTask.Should().NotBeNull();
        persistedTask!.Status.Should().Be(RagTaskStatus.Completed);
        GetTerminalTombstoneCount(service).Should().Be(0);
        GetTaskLifecycleCount(service).Should().Be(0);
        GetPublishLockEntryCount(service).Should().Be(0);

        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Completed);
        notifications.Clear();

        await service.UpdateTaskProgressAsync(taskId!, TaskStage.DocumentChunking, 50);

        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenTerminalStatusWinsDuringSave_DoesNotRepublishOrPersistProgress()
    {
        var store = new BlockingProgressSaveTaskStateStore();
        var notifications = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                notifications.Add(CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task));
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        notifications.Clear();
        store.BlockProgressSaveFor(taskId!);

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await store.WaitForBlockedProgressSaveAsync(TimeSpan.FromSeconds(2));

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();

        store.ReleaseBlockedProgressSave();
        await progressTask.WaitAsync(TimeSpan.FromSeconds(2));

        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Completed);
        notifications
            .SkipWhile(task => task.Status != RagTaskStatus.Completed)
            .Skip(1)
            .Should()
            .BeEmpty();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenTerminalWinsDuringSave_CleansTransientRegistriesAfterStaleProgressDrains()
    {
        var store = new BlockingProgressSaveTaskStateStore();
        var mediator = Substitute.For<IMediator>();
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        store.BlockProgressSaveFor(taskId!);

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await store.WaitForBlockedProgressSaveAsync(TimeSpan.FromSeconds(2));

        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);

        GetTerminalTombstoneCount(service).Should().Be(1);
        GetTaskLifecycleCount(service).Should().Be(1);

        store.ReleaseBlockedProgressSave();
        await progressTask.WaitAsync(TimeSpan.FromSeconds(2));

        GetTerminalTombstoneCount(service).Should().Be(0);
        GetTaskLifecycleCount(service).Should().Be(0);
        GetPublishLockEntryCount(service).Should().Be(0);
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenStopAllSavesFailedState_DoesNotDeleteFailedState()
    {
        var store = new BlockingProgressSaveTaskStateStore();
        var notifications = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                notifications.Add(CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task));
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        notifications.Clear();
        store.BlockProgressSaveFor(taskId!);

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await store.WaitForBlockedProgressSaveAsync(TimeSpan.FromSeconds(2));

        var stoppedCount = await service.StopAllTasksAsync();
        stoppedCount.Should().Be(1);

        store.ReleaseBlockedProgressSave();
        await progressTask.WaitAsync(TimeSpan.FromSeconds(2));

        var persistedTask = await store.LoadTaskStateAsync(taskId!);
        persistedTask.Should().NotBeNull();
        persistedTask!.Status.Should().Be(RagTaskStatus.Failed);
        persistedTask.ErrorMessage.Should().Be("Task stopped (when clearing data)");
        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Failed);
        notifications
            .SkipWhile(task => task.Status != RagTaskStatus.Failed)
            .Skip(1)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenTerminalDeleteIsInFlight_DoesNotReloadStaleProcessingTask()
    {
        var store = new BlockingProgressSaveTaskStateStore();
        var notifications = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                notifications.Add(CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task));
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        notifications.Clear();
        store.BlockDeleteFor(taskId!);
        store.BlockProgressSaveFor(taskId!);

        var completedTask = service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);
        await store.WaitForBlockedDeleteAsync(TimeSpan.FromSeconds(2));

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        var progressSaveBlocked = store.WaitForBlockedProgressSaveSignalAsync();
        var timeout = Task.Delay(TimeSpan.FromSeconds(2));
        var firstProgressResult = await Task.WhenAny(progressSaveBlocked, progressTask, timeout);
        firstProgressResult.Should().NotBe(timeout);

        store.ReleaseBlockedDelete();
        await completedTask.WaitAsync(TimeSpan.FromSeconds(2));
        store.ReleaseBlockedProgressSave();
        await progressTask.WaitAsync(TimeSpan.FromSeconds(2));

        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Completed);
        notifications
            .SkipWhile(task => task.Status != RagTaskStatus.Completed)
            .Skip(1)
            .Should()
            .BeEmpty();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenTerminalStatusWinsDuringPublish_DoesNotPublishStaleProgress()
    {
        var store = new InMemoryRagTaskStateStore();
        var notifications = new List<RagTask>();
        var progressPublishReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProgressPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var notification = CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task);
                notifications.Add(notification);
                if (notification.Status == RagTaskStatus.Completed)
                {
                    completedPublished.TrySetResult();
                }

                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        service.BeforeProgressPublishForTesting = currentTaskId =>
        {
            if (currentTaskId == taskId)
            {
                progressPublishReady.TrySetResult();
                return releaseProgressPublish.Task;
            }

            return Task.CompletedTask;
        };
        notifications.Clear();

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await progressPublishReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var completedTask = service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);
        await completedPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        releaseProgressPublish.TrySetResult();
        await Task.WhenAll(progressTask, completedTask).WaitAsync(TimeSpan.FromSeconds(2));

        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Completed);
        notifications
            .SkipWhile(task => task.Status != RagTaskStatus.Completed)
            .Skip(1)
            .Should()
            .BeEmpty();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WhenProgressPublishIsInFlight_BlocksTerminalPublish()
    {
        var store = new InMemoryRagTaskStateStore();
        var notifications = new List<RagTask>();
        var progressPublishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProgressPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var notification = CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task);
                if (notification.Status == RagTaskStatus.Processing &&
                    notification.CurrentStage == TaskStage.DocumentChunking)
                {
                    progressPublishStarted.TrySetResult();
                    await releaseProgressPublish.Task.WaitAsync(call.Arg<CancellationToken>());
                }

                lock (notifications)
                {
                    notifications.Add(notification);
                }

                if (notification.Status == RagTaskStatus.Completed)
                {
                    completedPublished.TrySetResult();
                }
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        notifications.Clear();

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await progressPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var completedTask = service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Completed);
        var completedBeforeProgressReleased = await Task.WhenAny(
            completedPublished.Task,
            Task.Delay(TimeSpan.FromMilliseconds(150))) == completedPublished.Task;

        completedBeforeProgressReleased.Should().BeFalse(
            "terminal publication must wait for the in-flight progress publication gate");

        releaseProgressPublish.TrySetResult();
        await Task.WhenAll(progressTask, completedTask).WaitAsync(TimeSpan.FromSeconds(2));

        notifications.Should().HaveCount(2);
        notifications[0].Status.Should().Be(RagTaskStatus.Processing);
        notifications[0].CurrentStage.Should().Be(TaskStage.DocumentChunking);
        notifications[1].Status.Should().Be(RagTaskStatus.Completed);
        notifications
            .SkipWhile(task => task.Status != RagTaskStatus.Completed)
            .Skip(1)
            .Should()
            .BeEmpty();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
    }

    [Fact]
    public async Task RetryTaskAsync_WhenFailedAndBelowMaxRetries_RequeuesTask()
    {
        var (service, store, _, _) = CreateService();
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
    public async Task CancelTaskAsync_WhenPending_RemovesTaskAndPublishesCancelled()
    {
        var (service, store, mediator, _) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        mediator.ClearReceivedCalls();

        var cancelled = await service.CancelTaskAsync(taskId!);

        cancelled.Should().BeTrue();
        (await service.GetTaskAsync(taskId!)).Should().BeNull();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
        await mediator.Received(1).Publish(
            Arg.Is<RagTaskStatusChangedEvent>(e =>
                e.Task.TaskId == taskId &&
                e.Task.Status == RagTaskStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelTaskAsync_WhenProcessing_CancelsRegisteredTokenAndPublishesCancelled()
    {
        var (service, store, mediator, cancellationRegistry) = CreateService();
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        using var hostCancellation = new CancellationTokenSource();
        var processingToken = cancellationRegistry.RegisterProcessingTask(taskId!, hostCancellation.Token);
        mediator.ClearReceivedCalls();

        var cancelled = await service.CancelTaskAsync(taskId!);

        cancelled.Should().BeTrue();
        processingToken.IsCancellationRequested.Should().BeTrue();
        hostCancellation.IsCancellationRequested.Should().BeFalse();
        (await service.GetTaskAsync(taskId!)).Should().BeNull();
        (await store.LoadTaskStateAsync(taskId!)).Should().BeNull();
        await mediator.Received(1).Publish(
            Arg.Is<RagTaskStatusChangedEvent>(e =>
                e.Task.TaskId == taskId &&
                e.Task.Status == RagTaskStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelTaskAsync_WhenProcessingAndProgressPublishIsInFlight_CancelsRegisteredTokenBeforePublishLockReleases()
    {
        var store = new InMemoryRagTaskStateStore();
        var progressPublishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProgressPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var notification = call.Arg<RagTaskStatusChangedEvent>().Task;
                if (notification.Status == RagTaskStatus.Processing &&
                    notification.CurrentStage == TaskStage.DocumentChunking)
                {
                    progressPublishStarted.TrySetResult();
                    await releaseProgressPublish.Task.WaitAsync(call.Arg<CancellationToken>());
                }
            });
        var cancellationRegistry = new RagTaskCancellationRegistry();
        var service = new RagTaskQueueService(
            store,
            mediator,
            cancellationRegistry,
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        using var hostCancellation = new CancellationTokenSource();
        var processingToken = cancellationRegistry.RegisterProcessingTask(taskId!, hostCancellation.Token);

        var progressTask = service.UpdateTaskProgressAsync(
            taskId!,
            TaskStage.DocumentChunking,
            50);
        await progressPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelTask = service.CancelTaskAsync(taskId!);
        var cancelledBeforePublishReleased = await WaitUntilAsync(
            () => processingToken.IsCancellationRequested,
            TimeSpan.FromMilliseconds(200));
        releaseProgressPublish.TrySetResult();
        await Task.WhenAll(progressTask, cancelTask).WaitAsync(TimeSpan.FromSeconds(2));

        cancelledBeforePublishReleased.Should().BeTrue(
            "processing cancellation must not wait behind terminal publication ordering");
        processingToken.IsCancellationRequested.Should().BeTrue();
        hostCancellation.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelTaskAsync_WhenTerminalDeleteFails_SavesTerminalSnapshotBeforeCleanup()
    {
        var store = new ThrowingDeleteTaskStateStore();
        var notifications = new List<RagTask>();
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<RagTaskStatusChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                notifications.Add(CloneTask(call.Arg<RagTaskStatusChangedEvent>().Task));
                return Task.CompletedTask;
            });
        var service = new RagTaskQueueService(
            store,
            mediator,
            new RagTaskCancellationRegistry(),
            NullLogger<RagTaskQueueService>.Instance);
        var taskId = await service.EnqueueTaskAsync(7, "content", "file.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        store.ThrowDeleteFor(taskId!);
        notifications.Clear();

        var cancelled = await service.CancelTaskAsync(taskId!);

        cancelled.Should().BeTrue();
        IsTaskActiveInMemory(service, taskId!).Should().BeFalse();
        var persistedTask = await store.LoadTaskStateAsync(taskId!);
        persistedTask.Should().NotBeNull();
        persistedTask!.Status.Should().Be(RagTaskStatus.Cancelled);
        notifications.Should().ContainSingle(task => task.Status == RagTaskStatus.Cancelled);
        GetTerminalTombstoneCount(service).Should().Be(0);
        GetTaskLifecycleCount(service).Should().Be(0);
        GetPublishLockEntryCount(service).Should().Be(0);
    }

    [Fact]
    public async Task StopAllTasksAsync_FailsPendingAndProcessingTasks()
    {
        var (service, _, mediator, _) = CreateService();
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
    public async Task StopAllTasksAsync_CancelsRegisteredProcessingTaskToken()
    {
        var (service, _, _, cancellationRegistry) = CreateService();
        var taskId = await service.EnqueueTaskAsync(2, "processing", "processing.md");
        await service.UpdateTaskStatusAsync(taskId!, RagTaskStatus.Processing);
        using var hostCancellation = new CancellationTokenSource();
        var processingToken = cancellationRegistry.RegisterProcessingTask(taskId!, hostCancellation.Token);

        await service.StopAllTasksAsync();

        processingToken.IsCancellationRequested.Should().BeTrue();
        hostCancellation.IsCancellationRequested.Should().BeFalse();
        cancellationRegistry.CompleteProcessingTask(taskId!);
    }

    [Fact]
    public async Task ClearAllTasksAsync_RemovesAllTasks()
    {
        var (service, store, _, _) = CreateService();
        await service.EnqueueTaskAsync(1, "first", "first.md");
        await service.EnqueueTaskAsync(2, "second", "second.md");

        await service.ClearAllTasksAsync();

        var tasks = await service.GetAllTasksAsync();
        tasks.Should().BeEmpty();
        var persistedTasks = await store.LoadAllTasksAsync();
        persistedTasks.Should().BeEmpty();
    }

    private static (
        RagTaskQueueService Service,
        InMemoryRagTaskStateStore Store,
        IMediator Mediator,
        RagTaskCancellationRegistry CancellationRegistry) CreateService()
    {
        var store = new InMemoryRagTaskStateStore();
        var mediator = Substitute.For<IMediator>();
        var cancellationRegistry = new RagTaskCancellationRegistry();
        var service = new RagTaskQueueService(
            store,
            mediator,
            cancellationRegistry,
            NullLogger<RagTaskQueueService>.Instance);

        return (service, store, mediator, cancellationRegistry);
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

    private static int GetTerminalTombstoneCount(RagTaskQueueService service)
    {
        var field = typeof(RagTaskQueueService).GetField(
            "_terminalTaskIds",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        var tombstones = field!.GetValue(service);
        tombstones.Should().NotBeNull();

        var countProperty = tombstones!.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();
        return (int)countProperty!.GetValue(tombstones)!;
    }

    private static int GetTaskLifecycleCount(RagTaskQueueService service)
    {
        var field = typeof(RagTaskQueueService).GetField(
            "_taskLifecycles",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        var lifecycles = field!.GetValue(service);
        lifecycles.Should().NotBeNull();

        var countProperty = lifecycles!.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();
        return (int)countProperty!.GetValue(lifecycles)!;
    }

    private static int GetPublishLockEntryCount(RagTaskQueueService service)
    {
        var publishLocksField = typeof(RagTaskQueueService).GetField(
            "_publishLocks",
            BindingFlags.Instance | BindingFlags.NonPublic);

        publishLocksField.Should().NotBeNull();
        var publishLocks = publishLocksField!.GetValue(service);
        publishLocks.Should().NotBeNull();

        var entriesField = publishLocks!.GetType().GetField(
            "_entries",
            BindingFlags.Instance | BindingFlags.NonPublic);

        entriesField.Should().NotBeNull();
        var entries = entriesField!.GetValue(publishLocks);
        entries.Should().NotBeNull();

        var countProperty = entries!.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();
        return (int)countProperty!.GetValue(entries)!;
    }

    private static bool IsTaskActiveInMemory(RagTaskQueueService service, string taskId)
    {
        var field = typeof(RagTaskQueueService).GetField(
            "_tasks",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        var tasks = field!.GetValue(service);
        tasks.Should().NotBeNull();

        var containsKeyMethod = tasks!.GetType().GetMethod("ContainsKey");
        containsKeyMethod.Should().NotBeNull();
        return (bool)containsKeyMethod!.Invoke(tasks, [taskId])!;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }

        return predicate();
    }

    private sealed class BlockingProgressSaveTaskStateStore : IRagTaskStateStore
    {
        private readonly Dictionary<string, RagTask> tasksById = [];
        private readonly TaskCompletionSource blockedProgressSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseBlockedProgressSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource blockedDelete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseBlockedDelete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? blockedTaskId;
        private string? blockedDeleteTaskId;

        public void BlockProgressSaveFor(string taskId)
        {
            blockedTaskId = taskId;
        }

        public void BlockDeleteFor(string taskId)
        {
            blockedDeleteTaskId = taskId;
        }

        public async Task WaitForBlockedProgressSaveAsync(TimeSpan timeout)
        {
            await blockedProgressSave.Task.WaitAsync(timeout);
        }

        public Task WaitForBlockedProgressSaveSignalAsync()
        {
            return blockedProgressSave.Task;
        }

        public async Task WaitForBlockedDeleteAsync(TimeSpan timeout)
        {
            await blockedDelete.Task.WaitAsync(timeout);
        }

        public void ReleaseBlockedProgressSave()
        {
            releaseBlockedProgressSave.TrySetResult();
        }

        public void ReleaseBlockedDelete()
        {
            releaseBlockedDelete.TrySetResult();
        }

        public async Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
        {
            if (task.TaskId == blockedTaskId &&
                task.Status == RagTaskStatus.Processing &&
                task.CurrentStage == TaskStage.DocumentChunking)
            {
                blockedProgressSave.TrySetResult();
                await releaseBlockedProgressSave.Task.WaitAsync(cancellationToken);
            }

            tasksById[task.TaskId] = CloneTask(task);
        }

        public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasksById.Values.Select(CloneTask).ToList());
        }

        public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasksById.TryGetValue(taskId, out var task) ? CloneTask(task) : null);
        }

        public async Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            if (taskId == blockedDeleteTaskId)
            {
                blockedDelete.TrySetResult();
                await releaseBlockedDelete.Task.WaitAsync(cancellationToken);
            }

            tasksById.Remove(taskId);
        }

        public Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
        {
            tasksById.Clear();

            foreach (var task in tasks)
            {
                tasksById[task.TaskId] = CloneTask(task);
            }

            return Task.CompletedTask;
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            tasksById.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDeleteTaskStateStore : IRagTaskStateStore
    {
        private readonly Dictionary<string, RagTask> tasksById = [];
        private string? deleteThrowsForTaskId;

        public void ThrowDeleteFor(string taskId)
        {
            deleteThrowsForTaskId = taskId;
        }

        public Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
        {
            tasksById[task.TaskId] = CloneTask(task);
            return Task.CompletedTask;
        }

        public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasksById.Values.Select(CloneTask).ToList());
        }

        public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasksById.TryGetValue(taskId, out var task) ? CloneTask(task) : null);
        }

        public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            if (taskId == deleteThrowsForTaskId)
            {
                throw new IOException("delete failed");
            }

            tasksById.Remove(taskId);
            return Task.CompletedTask;
        }

        public Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
        {
            tasksById.Clear();

            foreach (var task in tasks)
            {
                tasksById[task.TaskId] = CloneTask(task);
            }

            return Task.CompletedTask;
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            tasksById.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSaveTaskStateStore : IRagTaskStateStore
    {
        public Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
        {
            throw new IOException("save failed");
        }

        public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<RagTask>());
        }

        public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
