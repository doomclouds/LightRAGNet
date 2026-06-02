using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Services.QueryCache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.DocumentProcessing;

/// <summary>
/// Document processing service
/// Reference: Python version operate.py chunking_by_token_size and extract_entities
/// </summary>
public class DocumentProcessingService(
    ILLMService llmService,
    IEmbeddingService embeddingService,
    ITokenizer tokenizer,
    LightRagLlmCacheService llmCacheService,
    IOptions<LightRAGOptions> options,
    ILogger<DocumentProcessingService> logger,
    LightRagChunkingService? chunkingService = null)
{
    private readonly LightRAGOptions _options = options.Value;
    private readonly LightRagChunkingService? _chunkingService = chunkingService;
    private readonly SemaphoreSlim _extractGenerationSemaphore = new(10, 10);
    private static readonly List<string> DefaultEntityTypes =
    [
        "Person", "Creature", "Organization", "Location", "Event",
        "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"
    ];

    /// <summary>
    /// Document chunking
    /// Reference: operate.py chunking_by_token_size function
    /// </summary>
    public async Task<IReadOnlyList<Chunk>> ChunkDocumentAsync(
        string content,
        string docId,
        string filePath = "",
        LightRagChunkingSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (_chunkingService is null)
        {
            return ChunkDocument(content, docId, filePath);
        }

        return await _chunkingService.ChunkDocumentAsync(
            content,
            docId,
            filePath,
            snapshot,
            cancellationToken);
    }

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
        // 1. Vectorization
        var embedding = await embeddingService.GenerateEmbeddingAsync(
            chunk.Content,
            cancellationToken);
        
        // 2. Extract entities and relationships
        var entityTypes = ResolveEntityTypes();
        
        var maxEntities = _options.MaxEntitiesPerChunk > 0 ? _options.MaxEntitiesPerChunk : 45;
        var maxRelationships = _options.MaxRelationshipsPerChunk > 0 ? _options.MaxRelationshipsPerChunk : 60;
        var prompt = EntityExtractionPromptBuilder.Build(
            chunk.Content,
            entityTypes,
            maxEntities,
            maxRelationships);

        var cacheResult = await llmCacheService.GetOrCreateExtractAsync(
            prompt.CanonicalPrompt,
            chunk.Id,
            token => GenerateExtractResponseAsync(prompt, chunk.Id, token),
            cancellationToken);
        var rawResponse = cacheResult.Value;
        var llmCacheKeys = new List<string>();

        if (cacheResult.CacheKey is not null)
        {
            llmCacheKeys.Add(cacheResult.CacheKey);
        }

        if (cacheResult.Hit)
        {
            logger.LogDebug("Extract cache hit for chunk {ChunkId}", chunk.Id);
        }
        else if (cacheResult.CacheEnabled)
        {
            logger.LogDebug(
                "Extract cache miss for chunk {ChunkId}; generated response, saved={Saved}",
                chunk.Id,
                cacheResult.Saved);
        }
        else
        {
            logger.LogDebug("Extract cache disabled for chunk {ChunkId}; generated response", chunk.Id);
        }
        
        var extractionResult = EntityExtractionResultParser.Parse(
            rawResponse,
            maxEntities,
            maxRelationships);
        
        // Add source_id and file_path to entities and relationships
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var entity in extractionResult.Entities)
        {
            entity.SourceId = chunk.Id;
            entity.FilePath = chunk.FilePath;
            entity.Timestamp = timestamp;
        }
        
        foreach (var relation in extractionResult.Relationships)
        {
            relation.SourceChunkId = chunk.Id;
            relation.FilePath = chunk.FilePath;
            relation.Timestamp = timestamp;
        }
        
        var result = new ChunkResult
        {
            ChunkId = chunk.Id,
            Embedding = embedding,
            Entities = extractionResult.Entities,
            Relationships = extractionResult.Relationships,
            LlmCacheKeys = llmCacheKeys.Distinct(StringComparer.Ordinal).ToList()
        };

        return result;
    }

    private async Task<string> GenerateExtractResponseAsync(
        EntityExtractionPrompt prompt,
        string chunkId,
        CancellationToken cancellationToken)
    {
        var waitStart = DateTime.UtcNow;
        await _extractGenerationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var waitTime = (DateTime.UtcNow - waitStart).TotalMilliseconds;
            if (waitTime > 1000)
            {
                logger.LogDebug(
                    "Extract generation semaphore wait time was {WaitTime}ms for chunk {ChunkId}",
                    waitTime,
                    chunkId);
            }

            var response = await llmService.GenerateAsync(
                prompt.UserPrompt,
                prompt.SystemPrompt,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return response;
        }
        finally
        {
            _extractGenerationSemaphore.Release();
        }
    }

    private List<string> ResolveEntityTypes()
    {
        if (_options.EntityTypes is null)
        {
            return DefaultEntityTypes;
        }

        var configuredEntityTypes = _options.EntityTypes
            .Where(entityType => !string.IsNullOrWhiteSpace(entityType))
            .ToList();

        return configuredEntityTypes.Count > 0
            ? configuredEntityTypes
            : DefaultEntityTypes;
    }
}

