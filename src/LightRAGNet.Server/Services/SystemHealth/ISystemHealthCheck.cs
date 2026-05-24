namespace LightRAGNet.Server.Services.SystemHealth;

public interface ISystemHealthCheck
{
    string Id { get; }

    string Name { get; }

    string Category { get; }

    Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}
