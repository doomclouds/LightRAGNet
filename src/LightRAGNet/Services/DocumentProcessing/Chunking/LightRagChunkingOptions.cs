namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class LightRagChunkingOptions
{
    public LightRagChunkingStrategy Strategy { get; set; } = LightRagChunkingStrategy.FixedToken;
    public FixedTokenChunkingOptions FixedToken { get; set; } = new();
    public RecursiveCharacterChunkingOptions RecursiveCharacter { get; set; } = new();
    public SemanticVectorChunkingOptions SemanticVector { get; set; } = new();
    public ParagraphSemanticChunkingOptions ParagraphSemantic { get; set; } = new();
}

public sealed class FixedTokenChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public string? SplitByCharacter { get; set; }
    public bool SplitByCharacterOnly { get; set; }
}

public sealed class RecursiveCharacterChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public List<string> Separators { get; set; } =
    [
        "\n\n", "\n", "。", "！", "？", "；", "，", " ", ""
    ];
}

public enum SemanticVectorBreakpointThresholdType
{
    Percentile,
    StandardDeviation,
    Interquartile,
    Gradient
}

public sealed class SemanticVectorChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public SemanticVectorBreakpointThresholdType BreakpointThresholdType { get; set; } =
        SemanticVectorBreakpointThresholdType.Percentile;
    public double? BreakpointThresholdAmount { get; set; }
    public int BufferSize { get; set; } = 1;
    public int? NumberOfChunks { get; set; }
    public int? MinChunkSize { get; set; }
    public int MinChunkTokenSize { get; set; } = 0;
    public string SentenceSplitRegex { get; set; } = @"(?<=[。？！.!?])\s+";
    public bool FallBackToRecursiveWhenEmbeddingUnavailable { get; set; } = true;
}

public sealed class ParagraphSemanticChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public int MinChunkTokenSize { get; set; } = 0;
}

public sealed record LightRagChunkingSnapshot(
    LightRagChunkingStrategy Strategy,
    int ChunkTokenSize,
    FixedTokenChunkingSnapshot FixedToken,
    RecursiveCharacterChunkingSnapshot RecursiveCharacter,
    SemanticVectorChunkingSnapshot SemanticVector,
    ParagraphSemanticChunkingSnapshot ParagraphSemantic);

public sealed record FixedTokenChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    string? SplitByCharacter,
    bool SplitByCharacterOnly);

public sealed record RecursiveCharacterChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    IReadOnlyList<string> Separators);

public sealed record SemanticVectorChunkingSnapshot(
    int ChunkTokenSize,
    SemanticVectorBreakpointThresholdType BreakpointThresholdType,
    double? BreakpointThresholdAmount,
    int BufferSize,
    int? NumberOfChunks,
    int? MinChunkSize,
    int MinChunkTokenSize,
    string SentenceSplitRegex,
    bool FallBackToRecursiveWhenEmbeddingUnavailable);

public sealed record ParagraphSemanticChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    int MinChunkTokenSize);
