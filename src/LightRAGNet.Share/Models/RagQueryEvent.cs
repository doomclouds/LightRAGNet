using LightRAGNet.Core.Models;
using System.Text.Json.Serialization;

namespace LightRAGNet.Share.Models;

/// <summary>
/// Base event for RAG query streaming
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextChunkEvent), "text_chunk")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(DoneEvent), "done")]
[JsonDerivedType(typeof(QueryMetadataEvent), "metadata")]
public abstract class RagQueryEvent
{
}

/// <summary>
/// Text chunk event containing a piece of the response
/// </summary>
public class TextChunkEvent : RagQueryEvent
{
    [JsonPropertyName("chunk")]
    public string Chunk { get; set; } = string.Empty;
}

/// <summary>
/// Error event
/// </summary>
public class ErrorEvent : RagQueryEvent
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Done event indicating stream completion
/// </summary>
public class DoneEvent : RagQueryEvent
{
}

public sealed class QueryMetadataEvent : RagQueryEvent
{
    public QueryMode Mode { get; init; } = QueryMode.Mix;
    public bool Stream { get; init; }
    public bool IncludeReferences { get; init; }
    public string ResponseType { get; init; } = "Multiple Paragraphs";
    public string CachePolicy { get; init; } = "Unknown";
    public IReadOnlyList<RagQueryReferenceDto> References { get; init; } = [];
    public IReadOnlyList<string> HighLevelKeywords { get; init; } = [];
    public IReadOnlyList<string> LowLevelKeywords { get; init; } = [];
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}

public sealed class RagQueryReferenceDto
{
    public string ReferenceId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? PreviewUrl { get; init; }
    public string OpenKind { get; init; } = "ExternalOrUnresolved";
}
