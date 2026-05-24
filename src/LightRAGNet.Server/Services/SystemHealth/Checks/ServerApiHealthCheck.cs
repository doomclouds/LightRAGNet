namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class ServerApiHealthCheck : ISystemHealthCheck
{
    public string Id => "server-api";

    public string Name => "Server API";

    public string Category => "Server";

    public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Server API is reachable.",
            new Dictionary<string, object?>
            {
                ["reachable"] = true
            }));
    }
}
