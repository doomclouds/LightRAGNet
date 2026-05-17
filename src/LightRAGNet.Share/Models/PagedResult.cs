namespace LightRAGNet.Share.Models;

/// <summary>
/// Paged result
/// </summary>
/// <typeparam name="T">Data type</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// List of data items
    /// </summary>
    public List<T> Items { get; set; } = [];
    
    /// <summary>
    /// Total record count
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Current page number
    /// </summary>
    public int Page { get; set; }
    
    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }
    
    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }
}
