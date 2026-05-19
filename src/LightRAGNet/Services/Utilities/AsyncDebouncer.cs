namespace LightRAGNet.Services.Utilities;

public sealed class AsyncDebouncer : IAsyncDisposable
{
    private readonly AsyncOperationSlot _slot = new();

    public async Task DebounceAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action)
    {
        AsyncOperationSlot.Lease lease;
        try
        {
            lease = await _slot.StartNewAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        using (lease)
        {
            try
            {
                await Task.Delay(delay, lease.Token);
                if (!await _slot.IsCurrentAsync(lease))
                {
                    return;
                }

                await action(lease.Token);
            }
            catch (OperationCanceledException) when (lease.Token.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _slot.DisposeAsync();
    }
}
