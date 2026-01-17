namespace LightRAGNet;

public class LightRAGOptions
{
    /// <summary>
    /// Working directory
    /// </summary>
    public string WorkingDir { get; set; } = "./rag_storage";

    public string Workspace { get; set; } = "_";
    
    /// <summary>
    /// Chunk token size
    /// </summary>
    public int ChunkTokenSize { get; set; } = 1200;
    
    /// <summary>
    /// Chunk overlap token count
    /// </summary>
    public int ChunkOverlapTokenSize { get; set; } = 100;
    
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
}

