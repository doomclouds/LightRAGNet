using System.Text.Json.Serialization;

namespace LightRAGNet.Core.Interfaces;

public class RerankResult
{
    [JsonPropertyName("index")] public int Index { get; set; }
    
    [JsonPropertyName("relevance_score")] public float RelevanceScore { get; set; }
}

public interface IRerankService
{
    /// <summary>
    /// Rerank documents
    /// </summary>
    Task<List<RerankResult>> RerankAsync(
        string query,
        List<string> documents,
        int topN,
        CancellationToken cancellationToken = default);
}