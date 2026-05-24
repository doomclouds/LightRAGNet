using LightRAGNet.Storage;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class Neo4jHealthCheck(
    IDriver driver,
    IOptions<Neo4JOptions> options) : ISystemHealthCheck
{
    public string Id => "neo4j";

    public string Name => "Neo4j";

    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("RETURN 1 AS ok");
        var record = await cursor.SingleAsync(cancellationToken: cancellationToken);
        var result = record["ok"].As<int>();

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Neo4j is reachable.",
            new Dictionary<string, object?>
            {
                ["uri"] = options.Value.Uri,
                ["probe"] = "RETURN 1 AS ok",
                ["result"] = result
            });
    }
}
