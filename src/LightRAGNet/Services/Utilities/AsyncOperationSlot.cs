namespace LightRAGNet.Services.Utilities;

public sealed class AsyncOperationSlot : IAsyncDisposable
{
    private readonly object _gate = new();
    private Lease? _currentLease;
    private bool _disposed;

    public async ValueTask<Lease> StartNewAsync()
    {
        Lease? previousLease;
        Lease currentLease;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            previousLease = _currentLease;
            currentLease = new Lease(this, new CancellationTokenSource());
            _currentLease = currentLease;
        }

        if (previousLease is not null)
        {
            await previousLease.CancelAsync();
        }

        return currentLease;
    }

    /// <summary>
    /// Gets a snapshot of the current operation token for diagnostics.
    /// The returned token does not keep the backing lease alive; do not use it for long-lived registrations.
    /// </summary>
    public ValueTask<CancellationToken?> TryGetCurrentTokenAsync()
    {
        lock (_gate)
        {
            return ValueTask.FromResult<CancellationToken?>(
                _currentLease is null ? null : _currentLease.Token);
        }
    }

    public ValueTask<bool> IsCurrentAsync(Lease lease)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(ReferenceEquals(_currentLease, lease));
        }
    }

    private void Complete(Lease lease)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_currentLease, lease))
            {
                _currentLease = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Lease? currentLease;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            currentLease = _currentLease;
            _currentLease = null;
        }

        if (currentLease is not null)
        {
            await currentLease.CancelAsync();
        }
    }

    public sealed class Lease : IDisposable
    {
        private readonly AsyncOperationSlot _owner;
        private readonly CancellationTokenSource _cancellationSource;
        private int _completed;

        internal Lease(
            AsyncOperationSlot owner,
            CancellationTokenSource cancellationSource)
        {
            _owner = owner;
            _cancellationSource = cancellationSource;
            Token = cancellationSource.Token;
        }

        public CancellationToken Token { get; }

        internal async ValueTask CancelAsync()
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return;
            }

            try
            {
                await _cancellationSource.CancelAsync();
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _completed) != 0)
            {
            }
        }

        public bool Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return false;
            }

            _owner.Complete(this);
            _cancellationSource.Dispose();
            return true;
        }

        public void Dispose()
        {
            Complete();
        }
    }
}
