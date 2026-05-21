namespace LightRAGNet.Server.Services;

public static class DocumentIntakeStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Deleting = "Deleting";
    public const string DeletionFailed = "DeletionFailed";

    public static bool IsRetryable(string? status)
    {
        return status is Failed or Cancelled;
    }

    public static bool IsCancellable(string? status)
    {
        return status is Queued or Processing;
    }

    public static bool IsActive(string? status)
    {
        return status is Queued or Processing or "Pending" or Deleting;
    }
}
