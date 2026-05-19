using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.Utilities;

public sealed class AsyncEventDispatcher<T> : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<T, CancellationToken, Task> _handler;
    private readonly ILogger _logger;
    private readonly Func<T, string?>? _keySelector;
    private readonly CancellationTokenSource _handlerCts;
    private readonly CancellationTokenSource _readerAbortCts;
    private readonly Channel<DispatchItem> _channel;
    private readonly Task _readerTask;
    private readonly Dictionary<string, long> _latestByKey = new(StringComparer.Ordinal);
    private readonly List<DrainWaiter> _drainWaiters = [];
    private long _acceptedSequence;
    private long _completedSequence;
    private Exception? _drainFailure;
    private bool _disposed;

    public AsyncEventDispatcher(
        Func<T, CancellationToken, Task> handler,
        ILogger logger,
        Func<T, string?>? keySelector = null,
        CancellationToken cancellationToken = default)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keySelector = keySelector;
        _handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerAbortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _channel = Channel.CreateUnbounded<DispatchItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        _readerTask = Task.Run(ReadLoopAsync);
    }

    public Task EnqueueAsync(T value, CancellationToken cancellationToken = default)
    {
        return EnqueueCoreAsync(value, coalesceByKey: false, cancellationToken);
    }

    public Task EnqueueLatestAsync(T value, CancellationToken cancellationToken = default)
    {
        return EnqueueCoreAsync(value, coalesceByKey: true, cancellationToken);
    }

    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfDisposedOrCanceled();

            var targetSequence = _acceptedSequence;
            if (_completedSequence >= targetSequence)
            {
                return Task.CompletedTask;
            }

            if (_drainFailure is not null)
            {
                return TaskFromDrainFailure(_drainFailure);
            }

            var waiter = new DrainWaiter(
                targetSequence,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _drainWaiters.Add(waiter);

            if (cancellationToken.CanBeCanceled)
            {
                waiter.CancellationRegistration = cancellationToken.Register(
                    static state =>
                    {
                        var (dispatcher, drainWaiter, token) =
                            ((AsyncEventDispatcher<T> Dispatcher, DrainWaiter Waiter, CancellationToken Token))state!;
                        dispatcher.CancelDrainWaiter(drainWaiter, token);
                    },
                    (this, waiter, cancellationToken));
            }

            return waiter.Completion.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _channel.Writer.TryComplete();
        }

        await _handlerCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _readerTask.ConfigureAwait(false);
        }
        finally
        {
            _handlerCts.Dispose();
            _readerAbortCts.Dispose();
        }
    }

    private Task EnqueueCoreAsync(T value, bool coalesceByKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DispatchItem item;

        lock (_gate)
        {
            ThrowIfDisposedOrCanceled();

            // Without a key selector, latest enqueue intentionally behaves like ordinary FIFO enqueue.
            var key = coalesceByKey ? _keySelector?.Invoke(value) : null;
            var sequence = ++_acceptedSequence;
            item = new DispatchItem(value, sequence, key);

            if (key is not null)
            {
                _latestByKey[key] = sequence;
            }

            if (!_channel.Writer.TryWrite(item))
            {
                _acceptedSequence--;
                if (key is not null && _latestByKey.TryGetValue(key, out var latestSequence) && latestSequence == sequence)
                {
                    _latestByKey.Remove(key);
                }

                ThrowIfDisposedOrCanceled();
                throw new InvalidOperationException("The async event dispatcher is not accepting events.");
            }
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_readerAbortCts.Token).ConfigureAwait(false))
            {
                if (!ShouldHandle(item))
                {
                    Complete(item);
                    continue;
                }

                try
                {
                    await _handler(item.Value, _handlerCts.Token).ConfigureAwait(false);
                    Complete(item);
                }
                catch (OperationCanceledException ex) when (_handlerCts.IsCancellationRequested)
                {
                    FailIncompleteDrainWaiters(ex);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Async event dispatcher handler failed for {Value}", item.Value);
                    Complete(item);
                }
            }
        }
        catch (OperationCanceledException ex) when (_readerAbortCts.IsCancellationRequested)
        {
            FailIncompleteDrainWaiters(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Async event dispatcher reader failed.");
            FailIncompleteDrainWaiters(ex);
        }
        finally
        {
            CompleteReadyDrainWaiters();
        }
    }

    private bool ShouldHandle(DispatchItem item)
    {
        if (item.Key is null)
        {
            return true;
        }

        lock (_gate)
        {
            return _latestByKey.TryGetValue(item.Key, out var latestSequence)
                && latestSequence == item.Sequence;
        }
    }

    private void Complete(DispatchItem item)
    {
        List<DrainWaiter> readyWaiters;

        lock (_gate)
        {
            if (item.Key is not null
                && _latestByKey.TryGetValue(item.Key, out var latestSequence)
                && latestSequence == item.Sequence)
            {
                _latestByKey.Remove(item.Key);
            }

            if (item.Sequence > _completedSequence)
            {
                _completedSequence = item.Sequence;
            }

            readyWaiters = TakeReadyDrainWaiters();
        }

        CompleteDrainWaiters(readyWaiters);
    }

    private void FailIncompleteDrainWaiters(Exception exception)
    {
        List<DrainWaiter> readyWaiters;
        List<DrainWaiter> failedWaiters;

        lock (_gate)
        {
            if (_completedSequence >= _acceptedSequence)
            {
                readyWaiters = TakeReadyDrainWaiters();
                failedWaiters = [];
            }
            else
            {
                _drainFailure ??= exception;
                readyWaiters = [];
                failedWaiters = [];

                for (var index = _drainWaiters.Count - 1; index >= 0; index--)
                {
                    var waiter = _drainWaiters[index];
                    if (waiter.TargetSequence <= _completedSequence)
                    {
                        readyWaiters.Add(waiter);
                    }
                    else
                    {
                        failedWaiters.Add(waiter);
                    }

                    _drainWaiters.RemoveAt(index);
                }
            }
        }

        CompleteDrainWaiters(readyWaiters);
        FailDrainWaiters(failedWaiters, exception);
    }

    private void CompleteReadyDrainWaiters()
    {
        List<DrainWaiter> readyWaiters;

        lock (_gate)
        {
            readyWaiters = TakeReadyDrainWaiters();
        }

        CompleteDrainWaiters(readyWaiters);
    }

    private List<DrainWaiter> TakeReadyDrainWaiters()
    {
        var readyWaiters = new List<DrainWaiter>();

        for (var index = _drainWaiters.Count - 1; index >= 0; index--)
        {
            var waiter = _drainWaiters[index];
            if (_completedSequence < waiter.TargetSequence)
            {
                continue;
            }

            readyWaiters.Add(waiter);
            _drainWaiters.RemoveAt(index);
        }

        return readyWaiters;
    }

    private static void CompleteDrainWaiters(List<DrainWaiter> waiters)
    {
        foreach (var waiter in waiters)
        {
            waiter.TrySetResult();
        }
    }

    private static void FailDrainWaiters(List<DrainWaiter> waiters, Exception exception)
    {
        foreach (var waiter in waiters)
        {
            waiter.TrySetFailure(exception);
        }
    }

    private void CancelDrainWaiter(DrainWaiter waiter, CancellationToken cancellationToken)
    {
        var removed = false;

        lock (_gate)
        {
            removed = _drainWaiters.Remove(waiter);
        }

        if (removed)
        {
            waiter.TrySetCanceledFromCancellationCallback(cancellationToken);
        }
    }

    private void ThrowIfDisposedOrCanceled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_readerAbortCts.IsCancellationRequested)
        {
            throw new OperationCanceledException(_readerAbortCts.Token);
        }
    }

    private static Task TaskFromDrainFailure(Exception exception)
    {
        if (exception is OperationCanceledException cancellationException)
        {
            return Task.FromCanceled(cancellationException.CancellationToken);
        }

        return Task.FromException(exception);
    }

    private sealed record DispatchItem(T Value, long Sequence, string? Key);

    private sealed class DrainWaiter(long targetSequence, TaskCompletionSource completion)
    {
        public long TargetSequence { get; } = targetSequence;

        public TaskCompletionSource Completion { get; } = completion;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public void TrySetResult()
        {
            Completion.TrySetResult();
            CancellationRegistration.Dispose();
        }

        public void TrySetCanceledFromCancellationCallback(CancellationToken cancellationToken)
        {
            Completion.TrySetCanceled(cancellationToken);
            QueueCancellationRegistrationDispose();
        }

        public void TrySetFailure(Exception exception)
        {
            if (exception is OperationCanceledException cancellationException)
            {
                Completion.TrySetCanceled(cancellationException.CancellationToken);
            }
            else
            {
                Completion.TrySetException(exception);
            }

            CancellationRegistration.Dispose();
        }

        private void QueueCancellationRegistrationDispose()
        {
            ThreadPool.QueueUserWorkItem(static state =>
            {
                ((DrainWaiter)state!).CancellationRegistration.Dispose();
            }, this);
        }
    }
}
