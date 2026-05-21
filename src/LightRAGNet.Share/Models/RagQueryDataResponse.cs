namespace LightRAGNet.Share.Models;

public sealed class RagQueryDataResponse
{
    public string Status { get; init; } = "success";
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = [];
}
