using System.Reflection;
using FluentAssertions;
using LightRAGNet.Services.Utilities;

namespace LightRAGNet.Tests.Utilities;

public sealed class PerKeyAsyncLockTests
{
    [Fact]
    public async Task LockAsync_SameKey_SerializesWork()
    {
        var locker = new PerKeyAsyncLock<string>();
        var inside = 0;
        var maxInside = 0;

        var workers = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await using var lease = await locker.LockAsync("doc-1");
            var current = Interlocked.Increment(ref inside);
            UpdateMax(ref maxInside, current);

            try
            {
                await Task.Delay(5);
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }));

        await Task.WhenAll(workers);

        maxInside.Should().Be(1);
    }

    [Fact]
    public async Task LockAsync_DifferentKeys_CanRunInParallel()
    {
        var locker = new PerKeyAsyncLock<string>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = Task.Run(async () =>
        {
            await using var lease = await locker.LockAsync("doc-a");
            firstEntered.SetResult();
            await releaseFirst.Task;
        });

        await firstEntered.Task;
        await using (await locker.LockAsync("doc-b"))
        {
            secondEntered = true;
        }

        releaseFirst.SetResult();
        await first;

        secondEntered.Should().BeTrue();
    }

    [Fact]
    public async Task LockAsync_WhenWaitingCancellationRequested_Throws()
    {
        var locker = new PerKeyAsyncLock<string>();
        await using var first = await locker.LockAsync("doc-1");
        using var cts = new CancellationTokenSource();

        var waiting = locker.LockAsync("doc-1", cts.Token).AsTask();
        await cts.CancelAsync();

        await waiting.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LeaseDisposeAsync_WhenCalledMultipleTimes_IsIdempotent()
    {
        var locker = new PerKeyAsyncLock<string>();
        var lease = await locker.LockAsync("doc-1");

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maxInside = 0;
        var inside = 0;

        var first = Task.Run(async () =>
        {
            await using var firstLease = await locker.LockAsync("doc-1");
            UpdateMax(ref maxInside, Interlocked.Increment(ref inside));
            firstEntered.SetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref inside);
        });

        await firstEntered.Task;

        var second = Task.Run(async () =>
        {
            await using var secondLease = await locker.LockAsync("doc-1");
            UpdateMax(ref maxInside, Interlocked.Increment(ref inside));
            Interlocked.Decrement(ref inside);
        });

        await Task.Delay(25);
        maxInside.Should().Be(1);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        maxInside.Should().Be(1);
    }

    [Fact]
    public async Task LockAsync_AfterReleaseAndCancellation_RemovesUnusedKeyEntries()
    {
        var locker = new PerKeyAsyncLock<string>();
        var lease = await locker.LockAsync("doc-1");
        using var cts = new CancellationTokenSource();
        var waiting = locker.LockAsync("doc-1", cts.Token).AsTask();

        await cts.CancelAsync();
        await waiting.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();

        await lease.DisposeAsync();

        GetEntryCount(locker).Should().Be(0);
    }

    private static void UpdateMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var original = Interlocked.CompareExchange(ref target, value, current);
            if (original == current)
            {
                return;
            }

            current = original;
        }
    }

    private static int GetEntryCount<TKey>(PerKeyAsyncLock<TKey> locker)
        where TKey : notnull
    {
        var field = typeof(PerKeyAsyncLock<TKey>).GetField(
            "_entries",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        var entries = field!.GetValue(locker);
        entries.Should().NotBeNull();

        var countProperty = entries!.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();

        return (int)countProperty!.GetValue(entries)!;
    }
}
