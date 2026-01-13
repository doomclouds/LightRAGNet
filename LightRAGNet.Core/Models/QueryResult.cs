namespace LightRAGNet.Core.Models;

public class QueryResult
{
    /// <summary>
    /// Text content for non-streaming response
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// Streaming response iterator
    /// </summary>
    public IAsyncEnumerable<string>? ResponseIterator { get; set; }
    
    /// <summary>
    /// Complete structured data, including references and metadata
    /// </summary>
    public Dictionary<string, object>? RawData { get; set; }
    
    /// <summary>
    /// Whether this is a streaming result
    /// </summary>
    public bool IsStreaming { get; set; }
    
    /// <summary>
    /// Reference list
    /// </summary>
    public List<ReferenceItem> ReferenceList => 
        RawData?.TryGetValue("data", out var data) == true &&
        data is Dictionary<string, object> dataDict &&
        dataDict.TryGetValue("references", out var refs) &&
        refs is List<object> refList
            ? refList.OfType<Dictionary<string, object>>()
                .Select(r => new ReferenceItem
                {
                    ReferenceId = r.GetValueOrDefault("reference_id")?.ToString() ?? "",
                    FilePath = r.GetValueOrDefault("file_path")?.ToString() ?? ""
                })
                .ToList()
            : new List<ReferenceItem>();
    
    /// <summary>
    /// Metadata
    /// </summary>
    public Dictionary<string, object> Metadata =>
        RawData?.TryGetValue("metadata", out var metadata) == true &&
        metadata is Dictionary<string, object> metaDict
            ? metaDict
            : new Dictionary<string, object>();
}

public class ReferenceItem
{
    public string ReferenceId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

