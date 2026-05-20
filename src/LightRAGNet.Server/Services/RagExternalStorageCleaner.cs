using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Server.Services;

public sealed class RagExternalStorageCleaner(
    QdrantClient qdrantClient,
    IDriver neo4JDriver,
    ILogger<RagExternalStorageCleaner> logger) : IRagExternalStorageCleaner
{
    public async Task<IReadOnlyList<string>> ClearAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        await ClearQdrantCollectionsAsync(results, cancellationToken);
        await ClearNeo4jDataAsync(results);

        return results;
    }

    private async Task ClearQdrantCollectionsAsync(
        List<string> results,
        CancellationToken cancellationToken)
    {
        try
        {
            var collections = await qdrantClient.ListCollectionsAsync(cancellationToken);
            var lightragCollections = collections
                .Where(collection => collection.StartsWith("lightrag_vdb_dotnet_", StringComparison.Ordinal))
                .ToList();

            var deletedCollectionCount = 0;
            foreach (var collection in lightragCollections)
            {
                try
                {
                    await qdrantClient.DeleteCollectionAsync(collection, cancellationToken: cancellationToken);
                    deletedCollectionCount++;
                    logger.LogInformation("Deleted Qdrant collection: {Collection}", collection);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to delete Qdrant collection: {Collection}, {Error}",
                        collection,
                        ex.Message);
                }
            }

            if (deletedCollectionCount > 0)
            {
                results.Add($"Deleted {deletedCollectionCount} Qdrant collections");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error occurred while clearing Qdrant collections: {Error}", ex.Message);
        }
    }

    private async Task ClearNeo4jDataAsync(List<string> results)
    {
        try
        {
            await using var session = neo4JDriver.AsyncSession();
            var deleteResult = await session.RunAsync("MATCH (n) DETACH DELETE n RETURN count(n) as deleted");
            var record = await deleteResult.SingleAsync();
            var deletedCount = record["deleted"].As<int>();
            if (deletedCount > 0)
            {
                results.Add($"Deleted {deletedCount} Neo4j nodes");
            }

            logger.LogInformation("Deleted Neo4j node count: {Count}", deletedCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error occurred while clearing Neo4j data: {Error}", ex.Message);
        }
    }
}
