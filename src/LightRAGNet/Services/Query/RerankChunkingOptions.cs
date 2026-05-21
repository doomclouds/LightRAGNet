namespace LightRAGNet.Services.Query;

public sealed class RerankChunkingOptions
{
    public bool EnableChunking { get; set; } = true;

    public int MaxTokensPerDocument { get; set; } = 480;

    public int OverlapTokens { get; set; } = 32;
}
