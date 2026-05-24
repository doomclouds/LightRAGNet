using LightRAGNet.Storage;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class QdrantHealthCheck(
    QdrantClient client,
    IOptions<QdrantOptions> options) : ISystemHealthCheck
{
    public string Id => "qdrant";

    public string Name => "Qdrant";

    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collections = await client.ListCollectionsAsync(cancellationToken);
        var value = options.Value;

        return SystemHealthCheckResult.Healthy(
            Id,
            Name,
            Category,
            "Qdrant is reachable.",
            new Dictionary<string, object?>
            {
                ["host"] = value.Host,
                ["port"] = value.Port,
                ["embeddingDimension"] = value.EmbeddingDimension,
                ["collectionCount"] = collections.Count
            });
    }
}
