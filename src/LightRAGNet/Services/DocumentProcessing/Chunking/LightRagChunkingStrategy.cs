namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public enum LightRagChunkingStrategy
{
    FixedToken,
    RecursiveCharacter,
    SemanticVector,
    ParagraphSemantic
}

public static class LightRagChunkingStrategyExtensions
{
    public static string ToWireValue(this LightRagChunkingStrategy strategy)
    {
        return strategy switch
        {
            LightRagChunkingStrategy.FixedToken => "F",
            LightRagChunkingStrategy.RecursiveCharacter => "R",
            LightRagChunkingStrategy.SemanticVector => "V",
            LightRagChunkingStrategy.ParagraphSemantic => "P",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported chunking strategy.")
        };
    }
}
