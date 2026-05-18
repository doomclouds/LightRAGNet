namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentChunkSnapshot(
    string ChunkId,
    int Tokens,
    int ChunkOrderIndex,
    string FilePath);
