namespace LightRAGNet.Core.Models;

public enum QueryMode
{
    /// <summary>
    /// Local retrieval, focusing on directly related entities and relationships
    /// </summary>
    Local,
    
    /// <summary>
    /// Global retrieval, multi-hop graph traversal
    /// </summary>
    Global,
    
    /// <summary>
    /// Hybrid retrieval, combining local and global
    /// </summary>
    Hybrid,
    
    /// <summary>
    /// Naive retrieval, using vector retrieval only
    /// </summary>
    Naive,
    
    /// <summary>
    /// Mixed knowledge graph and vector retrieval
    /// </summary>
    Mix,
    
    /// <summary>
    /// Bypass retrieval, generate directly
    /// </summary>
    Bypass
}

