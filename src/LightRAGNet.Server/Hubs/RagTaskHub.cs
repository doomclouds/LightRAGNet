using Microsoft.AspNetCore.SignalR;

namespace LightRAGNet.Server.Hubs;

/// <summary>
/// RAG task status push Hub
/// </summary>
public class RagTaskHub : Hub
{
    /// <summary>
    /// Join task group (for receiving status updates for specific tasks)
    /// </summary>
    public async Task JoinTaskGroup(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"task-{taskId}");
    }

    /// <summary>
    /// Leave task group
    /// </summary>
    public async Task LeaveTaskGroup(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"task-{taskId}");
    }

    /// <summary>
    /// Join all task groups (for task list page)
    /// </summary>
    public async Task JoinAllTasksGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-tasks");
    }

    /// <summary>
    /// Leave all task groups
    /// </summary>
    public async Task LeaveAllTasksGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all-tasks");
    }
}
