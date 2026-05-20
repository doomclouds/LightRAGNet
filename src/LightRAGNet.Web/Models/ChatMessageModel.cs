using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Web.Models;

/// <summary>
/// Chat message model for UI display
/// </summary>
public class ChatMessageModel
{
    /// <summary>
    /// Message role (User or Assistant)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Message text content
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Query mode used to produce the message
    /// </summary>
    public QueryMode? Mode { get; set; }

    /// <summary>
    /// Indicates whether the message is currently streaming
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Indicates whether the message can be cached
    /// </summary>
    public bool IsCacheable { get; set; }

    /// <summary>
    /// Source references returned by the query
    /// </summary>
    public List<RagQueryReferenceDto> References { get; set; } = [];

    /// <summary>
    /// High-level keywords used by the query
    /// </summary>
    public List<string> HighLevelKeywords { get; set; } = [];

    /// <summary>
    /// Low-level keywords used by the query
    /// </summary>
    public List<string> LowLevelKeywords { get; set; } = [];

    /// <summary>
    /// Query diagnostics for UI display and debugging
    /// </summary>
    public Dictionary<string, string> Diagnostics { get; set; } = [];

    /// <summary>
    /// Error message associated with the chat message
    /// </summary>
    public string? ErrorMessage { get; set; }
}
