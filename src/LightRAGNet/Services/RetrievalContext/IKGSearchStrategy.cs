using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Knowledge graph search strategy interface
/// </summary>
internal interface IKGSearchStrategy
{
    /// <summary>
    /// Execute search
    /// </summary>
    /// <param name="context">Search context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results</returns>
    Task<KGSearchResult> SearchAsync(KGSearchContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Knowledge graph search context
/// </summary>
internal class KGSearchContext
{
    public string Query { get; set; } = string.Empty;
    public string LowLevelKeywords { get; set; } = string.Empty;
    public string HighLevelKeywords { get; set; } = string.Empty;
    /// <summary>
    /// Embedding vector based on original query (for document chunk retrieval)
    /// </summary>
    public float[]? QueryEmbedding { get; set; } = [];
    /// <summary>
    /// Embedding vector based on LowLevelKeywords (for entity retrieval)
    /// </summary>
    public float[]? LowLevelKeywordsEmbedding { get; set; }
    /// <summary>
    /// Embedding vector based on HighLevelKeywords (for relation retrieval)
    /// </summary>
    public float[]? HighLevelKeywordsEmbedding { get; set; }
    public QueryParam QueryParam { get; set; } = new();
}

