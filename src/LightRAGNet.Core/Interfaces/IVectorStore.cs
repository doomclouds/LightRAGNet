namespace LightRAGNet.Core.Interfaces;

public class VectorDocument
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string Content { get; set; } = string.Empty;
}

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string Content { get; set; } = string.Empty;
}

public interface IVectorStore
{
    /// <summary>
    /// Vector query
    /// </summary>
    Task<List<SearchResult>> QueryAsync(
        string collection,
        string query,
        int topK,
        float[]? queryEmbedding = null,
        float threshold = 0.2f,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Insert or update vectors
    /// </summary>
    Task UpsertAsync(
        string collection,
        IEnumerable<VectorDocument> documents,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete vectors
    /// </summary>
    Task DeleteAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get vector by ID
    /// </summary>
    Task<VectorDocument?> GetByIdAsync(
        string collection,
        string id,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get vectors in batch
    /// </summary>
    Task<List<VectorDocument>> GetByIdsAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);
}

