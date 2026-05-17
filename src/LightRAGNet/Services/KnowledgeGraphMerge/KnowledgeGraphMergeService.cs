using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Tasks.Dataflow;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Knowledge graph merge service
/// Reference: Python version operate.py merge_nodes_and_edges function
/// Refactored using design patterns: Builder pattern, Strategy pattern, Stage processor
/// </summary>
public class KnowledgeGraphMergeService
{
    private readonly IGraphStore _graphStore;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKVStore _fullEntitiesStore;
    private readonly IKVStore _fullRelationsStore;
    private readonly IKVStore _entityChunksStore;
    private readonly IKVStore _relationChunksStore;
    private readonly LightRAGOptions _options;
    private readonly ILogger<KnowledgeGraphMergeService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly EntityBuilder _entityBuilder;
    private readonly RelationBuilder _relationBuilder;

    public KnowledgeGraphMergeService(
        IGraphStore graphStore,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILLMService llmService,
        ITokenizer tokenizer,
        [FromKeyedServices(KVContracts.FullEntities)]
        IKVStore fullEntitiesStore,
        [FromKeyedServices(KVContracts.FullRelations)]
        IKVStore fullRelationsStore,
        [FromKeyedServices(KVContracts.EntityChunks)]
        IKVStore entityChunksStore,
        [FromKeyedServices(KVContracts.RelationChunks)]
        IKVStore relationChunksStore,
        IOptions<LightRAGOptions> options,
        ILogger<KnowledgeGraphMergeService> logger,
        ILoggerFactory loggerFactory)
    {
        _graphStore = graphStore;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _fullEntitiesStore = fullEntitiesStore;
        _fullRelationsStore = fullRelationsStore;
        _entityChunksStore = entityChunksStore;
        _relationChunksStore = relationChunksStore;
        _options = options.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;

        // Create internal dependency objects
        var descriptionMerger = new DescriptionMerger(
            llmService,
            tokenizer,
            options,
            _loggerFactory.CreateLogger<DescriptionMerger>());

        var sourceIdsLimiter = new SourceIdsLimiter(
            options,
            _loggerFactory.CreateLogger<SourceIdsLimiter>());

        _entityBuilder = new EntityBuilder(
            _graphStore,
            descriptionMerger,
            sourceIdsLimiter,
            options,
            _loggerFactory.CreateLogger<EntityBuilder>());

        _relationBuilder = new RelationBuilder(
            _graphStore,
            descriptionMerger,
            sourceIdsLimiter,
            options,
            _loggerFactory.CreateLogger<RelationBuilder>());
    }

    /// <summary>
    /// Merge entities and relationships
    /// Reference: operate.py merge_nodes_and_edges function (three-stage merge)
    /// </summary>
    public async Task MergeEntitiesAndRelationsAsync(
        List<ChunkResult> chunkResults,
        string? docId = null,
        ITargetBlock<TaskState>? taskStateTarget = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Collect all entities and relationships
        var (allEntities, allRelations) = 
            CollectEntitiesAndRelations(chunkResults);

        _logger.LogInformation(
            "Merging {EntityCount} entities and {RelationCount} relations",
            allEntities.Count,
            allRelations.Count);

        // Stage 1: Batch process all entities
        var entityMergeStage = new EntityMergeStage(
            _entityBuilder,
            _graphStore,
            _vectorStore,
            _embeddingService,
            _loggerFactory.CreateLogger<EntityMergeStage>(),
            allEntities,
            _entityChunksStore,
            _options,
            docId);
        
        // Link data flow and save link handle for later disconnection
        IDisposable? entityLink = null;
        if (taskStateTarget != null)
        {
            entityLink = entityMergeStage.GetTaskStateSource().LinkTo(taskStateTarget);
        }
        
        var entityDataList = await entityMergeStage.ExecuteAsync(cancellationToken);
        
        // Disconnect link
        entityLink?.Dispose();

        // Stage 2: Batch process all relationships
        var relationMergeStage = new RelationMergeStage(
            _relationBuilder,
            _graphStore,
            _vectorStore,
            _embeddingService,
            _loggerFactory.CreateLogger<RelationMergeStage>(),
            allRelations,
            _relationChunksStore,
            _entityChunksStore,
            _options,
            docId);
        
        // Link data flow and save link handle for later disconnection
        IDisposable? relationLink = null;
        if (taskStateTarget != null)
        {
            relationLink = relationMergeStage.GetTaskStateSource().LinkTo(taskStateTarget);
        }
        
        var (relationDataList, addedEntityNames) = await relationMergeStage.ExecuteAsync(cancellationToken);
        
        // Disconnect link
        relationLink?.Dispose();

        // Stage 3: Update storage (only update full_entities and full_relations)
        // Note: entity_chunks and relation_chunks updates are already completed during merge process
        var storageUpdateStage = new StorageUpdateStage(
            _fullEntitiesStore,
            _fullRelationsStore,
            _loggerFactory.CreateLogger<StorageUpdateStage>(),
            docId,
            entityDataList,
            relationDataList,
            addedEntityNames);
        
        // Link data flow and save link handle for later disconnection
        IDisposable? storageLink = null;
        if (taskStateTarget != null)
        {
            storageLink = storageUpdateStage.GetTaskStateSource().LinkTo(taskStateTarget);
        }
        
        await storageUpdateStage.ExecuteAsync(cancellationToken);
        
        // Disconnect link
        storageLink?.Dispose();
    }

    /// <summary>
    /// Collect all entities and relationships
    /// </summary>
    private static (Dictionary<string, List<Entity>> Entities, Dictionary<(string Source, string Target), List<Relationship>>
        Relations)
        CollectEntitiesAndRelations(List<ChunkResult> chunkResults)
    {
        var allEntities = new Dictionary<string, List<Entity>>();
        var allRelations = new Dictionary<(string Source, string Target), List<Relationship>>();

        foreach (var chunkResult in chunkResults)
        {
            // Collect entities
            foreach (var entity in chunkResult.Entities)
            {
                if (!allEntities.TryGetValue(entity.Name, out var value))
                {
                    value = [];
                    allEntities[entity.Name] = value;
                }

                value.Add(entity);
            }

            // Collect relationships (use sorted keys to ensure undirected graph)
            foreach (var relation in chunkResult.Relationships)
            {
                var key = string.Compare(relation.SourceId, relation.TargetId, StringComparison.Ordinal) < 0
                    ? (relation.SourceId, relation.TargetId)
                    : (relation.TargetId, relation.SourceId);

                if (!allRelations.TryGetValue(key, out var value))
                {
                    value = [];
                    allRelations[key] = value;
                }

                value.Add(relation);
            }
        }

        return (allEntities, allRelations);
    }
}