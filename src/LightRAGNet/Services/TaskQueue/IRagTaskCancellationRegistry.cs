namespace LightRAGNet.Services.TaskQueue;

public interface IRagTaskCancellationRegistry
{
    CancellationToken RegisterProcessingTask(string taskId, CancellationToken hostCancellationToken);

    void CompleteProcessingTask(string taskId);

    int CancelActiveTasks();
}
