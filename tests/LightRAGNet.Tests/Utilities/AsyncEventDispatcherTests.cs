using FluentAssertions;
using LightRAGNet.Services.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LightRAGNet.Tests.Utilities;

public sealed class AsyncEventDispatcherTests
{
    [Fact]
    public async Task EnqueueAsync_ProcessesEventsSerially()
    {
        var seen = new List<int>();
        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (value, _) =>
            {
                seen.Add(value);
                await Task.Delay(10);
            },
            NullLogger.Instance);

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueAsync(2);

        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 2);
    }

    [Fact]
    public async Task EnqueueLatestAsync_WhenKeyMatches_ProcessesOnlyLatestPendingValue()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();

        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (value, _) =>
            {
                seen.Add(value);

                if (value == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
            },
            NullLogger.Instance,
            value => "same-key");

        await dispatcher.EnqueueLatestAsync(1);
        await firstStarted.Task;
        await dispatcher.EnqueueLatestAsync(2);
        await dispatcher.EnqueueLatestAsync(3);

        releaseFirst.SetResult();
        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 3);
    }

    [Fact]
    public async Task DrainAsync_WaitsForQueuedAndCoalescedEvents()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<string>();

        await using var dispatcher = new AsyncEventDispatcher<(string Key, int Version)>(
            async (value, _) =>
            {
                seen.Add($"{value.Key}:{value.Version}");

                if (value is { Key: "a", Version: 1 })
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
            },
            NullLogger.Instance,
            value => value.Key);

        await dispatcher.EnqueueLatestAsync(("a", 1));
        await firstStarted.Task;
        await dispatcher.EnqueueLatestAsync(("b", 1));
        await dispatcher.EnqueueLatestAsync(("b", 2));

        var drainTask = dispatcher.DrainAsync();
        releaseFirst.SetResult();
        await drainTask;

        seen.Should().Equal("a:1", "b:2");
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerThrows_DoesNotStopLaterEvents()
    {
        var seen = new List<int>();

        await using var dispatcher = new AsyncEventDispatcher<int>(
            (value, _) =>
            {
                seen.Add(value);

                if (value == 1)
                {
                    throw new InvalidOperationException("first failed");
                }

                return Task.CompletedTask;
            },
            NullLogger.Instance);

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueAsync(2);

        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 2);
    }

    [Fact]
    public async Task DisposeAsync_WhenQueueHasAcceptedEvents_DrainsOrCompletesWithoutHanging()
    {
        var seen = new List<int>();

        var dispatcher = new AsyncEventDispatcher<int>(
            async (value, _) =>
            {
                seen.Add(value);
                await Task.Delay(10);
            },
            NullLogger.Instance);

        await dispatcher.EnqueueAsync(1);
        await dispatcher.EnqueueAsync(2);

        await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        seen.Should().Equal(1, 2);
    }

    [Fact]
    public async Task EnqueueAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var dispatcher = new AsyncEventDispatcher<int>(
            (_, _) => Task.CompletedTask,
            NullLogger.Instance);

        await dispatcher.DisposeAsync();

        var act = async () => await dispatcher.EnqueueAsync(1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DrainAsync_WhenLifetimeCanceledWithPendingEvents_DoesNotCompleteSuccessfully()
    {
        using var lifetime = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();

        var dispatcher = new AsyncEventDispatcher<int>(
            async (value, cancellationToken) =>
            {
                seen.Add(value);

                if (value == 1)
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            },
            NullLogger.Instance,
            cancellationToken: lifetime.Token);

        await dispatcher.EnqueueAsync(1);
        await firstStarted.Task;
        await dispatcher.EnqueueAsync(2);

        var drainTask = dispatcher.DrainAsync();
        await lifetime.CancelAsync();

        var act = async () => await drainTask.WaitAsync(TimeSpan.FromSeconds(2));

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().Equal(1);

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task DrainAsync_WhenRunningHandlerCanceledByLifetime_DoesNotCompleteSuccessfully()
    {
        using var lifetime = new CancellationTokenSource();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();

        var dispatcher = new AsyncEventDispatcher<int>(
            async (value, cancellationToken) =>
            {
                seen.Add(value);
                handlerStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            NullLogger.Instance,
            cancellationToken: lifetime.Token);

        await dispatcher.EnqueueAsync(1);
        await handlerStarted.Task;

        var drainTask = dispatcher.DrainAsync();
        await lifetime.CancelAsync();

        var act = async () => await drainTask.WaitAsync(TimeSpan.FromSeconds(2));

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().Equal(1);

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenHandlerWaitsOnToken_Completes()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new AsyncEventDispatcher<int>(
            async (_, cancellationToken) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            NullLogger.Instance);

        await dispatcher.EnqueueAsync(1);
        await handlerStarted.Task;

        await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DrainAsync_WhenRunningHandlerCanceledByDispose_DoesNotCompleteSuccessfully()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new AsyncEventDispatcher<int>(
            async (_, cancellationToken) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            NullLogger.Instance);

        await dispatcher.EnqueueAsync(1);
        await handlerStarted.Task;

        var drainTask = dispatcher.DrainAsync();

        await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        var act = async () => await drainTask.WaitAsync(TimeSpan.FromSeconds(2));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DrainAsync_WhenCallerCancellationRequested_CancelsWait()
    {
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();

        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (value, _) =>
            {
                seen.Add(value);
                handlerStarted.SetResult();
                await releaseHandler.Task;
            },
            NullLogger.Instance);
        using var callerCancellation = new CancellationTokenSource();

        await dispatcher.EnqueueAsync(1);
        await handlerStarted.Task;

        var drainTask = dispatcher.DrainAsync(callerCancellation.Token);
        await callerCancellation.CancelAsync();

        var act = async () => await drainTask;

        await act.Should().ThrowAsync<OperationCanceledException>();
        GetDrainWaiterCount(dispatcher).Should().Be(0);

        releaseHandler.SetResult();
        await dispatcher.DrainAsync();

        seen.Should().Equal(1);
    }

    [Fact]
    public async Task DrainAsync_WhenCallerCancellationRequested_ReleasesCanceledWaiterRegistration()
    {
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var dispatcher = new AsyncEventDispatcher<int>(
            async (_, _) =>
            {
                handlerStarted.SetResult();
                await releaseHandler.Task;
            },
            NullLogger.Instance);
        using var callerCancellation = new CancellationTokenSource();

        await dispatcher.EnqueueAsync(1);
        await handlerStarted.Task;

        var waiterReference = await CancelDrainAndCaptureWaiterAsync(dispatcher, callerCancellation);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        waiterReference.IsAlive.Should().BeFalse();

        releaseHandler.SetResult();
        await dispatcher.DrainAsync();
    }

    [Fact]
    public async Task EnqueueLatestAsync_AfterDispose_DoesNotInvokeKeySelector()
    {
        var keySelectorCalls = 0;
        var dispatcher = new AsyncEventDispatcher<int>(
            (_, _) => Task.CompletedTask,
            NullLogger.Instance,
            _ =>
            {
                keySelectorCalls++;
                return "key";
            });

        await dispatcher.DisposeAsync();

        var act = async () => await dispatcher.EnqueueLatestAsync(1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
        keySelectorCalls.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueLatestAsync_WithoutKeySelector_ProcessesEveryValue()
    {
        var seen = new List<int>();

        await using var dispatcher = new AsyncEventDispatcher<int>(
            (value, _) =>
            {
                seen.Add(value);
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        await dispatcher.EnqueueLatestAsync(1);
        await dispatcher.EnqueueLatestAsync(2);

        await dispatcher.DrainAsync();

        seen.Should().Equal(1, 2);
    }

    private static int GetDrainWaiterCount<T>(AsyncEventDispatcher<T> dispatcher)
    {
        var field = typeof(AsyncEventDispatcher<T>).GetField("_drainWaiters", BindingFlags.Instance | BindingFlags.NonPublic);
        var waiters = field!.GetValue(dispatcher) as System.Collections.ICollection;
        return waiters!.Count;
    }

    private static async Task<WeakReference> CancelDrainAndCaptureWaiterAsync<T>(
        AsyncEventDispatcher<T> dispatcher,
        CancellationTokenSource callerCancellation)
    {
        var drainTask = dispatcher.DrainAsync(callerCancellation.Token);
        var waiterReference = GetFirstDrainWaiterReference(dispatcher);

        await callerCancellation.CancelAsync();

        var act = async () => await drainTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        return waiterReference;
    }

    private static WeakReference GetFirstDrainWaiterReference<T>(AsyncEventDispatcher<T> dispatcher)
    {
        var field = typeof(AsyncEventDispatcher<T>).GetField("_drainWaiters", BindingFlags.Instance | BindingFlags.NonPublic);
        var waiters = (System.Collections.IList)field!.GetValue(dispatcher)!;
        return new WeakReference(waiters[0]!);
    }
}
