namespace LightRAGNet.Core.Interfaces;

public interface IKVStore
{
    /// <summary>
    /// Get value by ID
    /// </summary>
    Task<Dictionary<string, object>?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get values in batch
    /// </summary>
    Task<List<Dictionary<string, object>>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Filter existing keys
    /// </summary>
    Task<HashSet<string>> FilterKeysAsync(HashSet<string> keys, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Insert or update
    /// </summary>
    Task UpsertAsync(Dictionary<string, Dictionary<string, object>> data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete
    /// </summary>
    Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if empty
    /// </summary>
    Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Index completion callback (persistence)
    /// </summary>
    Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clear all data (memory and persistence)
    /// Reference: Python version drop method
    /// </summary>
    Task DropAsync(CancellationToken cancellationToken = default);
}

