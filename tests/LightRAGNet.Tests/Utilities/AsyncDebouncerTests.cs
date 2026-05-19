using FluentAssertions;
using LightRAGNet.Services.Utilities;

namespace LightRAGNet.Tests.Utilities;

public sealed class AsyncDebouncerTests
{
    [Fact]
    public async Task DebounceAsync_WhenRequestsOverlap_RunsOnlyLatestAction()
    {
        await using var debouncer = new AsyncDebouncer();
        var runCount = 0;

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => debouncer.DebounceAsync(
                TimeSpan.FromMilliseconds(50),
                _ =>
                {
                    Interlocked.Increment(ref runCount);
                    return Task.CompletedTask;
                }))
            .ToArray();

        await Task.WhenAll(tasks);

        runCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenRequestIsPending_CancelsActionWithoutThrowing()
    {
        var debouncer = new AsyncDebouncer();
        var ran = false;

        var pending = debouncer.DebounceAsync(
            TimeSpan.FromSeconds(5),
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            });

        await debouncer.DisposeAsync();
        await pending;

        ran.Should().BeFalse();
    }
}
