namespace LightRAGNet.Web.Models;

public sealed class RagQueryException : Exception
{
    public RagQueryException(string error, string? message)
        : base(string.IsNullOrWhiteSpace(message) ? error : message)
    {
        Error = error;
    }

    public string Error { get; }
}
