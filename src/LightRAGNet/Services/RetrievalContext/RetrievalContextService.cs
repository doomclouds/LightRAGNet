using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Query context construction
/// Build query context through retrieval, reference Python version operate.py kg_query and _build_query_context functions
/// </summary>
public class RetrievalContextService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    IGraphStore graphStore,
    IRerankService rerankService,
    ITokenizer tokenizer,
    [FromKeyedServices(KVContracts.TextChunks)] IKVStore textChunksStore,
    IOptions<LightRAGOptions> options,
    ILoggerFactory loggerFactory)
{
    private const string GraphFieldSep = "<SEP>";
    private const string DefaultKgChunkPickMethod = "VECTOR";
    
    private readonly ILogger<RetrievalContextService> _logger = loggerFactory.CreateLogger<RetrievalContextService>();
    private readonly LightRAGOptions _options = options.Value;
    /// <summary>
    /// Build query context
    /// Reference: operate.py _build_query_context function
    /// </summary>
    public async Task<QueryContextResult?> BuildQueryContextAsync(
        string query,
        KeywordsResult keywords,
        QueryParam queryParam,
        CancellationToken cancellationToken = default)
    {
        var llKeywordsStr = string.Join(", ", keywords.LowLevelKeywords);
        var hlKeywordsStr = string.Join(", ", keywords.HighLevelKeywords);
        
        // Perform knowledge graph search
        var searchResult = await PerformKGSearchAsync(
            query,
            llKeywordsStr,
            hlKeywordsStr,
            queryParam,
            cancellationToken);
        
        if (searchResult == null)
            return null;
        
        // Check empty results (reference Python version _build_query_context)
        // Python version: if not search_result["final_entities"] and not search_result["final_relations"]: ...
        if (searchResult.Entities.Count == 0 && searchResult.Relations.Count == 0)
        {
            if (queryParam.Mode != QueryMode.Mix)
            {
                return null;
            }

            // In Mix mode, return null if no chunks
            if (searchResult.Chunks.Count == 0)
            {
                return null;
            }
        }
        
        // Build context string
        var context = BuildContextString(searchResult, queryParam);
        
        // Build raw data
        var rawData = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["references"] = searchResult.References.Select((r, i) => new Dictionary<string, object>
                {
                    ["reference_id"] = (i + 1).ToString(),
                    ["file_path"] = r.FilePath
                }).ToList()
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["query_mode"] = queryParam.Mode.ToString(),
                ["high_level_keywords"] = keywords.HighLevelKeywords,
                ["low_level_keywords"] = keywords.LowLevelKeywords
            }
        };
        
        return new QueryContextResult
        {
            Context = context,
            RawData = rawData
        };
    }
    
    /// <summary>
    /// Perform knowledge graph search
    /// Reference: operate.py _perform_kg_search function
    /// Use strategy pattern to select different search strategies based on query mode
    /// </summary>
    private async Task<KGSearchResult?> PerformKGSearchAsync(
        string query,
        string llKeywords,
        string hlKeywords,
        QueryParam queryParam,
        CancellationToken cancellationToken)
    {
        // Pre-compute query vector (for document chunk retrieval)
        // Reference Python version: decide whether to pre-compute based on kg_chunk_pick_method
        // Python version: if query and (kg_chunk_pick_method == "VECTOR" or chunks_vdb): query_embedding = ...
        var kgChunkPickMethod = _options.KgChunkPickMethod ?? DefaultKgChunkPickMethod;
        float[]? queryEmbedding = null;
        if (!string.IsNullOrEmpty(query) && (kgChunkPickMethod == "VECTOR" || queryParam.Mode == QueryMode.Mix))
        {
            try
            {
                queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
                _logger.LogDebug("Pre-computed query embedding for all vector operations");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to pre-compute query embedding");
                queryEmbedding = null;
            }
        }
        
        // Generate embedding vector for LowLevelKeywords (for entity retrieval)
        // Reference Python version: entities_vdb.query(ll_keywords, ...) will use ll_keywords to generate embedding vector
        float[]? llKeywordsEmbedding = null;
        if (!string.IsNullOrEmpty(llKeywords))
        {
            llKeywordsEmbedding = await embeddingService.GenerateEmbeddingAsync(llKeywords, cancellationToken);
        }
        
        // Generate embedding vector for HighLevelKeywords (for relationship retrieval)
        // Reference Python version: relationships_vdb.query(hl_keywords, ...) will use hl_keywords to generate embedding vector
        float[]? hlKeywordsEmbedding = null;
        if (!string.IsNullOrEmpty(hlKeywords))
        {
            hlKeywordsEmbedding = await embeddingService.GenerateEmbeddingAsync(hlKeywords, cancellationToken);
        }
        
        // Create strategy factory and get corresponding strategy
        var strategyFactory = new KGSearchStrategyFactory(vectorStore, graphStore, loggerFactory);
        var strategy = strategyFactory.GetStrategy(queryParam.Mode);
        
        // Create search context
        var searchContext = new KGSearchContext
        {
            Query = query,
            LowLevelKeywords = llKeywords,
            HighLevelKeywords = hlKeywords,
            QueryEmbedding = queryEmbedding,
            LowLevelKeywordsEmbedding = llKeywordsEmbedding,
            HighLevelKeywordsEmbedding = hlKeywordsEmbedding,
            QueryParam = queryParam
        };
        
        // Execute strategy search
        var searchResult = await strategy.SearchAsync(searchContext, cancellationToken);
        
        // Vector retrieve document chunks (only needed in mix mode, consistent with Python version)
        // Reference Python version: if query_param.mode == "mix" and chunks_vdb: vector_chunks = await _get_vector_context(...)
        List<ChunkData> vectorChunks = [];
        var chunkTracking = new Dictionary<string, ChunkTrackingInfo>();
        if (queryParam.Mode == QueryMode.Mix)
        {
            vectorChunks = await RetrieveChunksAsync(query, queryEmbedding, queryParam, cancellationToken);
            
            // Track vector chunks with source metadata (consistent with Python version)
            // Python version: chunk_tracking[chunk_id] = {"source": "C", "frequency": 1, "order": i + 1}
            for (var i = 0; i < vectorChunks.Count; i++)
            {
                var chunkId = vectorChunks[i].ChunkId;
                if (!string.IsNullOrEmpty(chunkId))
                {
                    chunkTracking[chunkId] = new ChunkTrackingInfo
                    {
                        Source = "C",
                        Frequency = 1,
                        Order = i + 1
                    };
                }
            }
        }
        
        // Extract related document chunks from entities and relationships (consistent with Python version _merge_all_chunks)
        var entityChunks = await FindRelatedTextUnitFromEntitiesAsync(
            searchResult.Entities,
            query,
            queryEmbedding,
            chunkTracking,
            cancellationToken);
        
        var relationChunks = await FindRelatedTextUnitFromRelationsAsync(
            searchResult.Relations,
            entityChunks,
            query,
            queryEmbedding,
            chunkTracking,
            cancellationToken);
        
        // Round-robin merge chunks (consistent with Python version _merge_all_chunks)
        var mergedChunks = MergeAllChunksRoundRobin(vectorChunks, entityChunks, relationChunks);
        
        // Apply token limit (apply uniformly after merging all chunks)
        // Reference Python version: after _merge_all_chunks, chunks will apply token limit in _build_context_str
        // But for consistency, we apply the limit here
        var maxChunkTokens = queryParam.MaxTotalTokens - queryParam.MaxEntityTokens - queryParam.MaxRelationTokens;
        var finalChunks = ApplyTokenLimit(mergedChunks, maxChunkTokens);
        
        // Log information consistent with Python version
        _logger.LogInformation(
            "Raw search results: {EntityCount} entities, {RelationCount} relations, {VectorChunkCount} vector chunks",
            searchResult.Entities.Count,
            searchResult.Relations.Count,
            vectorChunks.Count);
        
        // Generate reference list (reference Python version generate_reference_list_from_chunks function)
        var (references, chunksWithRefIds) = GenerateReferenceListFromChunks(finalChunks);
        
        // Merge results
        searchResult.Chunks = chunksWithRefIds;
        searchResult.References = references;
                    
        return searchResult;
    }
    
    /// <summary>
    /// Retrieve document chunks (vector retrieval + Rerank)
    /// </summary>
    private async Task<List<ChunkData>> RetrieveChunksAsync(
        string query,
        float[]? queryEmbedding,
        QueryParam queryParam,
        CancellationToken cancellationToken)
    {
        // Vector retrieve document chunks
        var chunkResults = await vectorStore.QueryAsync(
            "chunks",
            query,
            queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK,
            queryEmbedding,
            cancellationToken: cancellationToken);
        
        var vectorChunks = chunkResults.Select(r => new ChunkData
        {
            Content = r.Content,
            FilePath = r.Metadata.GetValueOrDefault("file_path")?.ToString() ?? "",
            ChunkId = r.Id
        }).ToList();
        
        // Rerank (if enabled)
        if (queryParam.EnableRerank && vectorChunks.Count > 0)
        {
            var documents = vectorChunks.Select(c => c.Content).ToList();
            var rerankResults = await rerankService.RerankAsync(
                query,
                documents,
                queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK,
                cancellationToken);
            
            var rerankedChunks = rerankResults
                .OrderByDescending(r => r.RelevanceScore)
                .Select(r => vectorChunks[r.Index])
                .ToList();
            
            vectorChunks = rerankedChunks;
        }
        
        return vectorChunks;
    }
    
    /// <summary>
    /// Extract related document chunks from entities
    /// Reference: Python version _find_related_text_unit_from_entities function
    /// </summary>
    private async Task<List<ChunkData>> FindRelatedTextUnitFromEntitiesAsync(
        List<EntityData> entities,
        string? query,
        float[]? queryEmbedding,
        Dictionary<string, ChunkTrackingInfo> chunkTracking,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
            return [];
        
        _logger.LogDebug("Finding text chunks from {EntityCount} entities", entities.Count);
        
        // Step 1: Collect chunk IDs from all entities
        var entitiesWithChunks = new List<(string EntityName, List<string> ChunkIds, EntityData EntityData)>();
        foreach (var entity in entities)
        {
            if (string.IsNullOrEmpty(entity.SourceId))
                continue;
            
            var chunkIds = entity.SourceId
                .Split([GraphFieldSep], StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            
            if (chunkIds.Count > 0)
            {
                entitiesWithChunks.Add((entity.Name, chunkIds, entity));
            }
        }
        
        if (entitiesWithChunks.Count == 0)
        {
            _logger.LogWarning("No entities with text chunks found");
            return [];
        }
        
        // Step 2: Count chunk occurrences and deduplicate (keep chunks at earlier positions)
        var chunkOccurrenceCount = new Dictionary<string, int>();
        foreach (var (_, chunkIds, _) in entitiesWithChunks)
        {
            foreach (var chunkId in chunkIds)
            {
                chunkOccurrenceCount[chunkId] = chunkOccurrenceCount.GetValueOrDefault(chunkId, 0) + 1;
            }
        }
        
        // Deduplicate chunks for each entity (keep first occurrence)
        var deduplicatedEntities = entitiesWithChunks.Select(e =>
        {
            var deduplicated = new List<string>();
            foreach (var chunkId in e.ChunkIds)
            {
                if (chunkOccurrenceCount[chunkId] == 1 || !deduplicated.Contains(chunkId))
                {
                    deduplicated.Add(chunkId);
                }
            }
            return (e.EntityName, DeduplicatedChunks: deduplicated, e.EntityData);
        }).ToList();
        
        // Step 3: Sort chunks by occurrence count (higher count first)
        var sortedEntities = deduplicatedEntities.Select(e =>
        {
            var sorted = e.DeduplicatedChunks
                .OrderByDescending(chunkId => chunkOccurrenceCount.GetValueOrDefault(chunkId, 0))
                .ToList();
            return (e.EntityName, SortedChunks: sorted, e.EntityData);
        }).ToList();
        
        var totalEntityChunks = sortedEntities.Sum(e => e.SortedChunks.Count);
        
        // Step 4: Apply chunk selection algorithm
        var kgChunkPickMethod = _options.KgChunkPickMethod ?? DefaultKgChunkPickMethod;
        var maxRelatedChunks = _options.RelatedChunkNumber;
        var selectedChunkIds = new List<string>();
        
        if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
        {
            // VECTOR mode: use vector similarity selection (simplified implementation, use WEIGHT as fallback)
            _logger.LogWarning("VECTOR chunk pick method not fully implemented, falling back to WEIGHT");
            kgChunkPickMethod = "WEIGHT";
        }
        
        if (kgChunkPickMethod == "WEIGHT")
        {
            // WEIGHT mode: use weighted polling selection
            var itemsForPolling = sortedEntities.Select(e => (e.EntityName, e.SortedChunks)).ToList();
            selectedChunkIds = PickByWeightedPolling(itemsForPolling, maxRelatedChunks);
            _logger.LogInformation(
                "Selecting {SelectedCount} from {TotalCount} entity-related chunks by weighted polling",
                selectedChunkIds.Count,
                totalEntityChunks);
        }
        
        if (selectedChunkIds.Count == 0)
            return [];
        
        // Step 5: Batch get chunk data
        var uniqueChunkIds = selectedChunkIds.Distinct().ToList();
        var chunkDataList = await textChunksStore.GetByIdsAsync(uniqueChunkIds, cancellationToken);
        
        // Step 6: Build result chunks and update chunk tracking
        var resultChunks = new List<ChunkData>();
        for (int i = 0; i < uniqueChunkIds.Count; i++)
        {
            var chunkId = uniqueChunkIds[i];
            var chunkData = i < chunkDataList.Count ? chunkDataList[i] : null;
            
            if (chunkData != null && chunkData.TryGetValue("content", out var contentObj))
            {
                var content = contentObj.ToString() ?? "";
                var filePath = chunkData.GetValueOrDefault("file_path")?.ToString() ?? "";
                
                resultChunks.Add(new ChunkData
                {
                    ChunkId = chunkId,
                    Content = content,
                    FilePath = filePath
                });
                
                // Update chunk tracking
                chunkTracking[chunkId] = new ChunkTrackingInfo
                {
                    Source = "E",
                    Frequency = chunkOccurrenceCount.GetValueOrDefault(chunkId, 1),
                    Order = i + 1
                };
            }
        }
        
        return resultChunks;
    }
    
    /// <summary>
    /// Extract related document chunks from relationships
    /// Reference: Python version _find_related_text_unit_from_relations function
    /// </summary>
    private async Task<List<ChunkData>> FindRelatedTextUnitFromRelationsAsync(
        List<RelationData> relations,
        List<ChunkData> entityChunks,
        string? query,
        float[]? queryEmbedding,
        Dictionary<string, ChunkTrackingInfo> chunkTracking,
        CancellationToken cancellationToken)
    {
        if (relations.Count == 0)
            return [];
        
        _logger.LogDebug("Finding text chunks from {RelationCount} relations", relations.Count);
        
        // Step 1: Collect chunk IDs from all relationships
        // Note: use RSourceId (chunk IDs), not SourceId (entity ID)
        var relationsWithChunks = new List<(string RelationKey, List<string> ChunkIds, RelationData RelationData)>();
        foreach (var relation in relations)
        {
            // Use RSourceId to get chunk IDs
            if (string.IsNullOrEmpty(relation.RSourceId))
                continue;
            
            var chunkIds = relation.RSourceId
                .Split([GraphFieldSep], StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            
            if (chunkIds.Count > 0)
            {
                // Build relation identifier (use SourceId and TargetId, which are entity IDs)
                var relKey = string.Compare(relation.SourceId, relation.TargetId, StringComparison.Ordinal) < 0
                    ? $"{relation.SourceId}-{relation.TargetId}"
                    : $"{relation.TargetId}-{relation.SourceId}";
                
                relationsWithChunks.Add((relKey, chunkIds, relation));
            }
        }
        
        if (relationsWithChunks.Count == 0)
        {
            _logger.LogWarning("No relation-related chunks found");
            return [];
        }
        
        // Step 2: Extract chunk IDs from entity chunks for deduplication
        var entityChunkIds = entityChunks
            .Where(c => !string.IsNullOrEmpty(c.ChunkId))
            .Select(c => c.ChunkId)
            .ToHashSet();
        
        // Step 3: Count chunk occurrences and deduplicate (exclude entity chunks)
        var chunkOccurrenceCount = new Dictionary<string, int>();
        var removedEntityChunkIds = new HashSet<string>();
        
        var deduplicatedRelations = relationsWithChunks.Select(r =>
        {
            var deduplicated = new List<string>();
            foreach (var chunkId in r.ChunkIds)
            {
                // Skip chunks already in entity chunks
                if (entityChunkIds.Contains(chunkId))
                {
                    removedEntityChunkIds.Add(chunkId);
                    continue;
                }
                
                chunkOccurrenceCount[chunkId] = chunkOccurrenceCount.GetValueOrDefault(chunkId, 0) + 1;
                
                // Keep first occurrence of chunk
                if (chunkOccurrenceCount[chunkId] == 1)
                {
                    deduplicated.Add(chunkId);
                }
            }
            return (r.RelationKey, DeduplicatedChunks: deduplicated, r.RelationData);
        }).Where(r => r.DeduplicatedChunks.Count > 0).ToList();
        
        if (deduplicatedRelations.Count == 0)
        {
            _logger.LogInformation(
                "Find no additional relations-related chunks from {RelationCount} relations",
                relations.Count);
            return [];
        }
        
        // Step 4: Sort chunks by occurrence count
        var sortedRelations = deduplicatedRelations.Select(r =>
        {
            var sorted = r.DeduplicatedChunks
                .OrderByDescending(chunkId => chunkOccurrenceCount.GetValueOrDefault(chunkId, 0))
                .ToList();
            return (r.RelationKey, SortedChunks: sorted, r.RelationData);
        }).ToList();
        
        var totalRelationChunks = sortedRelations.Sum(r => r.SortedChunks.Count);
        _logger.LogInformation(
            "Find {TotalCount} additional chunks in {RelationCount} relations (deduplicated {RemovedCount})",
            totalRelationChunks,
            sortedRelations.Count,
            removedEntityChunkIds.Count);
        
        // Step 5: Apply chunk selection algorithm
        var kgChunkPickMethod = _options.KgChunkPickMethod ?? DefaultKgChunkPickMethod;
        var maxRelatedChunks = _options.RelatedChunkNumber;
        var selectedChunkIds = new List<string>();
        
        if (kgChunkPickMethod == "VECTOR" && !string.IsNullOrEmpty(query) && queryEmbedding != null)
        {
            // VECTOR mode: use vector similarity selection (simplified implementation, use WEIGHT as fallback)
            _logger.LogWarning("VECTOR chunk pick method not fully implemented, falling back to WEIGHT");
            kgChunkPickMethod = "WEIGHT";
        }
        
        if (kgChunkPickMethod == "WEIGHT")
        {
            // WEIGHT mode: use weighted polling selection
            var itemsForPolling = sortedRelations.Select(r => (r.RelationKey, r.SortedChunks)).ToList();
            selectedChunkIds = PickByWeightedPolling(itemsForPolling, maxRelatedChunks);
            _logger.LogInformation(
                "Selecting {SelectedCount} from {TotalCount} relation-related chunks by weighted polling",
                selectedChunkIds.Count,
                totalRelationChunks);
        }
        
        if (selectedChunkIds.Count == 0)
            return [];
        
        // Step 6: Batch get chunk data
        var uniqueChunkIds = selectedChunkIds.Distinct().ToList();
        var chunkDataList = await textChunksStore.GetByIdsAsync(uniqueChunkIds, cancellationToken);
        
        // Step 7: Build result chunks and update chunk tracking
        var resultChunks = new List<ChunkData>();
        for (int i = 0; i < uniqueChunkIds.Count; i++)
        {
            var chunkId = uniqueChunkIds[i];
            var chunkData = i < chunkDataList.Count ? chunkDataList[i] : null;
            
            if (chunkData != null && chunkData.TryGetValue("content", out var contentObj))
            {
                var content = contentObj.ToString() ?? "";
                var filePath = chunkData.GetValueOrDefault("file_path")?.ToString() ?? "";
                
                resultChunks.Add(new ChunkData
                {
                    ChunkId = chunkId,
                    Content = content,
                    FilePath = filePath
                });
                
                // Update chunk tracking
                chunkTracking[chunkId] = new ChunkTrackingInfo
                {
                    Source = "R",
                    Frequency = chunkOccurrenceCount.GetValueOrDefault(chunkId, 1),
                    Order = i + 1
                };
            }
        }
        
        return resultChunks;
    }
    
    /// <summary>
    /// Weighted polling selection of chunks
    /// Reference: Python version pick_by_weighted_polling function
    /// Linear gradient weighted polling algorithm, ensuring entities/relationships with higher importance get more text chunks
    /// </summary>
    private List<string> PickByWeightedPolling(
        List<(string Key, List<string> SortedChunks)> itemsWithChunks,
        int maxRelatedChunks,
        int minRelatedChunks = 1)
    {
        if (itemsWithChunks.Count == 0)
            return [];
        
        var n = itemsWithChunks.Count;
        
        // Case with only one entity/relationship
        if (n == 1)
        {
            var sortedChunks = itemsWithChunks[0].SortedChunks;
            return sortedChunks.Take(maxRelatedChunks).ToList();
        }
        
        // Calculate expected chunk count for each position (linear decrease)
        var expectedCounts = new List<int>();
        for (int i = 0; i < n; i++)
        {
            // Linear interpolation: from max_related_chunks to min_related_chunks
            var ratio = n > 1 ? (double)i / (n - 1) : 0;
            var expected = maxRelatedChunks - ratio * (maxRelatedChunks - minRelatedChunks);
            expectedCounts.Add((int)Math.Round(expected));
        }
        
        // First round allocation: allocate by expected value
        var selectedChunks = new List<string>();
        var usedCounts = new List<int>(); // Track chunk count used by each entity
        var totalRemaining = 0; // Accumulate remaining quota
        
        for (int i = 0; i < itemsWithChunks.Count; i++)
        {
            var sortedChunks = itemsWithChunks[i].SortedChunks;
            var expected = expectedCounts[i];
            
            // Actual allocatable count
            var actual = Math.Min(expected, sortedChunks.Count);
            selectedChunks.AddRange(sortedChunks.Take(actual));
            usedCounts.Add(actual);
            
            // Accumulate remaining quota
            var remaining = expected - actual;
            if (remaining > 0)
            {
                totalRemaining += remaining;
            }
        }
        
        // Second round allocation: multi-round scan to allocate remaining quota
        for (int round = 0; round < totalRemaining; round++)
        {
            var allocated = false;
            
            // Scan entities one by one, allocate one when finding unused chunk
            for (int i = 0; i < itemsWithChunks.Count; i++)
            {
                var sortedChunks = itemsWithChunks[i].SortedChunks;
                
                // Check if there are still unused chunks
                if (usedCounts[i] < sortedChunks.Count)
                {
                    // Allocate one chunk
                    selectedChunks.Add(sortedChunks[usedCounts[i]]);
                    usedCounts[i]++;
                    allocated = true;
                    break;
                }
            }
            
            // If no chunk was allocated this round, all entities are exhausted
            if (!allocated)
            {
                break;
            }
        }
        
        return selectedChunks;
    }
    
    /// <summary>
    /// Generate reference list from chunks
    /// Reference: Python version generate_reference_list_from_chunks function
    /// Sort by frequency descending, first occurrence position ascending, assign consecutive reference_id to each unique file_path
    /// </summary>
    private (List<ReferenceItem> References, List<ChunkData> ChunksWithRefIds) GenerateReferenceListFromChunks(
        List<ChunkData> chunks)
    {
        if (chunks.Count == 0)
            return ([], []);
        
        // Step 1: Count occurrences of each file_path
        var filePathCounts = new Dictionary<string, int>();
        foreach (var chunk in chunks)
        {
            var filePath = chunk.FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath != "unknown_source")
            {
                filePathCounts[filePath] = filePathCounts.GetValueOrDefault(filePath, 0) + 1;
            }
        }
        
        // Step 2: Sort by frequency descending, first occurrence position ascending
        var filePathWithIndices = new List<(string FilePath, int Count, int FirstIndex)>();
        var seenPaths = new HashSet<string>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var filePath = chunks[i].FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath != "unknown_source" && seenPaths.Add(filePath))
            {
                filePathWithIndices.Add((filePath, filePathCounts[filePath], i));
            }
        }
        
        // Sort by frequency descending, first occurrence position ascending
        var sortedFilePaths = filePathWithIndices
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.FirstIndex)
            .Select(x => x.FilePath)
            .ToList();
        
        // Step 3: Create mapping from file_path to reference_id
        var filePathToRefId = new Dictionary<string, string>();
        for (int i = 0; i < sortedFilePaths.Count; i++)
        {
            filePathToRefId[sortedFilePaths[i]] = (i + 1).ToString();
        }
        
        // Step 4: Add reference_id to each chunk
        var chunksWithRefIds = chunks.Select(chunk =>
        {
            var filePath = chunk.FilePath;
            var referenceId = !string.IsNullOrEmpty(filePath) && filePath != "unknown_source" && filePathToRefId.TryGetValue(filePath, out var refId)
                ? refId
                : string.Empty;
            
            return new ChunkData
            {
                ChunkId = chunk.ChunkId,
                Content = chunk.Content,
                FilePath = chunk.FilePath,
                ReferenceId = referenceId
            };
        }).ToList();
        
        // Step 5: Build reference_list
        var references = sortedFilePaths
            .Select((filePath, i) => new ReferenceItem
            {
                ReferenceId = (i + 1).ToString(),
                FilePath = filePath
            })
            .ToList();
        
        return (references, chunksWithRefIds);
    }
    
    /// <summary>
    /// Round-robin merge all chunks
    /// Reference: Python version _merge_all_chunks function
    /// </summary>
    private List<ChunkData> MergeAllChunksRoundRobin(
        List<ChunkData> vectorChunks,
        List<ChunkData> entityChunks,
        List<ChunkData> relationChunks)
    {
        var mergedChunks = new List<ChunkData>();
        var seenChunkIds = new HashSet<string>();
        var maxLen = Math.Max(Math.Max(vectorChunks.Count, entityChunks.Count), relationChunks.Count);
        var originLen = vectorChunks.Count + entityChunks.Count + relationChunks.Count;
        
        for (int i = 0; i < maxLen; i++)
        {
            // First from vector chunks (Naive mode)
            if (i < vectorChunks.Count)
            {
                var chunk = vectorChunks[i];
                if (!string.IsNullOrEmpty(chunk.ChunkId) && seenChunkIds.Add(chunk.ChunkId))
                {
                    mergedChunks.Add(chunk);
                }
            }
            
            // Then from entity chunks (Local mode)
            if (i < entityChunks.Count)
            {
                var chunk = entityChunks[i];
                if (!string.IsNullOrEmpty(chunk.ChunkId) && seenChunkIds.Add(chunk.ChunkId))
                {
                    mergedChunks.Add(chunk);
                }
            }
            
            // Finally from relation chunks (Global mode)
            if (i < relationChunks.Count)
            {
                var chunk = relationChunks[i];
                if (!string.IsNullOrEmpty(chunk.ChunkId) && seenChunkIds.Add(chunk.ChunkId))
                {
                    mergedChunks.Add(chunk);
                }
            }
        }
        
        _logger.LogInformation(
            "Round-robin merged chunks: {OriginLen} -> {MergedLen} (deduplicated {DedupLen})",
            originLen,
            mergedChunks.Count,
            originLen - mergedChunks.Count);
        
        return mergedChunks;
    }
    
    private string BuildContextString(KGSearchResult searchResult, QueryParam queryParam)
    {
        var parts = new List<string>();
        
        // For mix mode, results are already merged in MixSearchStrategy, directly use LocalEntities
        // For local and global modes, need to merge (although only one has data)
        var allEntities = searchResult.Entities;
        
        // Entity data (apply token limit)
        allEntities = allEntities
            .Take(GetEntityCountByTokens(allEntities, queryParam.MaxEntityTokens))
            .ToList();
        
        if (allEntities.Count > 0)
        {
            // Use concise text format instead of JSON to save tokens
            var entitiesText = string.Join("\n", allEntities.Select(e => 
                $"{e.Name} ({e.Type}): {e.Description}"));
            
            parts.Add($"Knowledge Graph Data (Entity):\n\n```\n{entitiesText}\n```");
        }
        
        // For mix mode, results are already merged in MixSearchStrategy, directly use LocalRelations
        // For local and global modes, need to merge (although only one has data)
        var allRelations = searchResult.Relations;
        
        // Relationship data (apply token limit)
        allRelations = allRelations
            .Take(GetRelationCountByTokens(allRelations, queryParam.MaxRelationTokens))
            .ToList();
        
        if (allRelations.Count > 0)
        {
            // Use concise text format instead of JSON to save tokens
            var relationsText = string.Join("\n", allRelations.Select(r => 
                $"{r.SourceId} -> {r.TargetId}: {r.Keywords} - {r.Description}"));
            
            parts.Add($"Knowledge Graph Data (Relationship):\n\n```\n{relationsText}\n```");
        }
        
        // Document chunks
        if (searchResult.Chunks.Count > 0)
        {
            // Build a mapping from file path to file name for chunks
            var filePathToFileName = new Dictionary<string, string>();
            foreach (var chunk in searchResult.Chunks)
            {
                if (!string.IsNullOrEmpty(chunk.FilePath) && !filePathToFileName.ContainsKey(chunk.FilePath))
                {
                    filePathToFileName[chunk.FilePath] = ExtractFileName(chunk.FilePath);
                }
            }
            
            // Use concise text format with file name instead of reference_id
            var chunksText = string.Join("\n\n", searchResult.Chunks.Select(c =>
            {
                var fileName = !string.IsNullOrEmpty(c.FilePath) && filePathToFileName.TryGetValue(c.FilePath, out var name)
                    ? name
                    : "unknown";
                return $"[{fileName}]\n{c.Content}";
            }));
            
            parts.Add($"Document Chunks (Each entry shows the file name in brackets, refer to the `Reference Document List` for file paths):\n\n```\n{chunksText}\n```");
        }
        
        // Reference list
        if (searchResult.References.Count > 0)
        {
            var refList = string.Join("\n", searchResult.References.Select((r, _) => 
            {
                // Extract file name from file path
                var filePath = r.FilePath;
                var fileName = ExtractFileName(filePath);
                
                if (!string.IsNullOrEmpty(filePath) && (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    // Format as Markdown link: [FileName](URL)
                    return $"[{fileName}]({filePath})";
                }
                // Fallback to original format if not a URL
                return $"[{fileName}] {filePath}";
            }));
            
            parts.Add($"Reference Document List (Each entry shows the file name and file path. Use the file name in citations, not reference_id):\n\n```\n{refList}\n```");
        }
        
        return string.Join("\n\n", parts);
    }
    
    /// <summary>
    /// Extract file name from file path
    /// </summary>
    private string ExtractFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "unknown";
        
        // Handle URL paths
        if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(filePath);
                var path = uri.AbsolutePath;
                var fileName = Path.GetFileName(path);
                
                // Decode URL-encoded file name (e.g., %20 -> space, %E5%91%A8%E6%8A%A5 -> decoded text)
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        // Use Uri.UnescapeDataString to decode URL-encoded characters
                        fileName = Uri.UnescapeDataString(fileName);
                    }
                    catch (Exception ex)
                    {
                        // If decoding fails, use the original fileName
                        _logger.LogWarning(ex, "Failed to decode URL-encoded file name: {FileName}", fileName);
                    }
                    return fileName;
                }
                return "unknown";
            }
            catch
            {
                // If URI parsing fails, try to extract filename directly
                var lastSlash = filePath.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < filePath.Length - 1)
                {
                    var fileName = filePath.Substring(lastSlash + 1);
                    try
                    {
                        fileName = Uri.UnescapeDataString(fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decode URL-encoded file name from path: {FileName}", fileName);
                    }
                    return fileName;
                }
                return "unknown";
            }
        }
        
        // Handle local file paths
        var fileNameFromPath = Path.GetFileName(filePath);
        return !string.IsNullOrEmpty(fileNameFromPath) ? fileNameFromPath : filePath;
    }
    
    private List<ChunkData> ApplyTokenLimit(List<ChunkData> chunks, int maxTokens)
    {
        var result = new List<ChunkData>();
        var currentTokens = 0;
        
        foreach (var chunk in chunks)
        {
            // Calculate tokens based on actual format used in context: "[FileName]\nContent"
            var fileName = ExtractFileName(chunk.FilePath);
            var chunkText = $"[{fileName}]\n{chunk.Content}";
            var chunkTokens = tokenizer.CountTokens(chunkText);
            if (currentTokens + chunkTokens > maxTokens)
                break;
            
            result.Add(chunk);
            currentTokens += chunkTokens;
        }
        
        return result;
    }
    
    private int GetEntityCountByTokens(IEnumerable<EntityData> entities, int maxTokens)
    {
        var count = 0;
        var currentTokens = 0;
        
        foreach (var entity in entities)
        {
            // Calculate tokens based on actual text format used in context: "Name (Type): Description"
            var entityText = $"{entity.Name} ({entity.Type}): {entity.Description}";
            var tokens = tokenizer.CountTokens(entityText);
            if (currentTokens + tokens > maxTokens)
                break;
            
            count++;
            currentTokens += tokens;
        }
        
        return count;
    }
    
    private int GetRelationCountByTokens(IEnumerable<RelationData> relations, int maxTokens)
    {
        var count = 0;
        var currentTokens = 0;
        
        foreach (var relation in relations)
        {
            // Calculate tokens based on actual text format used in context: "Source -> Target: Keywords - Description"
            var relationText = $"{relation.SourceId} -> {relation.TargetId}: {relation.Keywords} - {relation.Description}";
            var tokens = tokenizer.CountTokens(relationText);
            if (currentTokens + tokens > maxTokens)
                break;
            
            count++;
            currentTokens += tokens;
        }
        
        return count;
    }
}
