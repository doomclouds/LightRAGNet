using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LightRAGNet.Services.DocumentProcessing;

/// <summary>
/// Document processing service
/// Reference: Python version operate.py chunking_by_token_size and extract_entities
/// </summary>
public class DocumentProcessingService(
    ILLMService llmService,
    IEmbeddingService embeddingService,
    ITokenizer tokenizer,
    [FromKeyedServices(KVContracts.LLMCache)]
    IKVStore llmCacheStore,
    IOptions<LightRAGOptions> options,
    ILogger<DocumentProcessingService> logger)
{
    private readonly LightRAGOptions _options = options.Value;

    /// <summary>
    /// Document chunking
    /// Reference: operate.py chunking_by_token_size function
    /// </summary>
    public List<Chunk> ChunkDocument(
        string content,
        string docId,
        string filePath = "",
        string? splitByCharacter = null,
        bool splitByCharacterOnly = false)
    {
        var chunks = new List<Chunk>();
        
        // Preprocess content: trim leading/trailing whitespace before tokenization
        // This matches Python version behavior to ensure consistent token counts
        content = content.Trim();
        
        var tokens = tokenizer.Encode(content);
        
        if (!string.IsNullOrEmpty(splitByCharacter))
        {
            var rawChunks = content.Split(splitByCharacter);
            var newChunks = new List<(int Tokens, string Content)>();
            
            if (splitByCharacterOnly)
            {
                foreach (var chunk in rawChunks)
                {
                    var chunkTokens = tokenizer.Encode(chunk);
                    if (chunkTokens.Count > _options.ChunkTokenSize)
                    {
                        throw new InvalidOperationException(
                            $"Chunk exceeds token limit: {chunkTokens.Count} > {_options.ChunkTokenSize}");
                    }
                    newChunks.Add((chunkTokens.Count, chunk));
                }
            }
            else
            {
                foreach (var chunk in rawChunks)
                {
                    var chunkTokens = tokenizer.Encode(chunk);
                    if (chunkTokens.Count > _options.ChunkTokenSize)
                    {
                        // Further split by token size
                        for (var start = 0; start < chunkTokens.Count; 
                             start += _options.ChunkTokenSize - _options.ChunkOverlapTokenSize)
                        {
                            var end = Math.Min(start + _options.ChunkTokenSize, chunkTokens.Count);
                            var subTokens = chunkTokens.Skip(start).Take(end - start).ToList();
                            var chunkContent = tokenizer.Decode(subTokens);
                            newChunks.Add((subTokens.Count, chunkContent));
                        }
                    }
                    else
                    {
                        newChunks.Add((chunkTokens.Count, chunk));
                    }
                }
            }
            
            for (var index = 0; index < newChunks.Count; index++)
            {
                var (tokenCount, chunkContent) = newChunks[index];
                chunks.Add(new Chunk
                {
                    Id = HashUtils.ComputeMd5Hash(chunkContent, "chunk-"),
                    Content = chunkContent.Trim(),
                    Tokens = tokenCount,
                    ChunkOrderIndex = index,
                    FullDocId = docId,
                    FilePath = filePath
                });
            }
        }
        else
        {
            // Sliding window split by token size
            // Reference: Python version chunking_by_token_size function
            // Python: for index, start in enumerate(range(0, len(tokens), max_token_size - overlap_token_size))
            var stepSize = _options.ChunkTokenSize - _options.ChunkOverlapTokenSize;
            
            for (var index = 0; index < tokens.Count; index += stepSize)
            {
                var end = Math.Min(index + _options.ChunkTokenSize, tokens.Count);
                var remainingTokens = tokens.Count - index;
                
                // If remaining tokens are less than overlap size, merge with previous chunk
                // This matches Python version behavior to avoid creating tiny final chunks
                if (remainingTokens <= _options.ChunkOverlapTokenSize && chunks.Count > 0)
                {
                    // Merge remaining tokens into previous chunk
                    var prevChunk = chunks[^1];
                    var prevChunkTokens = tokenizer.Encode(prevChunk.Content);
                    var remainingChunkTokens = tokens.Skip(index).Take(remainingTokens).ToList();
                    var mergedTokens = prevChunkTokens.Concat(remainingChunkTokens).ToList();
                    var mergedContent = tokenizer.Decode(mergedTokens);
                    
                    // Update previous chunk with merged content
                    chunks[^1] = new Chunk
                    {
                        Id = HashUtils.ComputeMd5Hash(mergedContent, "chunk-"),
                        Content = mergedContent.Trim(),
                        Tokens = mergedTokens.Count,
                        ChunkOrderIndex = prevChunk.ChunkOrderIndex,
                        FullDocId = docId,
                        FilePath = filePath
                    };
                    break;
                }
                
                var chunkTokens = tokens.Skip(index).Take(end - index).ToList();
                
                // Only create chunk if it has tokens
                if (chunkTokens.Count == 0)
                {
                    break;
                }
                
                var chunkContent = tokenizer.Decode(chunkTokens);
                
                chunks.Add(new Chunk
                {
                    Id = HashUtils.ComputeMd5Hash(chunkContent, "chunk-"),
                    Content = chunkContent.Trim(),
                    Tokens = chunkTokens.Count,
                    ChunkOrderIndex = chunks.Count,
                    FullDocId = docId,
                    FilePath = filePath
                });
            }
        }
        
        return chunks;
    }
    
    /// <summary>
    /// Process single chunk: vectorization and entity extraction
    /// Reference: operate.py extract_entities function
    /// Implements LLMCache to avoid re-processing chunks on interruption
    /// </summary>
    public async Task<ChunkResult> ProcessChunkAsync(
        Chunk chunk,
        CancellationToken cancellationToken = default)
    {
        // Check cache first (using chunk ID as key)
        var cacheKey = chunk.Id;
        var cachedData = await llmCacheStore.GetByIdAsync(cacheKey, cancellationToken);
        
        if (cachedData != null)
        {
            logger.LogDebug("Cache hit for chunk {ChunkId}", chunk.Id);
            
            // Deserialize cached result
            try
            {
                var cachedResult = DeserializeChunkResult(cachedData);
                
                // Add source_id and file_path (these may vary per document, so we update them)
                foreach (var entity in cachedResult.Entities)
                {
                    entity.SourceId = chunk.Id;
                    entity.FilePath = chunk.FilePath;
                    // Keep original timestamp from cache
                }
                
                foreach (var relation in cachedResult.Relationships)
                {
                    relation.SourceChunkId = chunk.Id;
                    relation.FilePath = chunk.FilePath;
                    // Keep original timestamp from cache
                }
                
                return cachedResult;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize cached chunk result for {ChunkId}, will reprocess", chunk.Id);
                // Fall through to reprocess
            }
        }
        
        // Cache miss or deserialization failed, process chunk
        logger.LogDebug("Cache miss for chunk {ChunkId}, processing...", chunk.Id);
        
        // 1. Vectorization
        var embedding = await embeddingService.GenerateEmbeddingAsync(
            chunk.Content,
            cancellationToken);
        
        // 2. Extract entities and relationships
        var entityTypes = _options.EntityTypes ??
        [
            "Person", "Creature", "Organization", "Location", "Event",
            "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"
        ];
        
        // Get extraction limits from options (0 = no limit, use default values)
        int? maxEntities = _options.MaxEntitiesPerChunk > 0 ? _options.MaxEntitiesPerChunk : null;
        int? maxRelationships = _options.MaxRelationshipsPerChunk > 0 ? _options.MaxRelationshipsPerChunk : null;
        
        var extractionResult = await llmService.ExtractEntitiesAsync(
            chunk.Content,
            entityTypes,
            temperature: 0.3f,
            maxEntities: maxEntities,
            maxRelationships: maxRelationships,
            cancellationToken: cancellationToken);
        
        // Add source_id and file_path to entities and relationships
        foreach (var entity in extractionResult.Entities)
        {
            entity.SourceId = chunk.Id;
            entity.FilePath = chunk.FilePath;
            entity.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        
        foreach (var relation in extractionResult.Relationships)
        {
            relation.SourceChunkId = chunk.Id;
            relation.FilePath = chunk.FilePath;
            relation.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        
        var result = new ChunkResult
        {
            ChunkId = chunk.Id,
            Embedding = embedding,
            Entities = extractionResult.Entities,
            Relationships = extractionResult.Relationships
        };
        
        // Cache the result (without source_id/file_path as they vary per document)
        // Persist immediately to avoid data loss on interruption
        try
        {
            var cacheData = SerializeChunkResult(result);
            await llmCacheStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [cacheKey] = cacheData
            }, cancellationToken);
            
            // Persist immediately to disk to ensure cache survives interruption
            await llmCacheStore.IndexDoneCallbackAsync(cancellationToken);
            logger.LogDebug("Cached and persisted chunk result for {ChunkId}", chunk.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cache chunk result for {ChunkId}", chunk.Id);
            // Continue even if caching fails
        }
        
        return result;
    }
    
    /// <summary>
    /// Serialize ChunkResult for caching (excludes source_id/file_path as they vary per document)
    /// Reference: Python version LLMCache implementation
    /// </summary>
    private Dictionary<string, object> SerializeChunkResult(ChunkResult result)
    {
        return new Dictionary<string, object>
        {
            ["chunk_id"] = result.ChunkId,
            ["embedding"] = result.Embedding.Select(e => (object)e).ToList(),
            ["entities"] = result.Entities.Select(e => new Dictionary<string, object>
            {
                ["name"] = e.Name,
                ["type"] = e.Type,
                ["description"] = e.Description,
                ["timestamp"] = e.Timestamp
            }).Cast<object>().ToList(),
            ["relationships"] = result.Relationships.Select(r => new Dictionary<string, object>
            {
                ["source_id"] = r.SourceId,
                ["target_id"] = r.TargetId,
                ["keywords"] = r.Keywords,
                ["description"] = r.Description,
                ["weight"] = r.Weight,
                ["timestamp"] = r.Timestamp
            }).Cast<object>().ToList()
        };
    }
    
    /// <summary>
    /// Deserialize cached ChunkResult
    /// Reference: Python version LLMCache implementation
    /// </summary>
    private ChunkResult DeserializeChunkResult(Dictionary<string, object> data)
    {
        var result = new ChunkResult
        {
            ChunkId = data.GetValueOrDefault("chunk_id")?.ToString() ?? string.Empty
        };
        
        // Deserialize embedding
        if (data.TryGetValue("embedding", out var embeddingObj))
        {
            result.Embedding = embeddingObj switch
            {
                JsonElement embeddingJson when embeddingJson.ValueKind == JsonValueKind.Array =>
                    embeddingJson.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray(),
                List<object> embeddingList =>
                    embeddingList.Select(e => Convert.ToSingle(e)).ToArray(),
                _ => Array.Empty<float>()
            };
        }
        
        // Deserialize entities
        if (data.TryGetValue("entities", out var entitiesObj))
        {
            result.Entities = entitiesObj switch
            {
                JsonElement entitiesJson when entitiesJson.ValueKind == JsonValueKind.Array =>
                    entitiesJson.EnumerateArray().Select(entityJson => new Entity
                    {
                        Name = entityJson.GetProperty("name").GetString() ?? string.Empty,
                        Type = entityJson.GetProperty("type").GetString() ?? string.Empty,
                        Description = entityJson.GetProperty("description").GetString() ?? string.Empty,
                        Timestamp = entityJson.TryGetProperty("timestamp", out var ts) ? ts.GetInt64() : 0
                    }).ToList(),
                List<object> entitiesListObj =>
                    entitiesListObj.OfType<Dictionary<string, object>>().Select(entityDict => new Entity
                    {
                        Name = entityDict.GetValueOrDefault("name")?.ToString() ?? string.Empty,
                        Type = entityDict.GetValueOrDefault("type")?.ToString() ?? string.Empty,
                        Description = entityDict.GetValueOrDefault("description")?.ToString() ?? string.Empty,
                        Timestamp = entityDict.TryGetValue("timestamp", out var ts) ? Convert.ToInt64(ts) : 0
                    }).ToList(),
                _ => new List<Entity>()
            };
        }
        
        // Deserialize relationships
        if (data.TryGetValue("relationships", out var relationsObj))
        {
            result.Relationships = relationsObj switch
            {
                JsonElement relationsJson when relationsJson.ValueKind == JsonValueKind.Array =>
                    relationsJson.EnumerateArray().Select(relationJson => new Relationship
                    {
                        SourceId = relationJson.GetProperty("source_id").GetString() ?? string.Empty,
                        TargetId = relationJson.GetProperty("target_id").GetString() ?? string.Empty,
                        Keywords = relationJson.GetProperty("keywords").GetString() ?? string.Empty,
                        Description = relationJson.GetProperty("description").GetString() ?? string.Empty,
                        Weight = relationJson.TryGetProperty("weight", out var w) ? (float)w.GetDouble() : 1.0f,
                        Timestamp = relationJson.TryGetProperty("timestamp", out var ts) ? ts.GetInt64() : 0
                    }).ToList(),
                List<object> relationsListObj =>
                    relationsListObj.OfType<Dictionary<string, object>>().Select(relationDict => new Relationship
                    {
                        SourceId = relationDict.GetValueOrDefault("source_id")?.ToString() ?? string.Empty,
                        TargetId = relationDict.GetValueOrDefault("target_id")?.ToString() ?? string.Empty,
                        Keywords = relationDict.GetValueOrDefault("keywords")?.ToString() ?? string.Empty,
                        Description = relationDict.GetValueOrDefault("description")?.ToString() ?? string.Empty,
                        Weight = relationDict.TryGetValue("weight", out var w) ? Convert.ToSingle(w) : 1.0f,
                        Timestamp = relationDict.TryGetValue("timestamp", out var ts) ? Convert.ToInt64(ts) : 0
                    }).ToList(),
                _ => new List<Relationship>()
            };
        }
        
        return result;
    }
}

