using LightRAGNet.Services.Utilities;
using Microsoft.AspNetCore.SignalR.Client;

namespace LightRAGNet.Web.Services;

/// <summary>
/// RAG task status notification service - receives task status updates via SignalR
/// </summary>
public class RagTaskNotificationService : IAsyncDisposable
{
    private readonly ILogger<RagTaskNotificationService> logger;
    private HubConnection? _hubConnection;
    private readonly string _apiBaseUrl;
    private readonly object _disposeGate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly AsyncEventDispatcher<TaskStatusUpdate> _taskStatusDispatchQueue;
    private readonly AsyncEventDispatcher<NotificationDispatchKey> _systemDispatchQueue;
    private bool _isInitializing;
    private bool _disposed;

    private enum NotificationDispatchKey
    {
        DataCleared
    }

    public RagTaskNotificationService(
        ILogger<RagTaskNotificationService> logger,
        IConfiguration configuration)
    {
        this.logger = logger;
        _apiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5261";
        _taskStatusDispatchQueue = new AsyncEventDispatcher<TaskStatusUpdate>(
            async (update, token) => await NotifyTaskStatusHandlersAsync(update, token),
            logger,
            keySelector: update => update.TaskId);
        _systemDispatchQueue = new AsyncEventDispatcher<NotificationDispatchKey>(
            async (key, token) =>
            {
                if (key == NotificationDispatchKey.DataCleared)
                {
                    // Drain the accepted task-status updates snapshot before data-cleared handlers run.
                    await _taskStatusDispatchQueue.DrainAsync(token);
                    await NotifyDataClearedHandlersAsync(token);
                }
            },
            logger);
    }

    /// <summary>
    /// Task status update event (async)
    /// </summary>
    public event Func<object, TaskStatusUpdate, Task>? TaskStatusUpdated;

    /// <summary>
    /// Whether connected
    /// </summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Connection state change event (async)
    /// </summary>
    public event Func<object, string, Task>? ConnectionStateChanged;

    /// <summary>
    /// Data cleared event (triggered when all data is cleared, async)
    /// </summary>
    public event Func<object, EventArgs, Task>? DataCleared;

