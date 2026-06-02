using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet;

public partial class LightRAGOptions
{
    /// <summary>
    /// Working directory
    /// </summary>
    public string WorkingDir { get; set; } = "./rag_storage";

    public string Workspace { get; set; } = "_";

    /// <summary>
    /// Enables all LLM-backed caches.
    /// </summary>
    public bool EnableLlmCache { get; set; } = true;

    /// <summary>
    /// Enables query response cache entries.
    /// </summary>
    public bool EnableQueryCache { get; set; } = true;

    /// <summary>
    /// Enables keyword extraction cache entries.
    /// </summary>
    public bool EnableKeywordCache { get; set; } = true;

    /// <summary>
    /// Enables indexing-stage LLM cache for entity extraction and graph summaries.
    /// </summary>
    public bool EnableLlmCacheForEntityExtract { get; set; } = true;
    
    /// <summary>
    /// Chunk token size
    /// </summary>
    public int ChunkTokenSize { get; set; } = 1200;
    
    /// <summary>
    /// Chunk overlap token count
    /// </summary>
    public int ChunkOverlapTokenSize { get; set; } = 100;

    public LightRagChunkingOptions? Chunking { get; set; } = new();
    
    /// <summary>
    /// Top-K retrieval count
    /// </summary>
    public int TopK { get; set; } = 40;
    
    /// <summary>
    /// Document chunk Top-K count
    /// </summary>
    public int ChunkTopK { get; set; } = 20;
    
    /// <summary>
    /// Maximum token count for entity context
    /// </summary>
    public int MaxEntityTokens { get; set; } = 6000;
    
    /// <summary>
    /// Maximum token count for relationship context
    /// </summary>
    public int MaxRelationTokens { get; set; } = 8000;
    
    /// <summary>
    /// Maximum token count for total context
    /// </summary>
    public int MaxTotalTokens { get; set; } = 30000;
    
    /// <summary>
    /// Cosine similarity threshold
    /// </summary>
    public float CosineThreshold { get; set; } = 0.2f;
    
    /// <summary>
    /// Entity type list
    /// </summary>
    public List<string>? EntityTypes { get; set; }
    
    /// <summary>
    /// Number of description fragments to force LLM summary
    /// </summary>
    public int ForceLLMSummaryOnMerge { get; set; } = 8;
    
    /// <summary>
    /// Maximum token count for summary
    /// </summary>
    public int SummaryMaxTokens { get; set; } = 1200;
    
    /// <summary>
    /// Summary context size
    /// </summary>
    public int SummaryContextSize { get; set; } = 12000;
    
    /// <summary>
    /// Recommended summary length
    /// </summary>
    public int SummaryLengthRecommended { get; set; } = 600;
    
    /// <summary>
    /// Maximum number of source_ids per entity
    /// </summary>
    public int MaxSourceIdsPerEntity { get; set; } = 300;
    
    /// <summary>
    /// Maximum number of source_ids per relation
    /// </summary>
    public int MaxSourceIdsPerRelation { get; set; } = 300;
    
    /// <summary>
    /// Maximum number of file paths
    /// </summary>
    public int MaxFilePaths { get; set; } = 100;
    
    /// <summary>
    /// Source IDs limit method: FIFO (keep latest) or KEEP (keep old)
    /// </summary>
    public string SourceIdsLimitMethod { get; set; } = "FIFO";
    
    /// <summary>
    /// Knowledge graph chunk selection method: WEIGHT (based on weight) or VECTOR (based on vector similarity)
    /// </summary>
    public string? KgChunkPickMethod { get; set; } = "VECTOR";
    
    /// <summary>
    /// Number of related chunks per entity/relation
    /// </summary>
    public int RelatedChunkNumber { get; set; } = 5;
    
    /// <summary>
    /// Maximum number of entities to extract per chunk (0 = no limit)
    /// </summary>
    public int MaxEntitiesPerChunk { get; set; } = 45;
    
    /// <summary>
    /// Maximum number of relationships to extract per chunk (0 = no limit)
    /// </summary>
    public int MaxRelationshipsPerChunk { get; set; } = 60;

