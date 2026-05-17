using LightRAGNet.Models;
using LightRAGNet.Services.TaskQueue;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class InMemoryRagTaskStateStore : IRagTaskStateStore
{
    private readonly Dictionary<string, RagTask> tasksById = [];
    private readonly List<string> savedTaskIds = [];

    public int GetSaveCount(string taskId)
    {
        return savedTaskIds.Count(savedTaskId => savedTaskId == taskId);
    }

    public Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
    {
        tasksById[task.TaskId] = task;
        savedTaskIds.Add(task.TaskId);
        return Task.CompletedTask;
    }

    public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(tasksById.Values.ToList());
    }

    public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        tasksById.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        tasksById.Remove(taskId);
        return Task.CompletedTask;
    }

    public Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
    {
        tasksById.Clear();

        foreach (var task in tasks)
        {
            tasksById[task.TaskId] = task;
        }

        return Task.CompletedTask;
    }

    public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
    {
        tasksById.Clear();
        return Task.CompletedTask;
    }
}
