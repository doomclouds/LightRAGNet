namespace LightRAGNet.Services.Utilities;

public sealed class AsyncDebouncer : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCts;
    private long _version;
    private bool _disposed;

    public async Task DebounceAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action)
    {
        CancellationTokenSource? previousCts;
        CancellationTokenSource currentCts;
        CancellationToken currentToken;
        long currentVersion;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previousCts = _currentCts;
            currentCts = new CancellationTokenSource();
            currentToken = currentCts.Token;
            currentVersion = ++_version;
            _currentCts = currentCts;
        }

        if (previousCts is not null)
        {
            await previousCts.CancelAsync();
            previousCts.Dispose();
        }

        try
        {
            await Task.Delay(delay, currentToken);

            lock (_gate)
            {
                if (_disposed ||
                    !ReferenceEquals(_currentCts, currentCts) ||
                    currentVersion != _version)
                {
                    return;
                }
            }

            await action(currentToken);
        }
        catch (OperationCanceledException) when (currentToken.IsCancellationRequested)
        {
        }
        finally
        {
            var shouldDispose = false;

            lock (_gate)
            {
                if (ReferenceEquals(_currentCts, currentCts))
                {
                    _currentCts = null;
                    shouldDispose = true;
                }
            }

            if (shouldDispose)
            {
                currentCts.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? currentCts;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            currentCts = _currentCts;
            _currentCts = null;
        }

        if (currentCts is not null)
        {
            await currentCts.CancelAsync();
            currentCts.Dispose();
        }
    }
}