    /// <summary>
    /// Initialize and connect SignalR Hub
    /// </summary>
    public async Task InitializeAsync()
    {
        CancellationToken disposeToken;
        lock (_disposeGate)
        {
            if (_disposed)
            {
                return;
            }

            disposeToken = _disposeCts.Token;
        }

        if (disposeToken.IsCancellationRequested)
        {
            return;
        }

        // If already connected or connecting, return directly
        if (_hubConnection != null)
        {
            var state = _hubConnection.State;
            if (state is HubConnectionState.Connected or HubConnectionState.Connecting
                or HubConnectionState.Reconnecting)
            {
                return;
            }
        }

        // Use lock to prevent concurrent initialization
        var lockTaken = false;
        try
        {
            await _initLock.WaitAsync(disposeToken);
            lockTaken = true;
        }
        catch (OperationCanceledException) when (disposeToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        
        try
        {
            if (_disposed || disposeToken.IsCancellationRequested)
            {
                return;
            }

            // Double check to prevent concurrency
            if (_isInitializing)
            {
                // Wait for initialization to complete
                while (_isInitializing && _hubConnection?.State != HubConnectionState.Connected)
                {
                    await Task.Delay(100, disposeToken);
                }
                return;
            }

            if (_hubConnection != null)
            {
                var state = _hubConnection.State;
                if (state is HubConnectionState.Connected or HubConnectionState.Connecting
                    or HubConnectionState.Reconnecting)
                {
                    return;
                }
                
                // If connection is disconnected, clean up first
                try
                {
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error cleaning up old connection");
                }
                _hubConnection = null;
            }

            _isInitializing = true;

            var hubUrl = $"{_apiBaseUrl.TrimEnd('/')}/hubs/ragtask";
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Register handler for receiving task status updates
            _hubConnection.On<TaskStatusUpdate>("TaskStatusUpdated", update =>
            {
                logger.LogInformation("Received task status update: TaskId={TaskId}, Status={Status}, Progress={Progress}, Stage={Stage}", 
                    update.TaskId, update.Status, update.Progress, update.CurrentStage);

                try
                {
                    _ = _taskStatusDispatchQueue.EnqueueLatestAsync(update);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enqueue task status update notification: TaskId={TaskId}", update.TaskId);
                }
            });

            // Register handler for receiving data cleared events
            _hubConnection.On("DataCleared", () =>
            {
                logger.LogInformation("Received data cleared event, notifying frontend to refresh");

                try
                {
                    _ = _systemDispatchQueue.EnqueueAsync(NotificationDispatchKey.DataCleared);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enqueue data cleared notification");
                }
            });

            // Listen to connection state changes
            _hubConnection.Closed += async (error) =>
            {
                logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
                await NotifyConnectionStateChangedFromHubCallbackAsync("Disconnected", disposeToken);
            };

            _hubConnection.Reconnecting += async (error) =>
            {
                logger.LogInformation("SignalR reconnecting: {Error}", error?.Message);
                await NotifyConnectionStateChangedFromHubCallbackAsync("Reconnecting", disposeToken);
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                logger.LogInformation("SignalR reconnected: ConnectionId={ConnectionId}", connectionId);
                await NotifyConnectionStateChangedFromHubCallbackAsync("Connected", disposeToken);
                
                // Rejoin all task groups after reconnection
                await JoinAllTasksGroupAsync(disposeToken);
            };

            try
            {
                await _hubConnection.StartAsync(disposeToken);
                logger.LogInformation("SignalR connection established");
                await NotifyConnectionStateChangedHandlersAsync("Connected", disposeToken);
                
                // Join all task groups
                await JoinAllTasksGroupAsync(disposeToken);
            }
            catch (OperationCanceledException) when (disposeToken.IsCancellationRequested)
            {
                logger.LogDebug("SignalR connection initialization stopped because service is disposing");
            }
            catch (TaskCanceledException)
            {
                // Task was cancelled (may be due to page navigation), don't log error
                logger.LogWarning("SignalR connection initialization cancelled");
                await NotifyConnectionStateChangedHandlersAsync("Disconnected", disposeToken);
            }
            catch (Exception ex)
            {
                // Check if it's a connection refused error (Server not started)
                var isConnectionRefused = ex is HttpRequestException httpEx && 
                                        httpEx.InnerException is System.Net.Sockets.SocketException socketEx &&
                                        socketEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused;
                
                if (isConnectionRefused)
                {
                    logger.LogWarning("SignalR connection failed: Server application not started (please start LightRAGNet.Server first)");
                    await NotifyConnectionStateChangedHandlersAsync("ServerNotStarted", disposeToken);
                }
                else
                {
                    logger.LogError(ex, "SignalR connection failed");
                    await NotifyConnectionStateChangedHandlersAsync("Disconnected", disposeToken);
                }
            }
        }
        finally
        {
            if (lockTaken)
            {
                _isInitializing = false;
                _initLock.Release();
            }
        }
    }

    /// <summary>
    /// Join all task groups
    /// </summary>
    private async Task JoinAllTasksGroupAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("JoinAllTasksGroup", cancellationToken);
                logger.LogInformation("Joined all task groups, can receive task status updates");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Joining task groups stopped because service is disposing");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to join all task groups");
            }
        }
        else
        {
            logger.LogWarning("Cannot join task groups: SignalR connection not established, current state: {State}", _hubConnection?.State);
        }
    }

    private async Task NotifyTaskStatusHandlersAsync(TaskStatusUpdate update, CancellationToken cancellationToken)
    {
        var handlers = TaskStatusUpdated?.GetInvocationList()
            .Cast<Func<object, TaskStatusUpdate, Task>>()
            .ToArray();

        if (handlers is null || handlers.Length == 0)
            return;

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await handler(this, update);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Task status update event handler cancelled itself: TaskId={TaskId}", update.TaskId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling task status update event handler: TaskId={TaskId}", update.TaskId);
            }
        }
    }

    private async Task NotifyDataClearedHandlersAsync(CancellationToken cancellationToken)
    {
        var handlers = DataCleared?.GetInvocationList()
            .Cast<Func<object, EventArgs, Task>>()
            .ToArray();

        if (handlers is null || handlers.Length == 0)
            return;

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await handler(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Data cleared event handler cancelled itself");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling data cleared event handler");
            }
        }
    }

    private async Task NotifyConnectionStateChangedFromHubCallbackAsync(string state, CancellationToken cancellationToken)
    {
        try
        {
            await NotifyConnectionStateChangedHandlersAsync(state, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("SignalR lifecycle notification skipped because service is disposing: State={State}", state);
        }
    }

    private async Task NotifyConnectionStateChangedHandlersAsync(string state, CancellationToken cancellationToken = default)
    {
        var handlers = ConnectionStateChanged?.GetInvocationList()
            .Cast<Func<object, string, Task>>()
            .ToArray();

        if (handlers is null || handlers.Length == 0)
            return;

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await handler(this, state);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Connection state change event handler cancelled itself: State={State}", state);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling connection state change event handler: State={State}", state);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await _disposeCts.CancelAsync();

        var lockTaken = false;
        try
        {
            await _initLock.WaitAsync();
            lockTaken = true;

            if (_hubConnection is not null)
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
        finally
        {
            if (lockTaken)
            {
                _initLock.Release();
            }
        }

        await _systemDispatchQueue.DisposeAsync();
        await _taskStatusDispatchQueue.DisposeAsync();
        _initLock.Dispose();
        _disposeCts.Dispose();
    }
}

/// <summary>
/// Task status update data
/// </summary>
public class TaskStatusUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public string OperationType { get; set; } = "IndexDocument";
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? CurrentStage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
