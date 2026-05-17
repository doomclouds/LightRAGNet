namespace LightRAGNet.Core.Interfaces;

public interface IEmbeddingService
{
    /// <summary>
    /// Generate embedding vector for a single text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate embedding vectors in batch
    /// </summary>
    Task<float[][]> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Vector dimension
    /// </summary>
    int EmbeddingDimension { get; }
    
    /// <summary>
    /// Maximum token count
    /// </summary>
    int MaxTokenSize { get; }
}

