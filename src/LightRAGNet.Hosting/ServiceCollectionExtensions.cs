using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using LightRAGNet.Embedding;
using LightRAGNet.LLM;
using LightRAGNet.Rerank;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Services.GraphCuration;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
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
        services.Configure<RerankChunkingOptions>(configuration.GetSection("Rerank"));
        services.Configure<AliyunEmbeddingOptions>(configuration.GetSection("Embedding"));
        services.Configure<QdrantOptions>(configuration.GetSection("Qdrant"));
        services.Configure<Neo4JOptions>(configuration.GetSection("Neo4j"));
        services.Configure<LightRAGOptions>(configuration.GetSection("LightRAG"));
        services.Configure<CacheMetricsOptions>(configuration.GetSection("CacheMetrics"));

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
        
        // Register Embedding service with HttpClient configuration
        services.AddHttpClient<IEmbeddingService, AliyunEmbeddingService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AliyunEmbeddingOptions>>().Value;
            var apiKey = options.ApiKey;
            
            // Get API key from options or environment variable
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ??
                         throw new ArgumentException("Configure the API key[Embedding:ApiKey] in the appsettings.json file " +
                                                     "or set the DASHSCOPE_API_KEY environment variable.");
            }
            
            // Set authentication header
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        });
        
        // Register Rerank service with HttpClient configuration
        services.AddHttpClient<IRerankService, AliyunRerankService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AliyunRerankOptions>>().Value;
            var apiKey = options.ApiKey;
            
            // Get API key from options or environment variable
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ??
                         throw new ArgumentException("Configure the API key[Rerank:ApiKey] in the appsettings.json file " +
                                                     "or set the DASHSCOPE_API_KEY environment variable.");
            }
            
            // Set authentication header
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        });

        #endregion

        #region Register Graph Store Services

        services.AddSingleton<IDriver>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Neo4JOptions>>().Value;
            return GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.User, options.GetEffectivePassword()));
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

        services.AddSingleton<IDocumentStatusStore, KvDocumentStatusStore>();
        services.AddSingleton<DocumentLifecycleService>();

        #endregion

        #region Register Retrieval Services

        services.AddSingleton<FixedTokenChunkingStrategy>();
        services.AddSingleton<RecursiveCharacterChunkingStrategy>();
        services.AddSingleton<SemanticVectorChunkingStrategy>();
        services.AddSingleton<ParagraphSemanticChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<FixedTokenChunkingStrategy>());
        services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<RecursiveCharacterChunkingStrategy>());
        services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<SemanticVectorChunkingStrategy>());
        services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<ParagraphSemanticChunkingStrategy>());
        services.AddSingleton<LightRagChunkingService>();
        services.AddSingleton<DocumentProcessingService>();
        services.AddSingleton<DocumentDeletionService>();
        services.AddSingleton(sp => new GraphCurationService(
            sp.GetRequiredService<IGraphStore>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.TextChunks),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.FullEntities),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.FullRelations),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.EntityChunks),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.RelationChunks),
            () => sp.GetRequiredService<LightRagLlmCacheService>()
                .BumpWorkspaceQueryRevisionAsync(
                    sp.GetRequiredService<IOptions<LightRAGOptions>>().Value.Workspace,
                    CancellationToken.None),
            sp.GetRequiredService<ILogger<GraphCurationService>>()));
        services.AddSingleton<KnowledgeGraphMergeService>();
        services.AddSingleton<RerankDocumentChunker>();
        services.AddSingleton<RerankCoordinator>();
        services.AddSingleton(sp => new RetrievalContextService(
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<IGraphStore>(),
            sp.GetRequiredService<RerankCoordinator>(),
            sp.GetRequiredService<ITokenizer>(),
            sp.GetRequiredKeyedService<IKVStore>(KVContracts.TextChunks),
            sp.GetRequiredService<IOptions<LightRAGOptions>>(),
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new NaiveQueryService(
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<RerankCoordinator>(),
            sp.GetRequiredService<ITokenizer>()));
        services.AddSingleton<LightRagCacheKeyBuilder>();
        services.AddSingleton<ICacheMetricsStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonCacheMetricsStore>>();
            var lightragOptions = sp.GetRequiredService<IOptions<LightRAGOptions>>().Value;
            var metricsOptions = sp.GetRequiredService<IOptions<CacheMetricsOptions>>().Value;
            var workingDir = lightragOptions.WorkingDir;

            if (!Path.IsPathRooted(workingDir))
            {
                workingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workingDir);
            }

            Directory.CreateDirectory(workingDir);
            return new JsonCacheMetricsStore(
                Path.Combine(workingDir, "cache_metrics.json"),
                metricsOptions,
                logger);
        });
        services.AddSingleton<ICacheMetricsRecorder, CacheMetricsRecorder>();
        services.AddSingleton<LightRagLlmCacheService>();
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
        services.AddSingleton<IRagTaskCancellationRegistry, RagTaskCancellationRegistry>();
        services.AddSingleton<IRagTaskQueueService, RagTaskQueueService>();
        services.AddHostedService<RagTaskProcessorService>();

        #endregion

        return services;
    }
}
