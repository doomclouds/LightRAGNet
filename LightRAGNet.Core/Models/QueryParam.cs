using Microsoft.Extensions.AI;

namespace LightRAGNet.Core.Models;

public class QueryParam
{
    /// <summary>
    /// Query mode
    /// </summary>
    public QueryMode Mode { get; set; } = QueryMode.Mix;
    
    /// <summary>
    /// If true, only return retrieved context without generating response
    /// </summary>
    public bool OnlyNeedContext { get; set; } = false;
    
    /// <summary>
    /// If true, only return generated prompt without generating response
    /// </summary>
    public bool OnlyNeedPrompt { get; set; } = false;
    
    /// <summary>
    /// Response format type
    /// </summary>
    public string ResponseType { get; set; } = "Multiple Paragraphs";
    
    /// <summary>
    /// Whether to enable streaming output
    /// </summary>
    public bool Stream { get; set; } = false;
    
    /// <summary>
    /// Number of entities/relationships to retrieve
    /// </summary>
    public int TopK { get; set; } = 40;
    
    /// <summary>
    /// Number of document chunks to retrieve
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
    /// High-level keywords list
    /// </summary>
    public List<string> HighLevelKeywords { get; set; } = [];
    
    /// <summary>
    /// Low-level keywords list
    /// </summary>
    public List<string> LowLevelKeywords { get; set; } = [];
    
    /// <summary>
    /// Conversation history
    /// </summary>
    public List<ChatMessage> ConversationHistory { get; set; } = [];
    
    /// <summary>
    /// User custom prompt
    /// </summary>
    public string? UserPrompt { get; set; }
    
    /// <summary>
    /// Whether to enable reranking
    /// </summary>
    public bool EnableRerank { get; set; } = true;
    
    /// <summary>
    /// Whether to include reference list
    /// </summary>
    public bool IncludeReferences { get; set; } = false;
}

