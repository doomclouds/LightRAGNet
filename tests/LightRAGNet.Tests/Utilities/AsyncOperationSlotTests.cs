using FluentAssertions;
using LightRAGNet.Services.Utilities;

namespace LightRAGNet.Tests.Utilities;

public sealed class AsyncOperationSlotTests
{
    [Fact]
    public async Task StartNewAsync_CancelsPreviousOperation()
    {
        await using var slot = new AsyncOperationSlot();
        using var firstCanceled = new ManualResetEventSlim();

        var first = await slot.StartNewAsync();
        await using var registration = first.Token.Register(firstCanceled.Set);

        var second = await slot.StartNewAsync();

        firstCanceled.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task StartNewAsync_WhenPreviousLeaseIsCanceled_KeepsPreviousTokenRegistrableUntilLeaseCompletes()
    {
        await using var slot = new AsyncOperationSlot();

        var first = await slot.StartNewAsync();
        var second = await slot.StartNewAsync();

        first.Token.IsCancellationRequested.Should().BeTrue();
        using (first.Token.Register(static () => { }))
        {
        }

        first.Invoking(static lease => lease.Token.WaitHandle.WaitOne(TimeSpan.Zero))
            .Should().NotThrow<ObjectDisposedException>();

        first.Complete();
        first.Invoking(static lease => lease.Token.WaitHandle.WaitOne(TimeSpan.Zero))
            .Should().Throw<ObjectDisposedException>();
        second.Complete();
    }

    [Fact]
    public async Task Complete_DisposesOnlyMatchingOperation()
    {
        await using var slot = new AsyncOperationSlot();

        var first = await slot.StartNewAsync();
        var second = await slot.StartNewAsync();

        first.Complete();

        var currentToken = await slot.TryGetCurrentTokenAsync();
        currentToken.Should().NotBeNull();
        currentToken.Value.Should().Be(second.Token);

        second.Complete();

        (await slot.TryGetCurrentTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_CancelsCurrentOperationAndRejectsNewOperation()
    {
        var slot = new AsyncOperationSlot();
        var lease = await slot.StartNewAsync();

        await slot.DisposeAsync();

        lease.Token.IsCancellationRequested.Should().BeTrue();
        await slot.Invoking(static slot => slot.StartNewAsync().AsTask())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_WhenCurrentLeaseIsCanceled_KeepsCurrentTokenRegistrableUntilLeaseCompletes()
    {
        var slot = new AsyncOperationSlot();
        var lease = await slot.StartNewAsync();

        await slot.DisposeAsync();

        lease.Token.IsCancellationRequested.Should().BeTrue();
        using (lease.Token.Register(static () => { }))
        {
        }

        lease.Invoking(static lease => lease.Token.WaitHandle.WaitOne(TimeSpan.Zero))
            .Should().NotThrow<ObjectDisposedException>();

        lease.Complete();
        lease.Invoking(static lease => lease.Token.WaitHandle.WaitOne(TimeSpan.Zero))
            .Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task IsCurrentAsync_WhenLeaseHasCompleted_ReturnsFalseWithoutThrowing()
    {
        await using var slot = new AsyncOperationSlot();
        var lease = await slot.StartNewAsync();

        lease.Complete();

        (await slot.IsCurrentAsync(lease)).Should().BeFalse();
    }

    [Fact]
    public async Task LeaseDispose_WhenCalledMultipleTimes_IsIdempotent()
    {
        await using var slot = new AsyncOperationSlot();
        var lease = await slot.StartNewAsync();

        lease.Dispose();
        lease.Dispose();
        lease.Complete();

        (await slot.TryGetCurrentTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TryGetCurrentTokenAsync_WhenDisposed_ReturnsNull()
    {
        var slot = new AsyncOperationSlot();
        await slot.StartNewAsync();

        await slot.DisposeAsync();

        (await slot.TryGetCurrentTokenAsync()).Should().BeNull();
    }
}
