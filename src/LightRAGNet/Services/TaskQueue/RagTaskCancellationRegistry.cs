using System.Collections.Concurrent;

namespace LightRAGNet.Services.TaskQueue;

public sealed class RagTaskCancellationRegistry : IRagTaskCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> processingTasks = [];

    public CancellationToken RegisterProcessingTask(string taskId, CancellationToken hostCancellationToken)
    {
        var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        var existing = processingTasks.AddOrUpdate(
            taskId,
            taskCancellation,
            (_, previous) =>
            {
                previous.Dispose();
                return taskCancellation;
            });

        return existing.Token;
    }

    public void CompleteProcessingTask(string taskId)
    {
        if (processingTasks.TryRemove(taskId, out var cancellation))
        {
            cancellation.Dispose();
        }
    }

    public bool CancelTask(string taskId)
    {
        if (!processingTasks.TryGetValue(taskId, out var cancellation) ||
            cancellation.IsCancellationRequested)
        {
            return false;
        }

        cancellation.Cancel();
        return true;
    }

    public int CancelActiveTasks()
    {
        var cancelledCount = 0;
        foreach (var cancellation in processingTasks.Values)
        {
            if (cancellation.IsCancellationRequested)
            {
                continue;
            }

            cancellation.Cancel();
            cancelledCount++;
        }

        return cancelledCount;
    }
}
