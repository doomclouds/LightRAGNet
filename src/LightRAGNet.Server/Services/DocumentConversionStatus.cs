namespace LightRAGNet.Server.Services;

public static class DocumentConversionStatus
{
    public const string NotStarted = "NotStarted";
    public const string NotRequired = "NotRequired";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
