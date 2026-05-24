using LightRAGNet.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class SqliteHealthCheck(AppDbContext dbContext) : ISystemHealthCheck
{
    public string Id => "sqlite";

    public string Name => "SQLite";

    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return SystemHealthCheckResult.Unhealthy(
                Id,
                Name,
                Category,
                "SQLite database cannot be reached.",
                "Verify the SQLite connection string and database file permissions.",
                ["Web Management"],
                new Dictionary<string, object?>
                {
                    ["canConnect"] = false
                });
        }

        var documentCount = await dbContext.MarkdownDocuments.CountAsync(cancellationToken);

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "SQLite database is reachable.",
            new Dictionary<string, object?>
            {
                ["canConnect"] = true,
                ["documentCount"] = documentCount
            });
    }
}
