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
    public List<ReferenceItem> ReferenceList
    {
        get
        {
            if (RawData?.TryGetValue("data", out var data) != true ||
                data is not Dictionary<string, object> dataDict ||
                dataDict.TryGetValue("references", out var refs) != true)
            {
                return [];
            }

            if (refs is IEnumerable<Dictionary<string, object>> dictionaryRefs)
            {
                return dictionaryRefs.Select(ToReferenceItem).ToList();
            }

            if (refs is IEnumerable<object> objectRefs)
            {
                return objectRefs
                    .OfType<Dictionary<string, object>>()
                    .Select(ToReferenceItem)
                    .ToList();
            }

            return [];
        }
    }
    
    /// <summary>
    /// Metadata
    /// </summary>
    public Dictionary<string, object> Metadata =>
        RawData?.TryGetValue("metadata", out var metadata) == true &&
        metadata is Dictionary<string, object> metaDict
            ? metaDict
            : new Dictionary<string, object>();

    private static ReferenceItem ToReferenceItem(Dictionary<string, object> reference)
    {
        return new ReferenceItem
        {
            ReferenceId = reference.GetValueOrDefault("reference_id")?.ToString() ?? string.Empty,
            FilePath = reference.GetValueOrDefault("file_path")?.ToString() ?? string.Empty
        };
    }
}

public class ReferenceItem
{
    public string ReferenceId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

