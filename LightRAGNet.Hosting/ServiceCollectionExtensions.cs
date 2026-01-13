using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using LightRAGNet.Embedding;
using LightRAGNet.LLM;
using LightRAGNet.Rerank;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLightRAG(this IServiceCollection services, IConfiguration configuration)
    {
        #region Register Configuration

        services.Configure<DeepSeekOptions>(configuration.GetSection("LLM"));
        services.Configure<AliyunRerankOptions>(configuration.GetSection("Rerank"));
        services.Configure<AliyunEmbeddingOptions>(configuration.GetSection("Embedding"));
        services.Configure<QdrantOptions>(configuration.GetSection("Qdrant"));
        services.Configure<Neo4JOptions>(configuration.GetSection("Neo4j"));
        services.Configure<LightRAGOptions>(configuration.GetSection("LightRAG"));

        #endregion

        #region Register Vector Store Services

        services.AddSingleton<QdrantClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(options.Host, options.Port);
        });
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        #endregion

        #region Register LLM, Rerank and Embedding Services

        services.AddSingleton<ILLMService, DeepSeekLLMService>();
        
        // Register Tokenizer, try to find tokenizer.json from multiple locations
        services.AddSingleton<ITokenizer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DeepSeekTokenizer>>();
            var currentDir = Directory.GetCurrentDirectory();
            
            // Try multiple possible paths
            var possiblePaths = new[]
            {
                Path.Combine(currentDir, "tokenizer.json"), // Current working directory
                Path.Combine(currentDir, "deepseek_v3_tokenizer", "tokenizer.json"), // deepseek_v3_tokenizer under project root
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tokenizer.json"), // Application directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "deepseek_v3_tokenizer", "tokenizer.json"), // Search upward from bin directory
            };
            
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    logger.LogInformation("Found tokenizer.json: {Path}", path);
                    return new DeepSeekTokenizer(path);
                }
            }
            
            // If all not found, use default path (will throw exception)
            logger.LogWarning("tokenizer.json not found, using default path (current directory)");
            return new DeepSeekTokenizer();
        });
        
        services.AddHttpClient<IEmbeddingService, AliyunEmbeddingService>();
        services.AddHttpClient<IRerankService, AliyunRerankService>();

        #endregion

        #region Register Graph Store Services

        services.AddSingleton<IDriver>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Neo4JOptions>>().Value;
            return GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.User, options.Password));
        });
        services.AddSingleton<IGraphStore, Neo4JGraphStore>();

        #endregion

        #region Register KV Store Services

        foreach (var kvStoreName in KVContracts.GetKVStoreNames())
        {
            services.AddKeyedSingleton<IKVStore>(kvStoreName, (sp, _) =>
            {
                var logger = sp.GetRequiredService<ILogger<JsonKVStore>>();
                var lightragOptions = sp.GetRequiredService<IOptions<LightRAGOptions>>().Value;
                var workingDir = lightragOptions.WorkingDir;
                
                // If relative path, convert to absolute path based on application runtime path
                if (!Path.IsPathRooted(workingDir))
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    workingDir = Path.Combine(baseDir, workingDir);
                }
                
                Directory.CreateDirectory(workingDir);
                return new JsonKVStore(Path.Combine(workingDir, $"{kvStoreName}.json"), logger);
            });
        }

        #endregion

        #region Register Retrieval Services

        services.AddSingleton<DocumentProcessingService>();
        services.AddSingleton<KnowledgeGraphMergeService>();
        services.AddSingleton<RetrievalContextService>();
        services.AddSingleton<LightRAG>();

        #endregion

        #region Register MediatR

        // Register MediatR, scan current assembly and LightRAGNet assembly
        var lightRagAssembly = typeof(LightRAG).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(lightRagAssembly);
        });

        #endregion

        #region Register Task Queue Services

        services.AddSingleton<IRagTaskStateStore, RagTaskStateStore>();
        services.AddSingleton<IRagTaskQueueService, RagTaskQueueService>();
        services.AddHostedService<RagTaskProcessorService>();

        #endregion

        return services;
    }
}