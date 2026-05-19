namespace LightRAGNet.Services.Utilities;

public sealed class PerKeyAsyncLock<TKey>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = [];

    public async ValueTask<Lease> LockAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Entry entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existingEntry))
            {
                existingEntry = new Entry();
                _entries.Add(key, existingEntry);
            }

            entry = existingEntry;
            entry.ReferenceCount++;
        }

        return await WaitAndCreateLeaseAsync(key, entry, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Lease> WaitAndCreateLeaseAsync(
        TKey key,
        Entry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(TKey key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(TKey key, Entry entry)
    {
        var shouldDispose = false;

        lock (_gate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0
                && _entries.TryGetValue(key, out var current)
                && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            entry.Semaphore.Dispose();
        }
    }

    internal sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly PerKeyAsyncLock<TKey> _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private int _disposed;

        internal Lease(PerKeyAsyncLock<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
