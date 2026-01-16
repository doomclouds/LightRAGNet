using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
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
    IOptions<LightRAGOptions> options)
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
            for (var index = 0; index < tokens.Count; 
                 index += _options.ChunkTokenSize - _options.ChunkOverlapTokenSize)
            {
                var end = Math.Min(index + _options.ChunkTokenSize, tokens.Count);
                var chunkTokens = tokens.Skip(index).Take(end - index).ToList();
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
        
        return new ChunkResult
        {
            ChunkId = chunk.Id,
            Embedding = embedding,
            Entities = extractionResult.Entities,
            Relationships = extractionResult.Relationships
        };
    }
}