    public LightRagChunkingSnapshot CreateChunkingSnapshot()
    {
        var chunking = Chunking ?? new LightRagChunkingOptions();
        var fixedToken = chunking.FixedToken ?? new FixedTokenChunkingOptions();
        var recursiveCharacter = chunking.RecursiveCharacter ?? new RecursiveCharacterChunkingOptions();
        var semanticVector = chunking.SemanticVector ?? new SemanticVectorChunkingOptions();
        var paragraphSemantic = chunking.ParagraphSemantic ?? new ParagraphSemanticChunkingOptions();
        var recursiveSeparators = recursiveCharacter.Separators is { Count: > 0 }
            ? recursiveCharacter.Separators
            : RecursiveCharacterChunkingOptions.CreateDefaultSeparators();
        var sentenceSplitRegex = string.IsNullOrWhiteSpace(semanticVector.SentenceSplitRegex)
            ? SemanticVectorChunkingOptions.DefaultSentenceSplitRegex
            : semanticVector.SentenceSplitRegex;

        var globalSize = ChunkingUtilities.RequirePositiveChunkSize(ChunkTokenSize, nameof(ChunkTokenSize));
        var globalOverlap = ChunkingUtilities.ClampOverlap(globalSize, ChunkOverlapTokenSize);

        var fixedSize = fixedToken.ChunkTokenSize ?? globalSize;
        fixedSize = ChunkingUtilities.RequirePositiveChunkSize(fixedSize, "Chunking:FixedToken:ChunkTokenSize");
        var fixedOverlap = ChunkingUtilities.ClampOverlap(
            fixedSize,
            fixedToken.ChunkOverlapTokenSize ?? globalOverlap);

        var recursiveSize = recursiveCharacter.ChunkTokenSize ?? globalSize;
        recursiveSize = ChunkingUtilities.RequirePositiveChunkSize(
            recursiveSize,
            "Chunking:RecursiveCharacter:ChunkTokenSize");
        var recursiveOverlap = ChunkingUtilities.ClampOverlap(
            recursiveSize,
            recursiveCharacter.ChunkOverlapTokenSize ?? globalOverlap);

        var vectorSize = semanticVector.ChunkTokenSize ?? globalSize;
        vectorSize = ChunkingUtilities.RequirePositiveChunkSize(
            vectorSize,
            "Chunking:SemanticVector:ChunkTokenSize");

        var paragraphSize = paragraphSemantic.ChunkTokenSize ?? 2000;
        paragraphSize = ChunkingUtilities.RequirePositiveChunkSize(
            paragraphSize,
            "Chunking:ParagraphSemantic:ChunkTokenSize");
        var paragraphOverlap = ChunkingUtilities.ClampOverlap(
            paragraphSize,
            paragraphSemantic.ChunkOverlapTokenSize ?? globalOverlap);

        return new LightRagChunkingSnapshot(
            chunking.Strategy,
            globalSize,
            new FixedTokenChunkingSnapshot(
                fixedSize,
                fixedOverlap,
                fixedToken.SplitByCharacter,
                fixedToken.SplitByCharacterOnly),
            new RecursiveCharacterChunkingSnapshot(
                recursiveSize,
                recursiveOverlap,
                [.. recursiveSeparators]),
            new SemanticVectorChunkingSnapshot(
                vectorSize,
                semanticVector.BreakpointThresholdType,
                semanticVector.BreakpointThresholdAmount,
                Math.Max(1, semanticVector.BufferSize),
                semanticVector.NumberOfChunks,
                semanticVector.MinChunkSize,
                Math.Max(0, semanticVector.MinChunkTokenSize),
                sentenceSplitRegex,
                semanticVector.FallBackToRecursiveWhenEmbeddingUnavailable),
            new ParagraphSemanticChunkingSnapshot(
                paragraphSize,
                paragraphOverlap,
                Math.Max(0, paragraphSemantic.MinChunkTokenSize)));
    }
}

