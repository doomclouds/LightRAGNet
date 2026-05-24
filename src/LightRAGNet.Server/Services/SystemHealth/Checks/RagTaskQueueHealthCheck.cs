using LightRAGNet.Models;
using LightRAGNet.Services.TaskQueue;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class RagTaskQueueHealthCheck(IRagTaskStateStore stateStore) : ISystemHealthCheck
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(30);

    public string Id => "rag-task-queue";

    public string Name => "RAG task queue";

    public string Category => "Workers";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var tasks = await stateStore.LoadAllTasksAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var pending = tasks.Count(task => task.Status == RagTaskStatus.Pending);
        var processing = tasks.Count(task => task.Status == RagTaskStatus.Processing);
        var failed = tasks.Count(task => task.Status == RagTaskStatus.Failed);
        var staleActive = tasks.Count(task => IsStaleActive(task, now));
        var evidence = new Dictionary<string, object?>
        {
            ["total"] = tasks.Count,
            ["pending"] = pending,
            ["processing"] = processing,
            ["failed"] = failed,
            ["staleActive"] = staleActive
        };

        if (failed > 0 || staleActive > 0)
        {
            return SystemHealthCheckResult.Degraded(
                Id,
                Name,
                Category,
                "RAG task queue has failed or stale active tasks.",
                "Review failed RAG tasks and restart or retry stale active tasks.",
                ["Document Indexing Queue"],
                evidence);
        }

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "RAG task queue is healthy.",
            evidence);
    }

    private static bool IsStaleActive(RagTask task, DateTime now)
    {
        var activeSince = task.Status switch
        {
            RagTaskStatus.Pending => task.CreatedAt,
            RagTaskStatus.Processing => task.StartedAt ?? task.CreatedAt,
            _ => (DateTime?)null
        };

        return activeSince.HasValue && now - activeSince.Value.ToUniversalTime() > StaleThreshold;
    }
}
