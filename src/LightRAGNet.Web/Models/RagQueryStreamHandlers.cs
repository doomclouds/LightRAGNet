using LightRAGNet.Share.Models;

namespace LightRAGNet.Web.Models;

public sealed class RagQueryStreamHandlers
{
    public Func<string, Task> OnChunkReceived { get; init; } = _ => Task.CompletedTask;
    public Func<QueryMetadataEvent, Task> OnMetadataReceived { get; init; } = _ => Task.CompletedTask;
}
