using System.Text.Json.Serialization;

namespace LightRAGNet.Share.Models;

/// <summary>
/// Base event for RAG query streaming
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextChunkEvent), "text_chunk")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(DoneEvent), "done")]
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
