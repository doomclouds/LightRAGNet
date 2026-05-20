using LightRAGNet.Core.Models;

namespace LightRAGNet.Share.Models;

public sealed class RagQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public QueryMode Mode { get; set; } = QueryMode.Mix;
    public bool Stream { get; set; } = true;
    public bool IncludeReferences { get; set; } = true;
    public string ResponseType { get; set; } = "Multiple Paragraphs";
    public int TopK { get; set; } = 40;
    public int ChunkTopK { get; set; } = 20;
    public bool EnableRerank { get; set; } = true;
    public List<string> HighLevelKeywords { get; set; } = [];
    public List<string> LowLevelKeywords { get; set; } = [];
    public bool OnlyNeedContext { get; set; }
    public bool OnlyNeedPrompt { get; set; }
}
