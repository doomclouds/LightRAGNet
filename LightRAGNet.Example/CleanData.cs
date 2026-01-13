using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Example;

/// <summary>
/// Utility class for cleaning all LightRAG data
/// </summary>
public static class CleanData
{
    public static async Task CleanAllAsync(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var qdrantClient = serviceProvider.GetRequiredService<QdrantClient>();
        var neo4JDriver = serviceProvider.GetRequiredService<IDriver>();
        var lightragOptions = serviceProvider.GetRequiredService<IOptions<LightRAGOptions>>().Value;

        Console.WriteLine("=== Cleaning LightRAG Data ===");
        Console.WriteLine();
        
        // 1. Clean KV stores in memory (clean memory first, then delete files)
        Console.WriteLine("1. Cleaning KV stores in memory...");
        try
        {
            var kvStoreNames = KVContracts.GetKVStoreNames().ToList();
            foreach (var storeName in kvStoreNames)
            {
                try
                {
                    var kvStore = serviceProvider.GetRequiredKeyedService<IKVStore>(storeName);
                    await kvStore.DropAsync();
                    logger.LogInformation("Cleared KV store in memory: {StoreName}", storeName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clear KV store in memory: {StoreName}", storeName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning KV stores in memory");
        }

        // 2. Clean local JSON files (DropAsync has already saved empty state, delete files here to ensure complete cleanup)
        Console.WriteLine("2. Cleaning local JSON files...");
        var workingDir = lightragOptions.WorkingDir;
        // If relative path, convert to absolute path based on application runtime path
        if (!Path.IsPathRooted(workingDir))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            workingDir = Path.Combine(baseDir, workingDir);
        }
        var jsonFiles = new[]
        {
            Path.Combine(workingDir, "text_chunks.json"),
            Path.Combine(workingDir, "full_docs.json"),
            Path.Combine(workingDir, "full_entities.json"),
            Path.Combine(workingDir, "full_relations.json"),
            Path.Combine(workingDir, "entity_chunks.json"),
            Path.Combine(workingDir, "relation_chunks.json"),
            Path.Combine(workingDir, "llm_cache.json")
        };

        foreach (var file in jsonFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
                logger.LogInformation("Deleted: {File}", file);
            }
        }

        // 3. Clean Qdrant collections
        Console.WriteLine("3. Cleaning Qdrant collections...");
        try
        {
            var collections = await qdrantClient.ListCollectionsAsync();
            var lightragCollections = collections.Where(c => c.StartsWith("lightrag_vdb_dotnet_")).ToList();
            
            foreach (var collection in lightragCollections)
            {
                try
                {
                    await qdrantClient.DeleteCollectionAsync(collection);
                    logger.LogInformation("Deleted Qdrant collection: {Collection}", collection);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete Qdrant collection: {Collection}", collection);
                }
            }

            if (lightragCollections.Count == 0)
            {
                logger.LogInformation("No Qdrant collections found to clean");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning Qdrant collections");
        }

        // 4. Clean Neo4j data
        Console.WriteLine("4. Cleaning Neo4j data...");
        try
        {
            await using var session = neo4JDriver.AsyncSession();

            // Delete all nodes and relationships
            var deleteResult = await session.RunAsync("MATCH (n) DETACH DELETE n RETURN count(n) as deleted");
            var record = await deleteResult.SingleAsync();
            var deletedCount = record["deleted"].As<int>();
            logger.LogInformation("Deleted Neo4j nodes count: {Count}", deletedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning Neo4j data");
        }

        // 5. Clean RAG tasks
        Console.WriteLine("5. Cleaning RAG tasks...");
        try
        {
            var taskQueueService = serviceProvider.GetService<IRagTaskQueueService>();
            if (taskQueueService != null)
            {
                await taskQueueService.ClearAllTasksAsync();
                logger.LogInformation("Cleared all RAG tasks");
            }
            else
            {
                // If service is not available, try to delete tasks.json file directly
                var tasksFilePath = Path.Combine(workingDir, "tasks.json");
                if (File.Exists(tasksFilePath))
                {
                    File.Delete(tasksFilePath);
                    logger.LogInformation("Deleted tasks.json file: {FilePath}", tasksFilePath);
                }
                else
                {
                    logger.LogInformation("No tasks.json file found to clean");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error cleaning RAG tasks (this is optional if task queue service is not available)");
        }

        // 6. Clean markdown-related files (tasks.json if not already deleted)
        Console.WriteLine("6. Cleaning markdown-related files...");
        try
        {
            var tasksFilePath = Path.Combine(workingDir, "tasks.json");
            if (File.Exists(tasksFilePath))
            {
                File.Delete(tasksFilePath);
                logger.LogInformation("Deleted tasks.json file: {FilePath}", tasksFilePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error cleaning markdown-related files");
        }

        Console.WriteLine();
        Console.WriteLine("=== Cleaning Complete ===");
    }
}

