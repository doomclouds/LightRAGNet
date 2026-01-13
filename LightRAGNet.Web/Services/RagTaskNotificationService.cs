using Microsoft.AspNetCore.SignalR.Client;

namespace LightRAGNet.Web.Services;

/// <summary>
/// RAG task status notification service - receives task status updates via SignalR
/// </summary>
public class RagTaskNotificationService(
    ILogger<RagTaskNotificationService> logger,
    IConfiguration configuration)
{
    private HubConnection? _hubConnection;
    private readonly string _apiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5261";
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitializing;

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
        try
        {
            await _initLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            // If SemaphoreSlim has been disposed, recreate (should not happen, but as a safety measure)
            logger.LogWarning("SemaphoreSlim has been disposed, recreating");
            return; // Or throw exception, depending on business logic
        }
        
        try
        {
            // Double check to prevent concurrency
            if (_isInitializing)
            {
                // Wait for initialization to complete
                while (_isInitializing && _hubConnection?.State != HubConnectionState.Connected)
                {
                    await Task.Delay(100);
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
                
                // Execute asynchronously, don't wait for completion
                if (TaskStatusUpdated != null)
                {
                    var tasks = TaskStatusUpdated.GetInvocationList()
                        .Cast<Func<object, TaskStatusUpdate, Task>>()
                        .Select(handler =>
                        {
                            try
                            {
                                return handler(this, update);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Error calling task status update event handler: TaskId={TaskId}", update.TaskId);
                                return Task.CompletedTask;
                            }
                        });

                    Task.WhenAll(tasks);
                }
            });

            // Register handler for receiving data cleared events
            _hubConnection.On("DataCleared", () =>
            {
                logger.LogInformation("Received data cleared event, notifying frontend to refresh");
                
                // Execute asynchronously, don't wait for completion
                if (DataCleared != null)
                {
                    var tasks = DataCleared.GetInvocationList()
                        .Cast<Func<object, EventArgs, Task>>()
                        .Select(handler =>
                        {
                            try
                            {
                                return handler(this, EventArgs.Empty);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Error calling data cleared event handler");
                                return Task.CompletedTask;
                            }
                        });
                    
                    Task.WhenAll(tasks);
                }
            });

            // Listen to connection state changes
            _hubConnection.Closed += async (error) =>
            {
                logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
                if (ConnectionStateChanged != null)
                {
                    var tasks = ConnectionStateChanged.GetInvocationList()
                        .Cast<Func<object, string, Task>>()
                        .Select(handler => handler(this, "Disconnected"));
                    await Task.WhenAll(tasks);
                }
            };

            _hubConnection.Reconnecting += async (error) =>
            {
                logger.LogInformation("SignalR reconnecting: {Error}", error?.Message);
                if (ConnectionStateChanged != null)
                {
                    var tasks = ConnectionStateChanged.GetInvocationList()
                        .Cast<Func<object, string, Task>>()
                        .Select(handler => handler(this, "Reconnecting"));
                    await Task.WhenAll(tasks);
                }
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                logger.LogInformation("SignalR reconnected: ConnectionId={ConnectionId}", connectionId);
                if (ConnectionStateChanged != null)
                {
                    var tasks = ConnectionStateChanged.GetInvocationList()
                        .Cast<Func<object, string, Task>>()
                        .Select(handler => handler(this, "Connected"));
                    await Task.WhenAll(tasks);
                }
                
                // Rejoin all task groups after reconnection
                await JoinAllTasksGroupAsync();
            };

            try
            {
                await _hubConnection.StartAsync();
                logger.LogInformation("SignalR connection established");
                if (ConnectionStateChanged != null)
                {
                    var tasks = ConnectionStateChanged.GetInvocationList()
                        .Cast<Func<object, string, Task>>()
                        .Select(handler => handler(this, "Connected"));
                    await Task.WhenAll(tasks);
                }
                
                // Join all task groups
                await JoinAllTasksGroupAsync();
            }
            catch (TaskCanceledException)
            {
                // Task was cancelled (may be due to page navigation), don't log error
                logger.LogWarning("SignalR connection initialization cancelled");
                if (ConnectionStateChanged != null)
                {
                    var tasks = ConnectionStateChanged.GetInvocationList()
                        .Cast<Func<object, string, Task>>()
                        .Select(handler => handler(this, "Disconnected"));
                    await Task.WhenAll(tasks);
                }
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
                    if (ConnectionStateChanged != null)
                    {
                        var tasks = ConnectionStateChanged.GetInvocationList()
                            .Cast<Func<object, string, Task>>()
                            .Select(handler => handler(this, "ServerNotStarted"));
                        await Task.WhenAll(tasks);
                    }
                }
                else
                {
                    logger.LogError(ex, "SignalR connection failed");
                    if (ConnectionStateChanged != null)
                    {
                        var tasks = ConnectionStateChanged.GetInvocationList()
                            .Cast<Func<object, string, Task>>()
                            .Select(handler => handler(this, "Disconnected"));
                        await Task.WhenAll(tasks);
                    }
                }
            }
        }
        finally
        {
            _isInitializing = false;
            try
            {
                _initLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // If SemaphoreSlim has been disposed, ignore
                logger.LogWarning("Attempting to release already disposed SemaphoreSlim");
            }
        }
    }

    /// <summary>
    /// Join all task groups
    /// </summary>
    private async Task JoinAllTasksGroupAsync()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("JoinAllTasksGroup");
                logger.LogInformation("Joined all task groups, can receive task status updates");
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
}

/// <summary>
/// Task status update data
/// </summary>
public class TaskStatusUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? CurrentStage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
