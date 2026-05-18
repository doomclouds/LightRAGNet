namespace LightRAGNet.Services.DocumentLifecycle;

public enum DocumentLifecycleStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    Deleting,
    Deleted,
    DeletionFailed
}

public static class DocumentLifecycleStatusExtensions
{
    public static string ToWireValue(this DocumentLifecycleStatus status)
    {
        return status switch
        {
            DocumentLifecycleStatus.Pending => "pending",
            DocumentLifecycleStatus.Processing => "processing",
            DocumentLifecycleStatus.Processed => "processed",
            DocumentLifecycleStatus.Failed => "failed",
            DocumentLifecycleStatus.Deleting => "deleting",
            DocumentLifecycleStatus.Deleted => "deleted",
            DocumentLifecycleStatus.DeletionFailed => "deletion_failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown lifecycle status.")
        };
    }

    public static DocumentLifecycleStatus FromWireValue(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "pending" => DocumentLifecycleStatus.Pending,
            "processing" => DocumentLifecycleStatus.Processing,
            "processed" => DocumentLifecycleStatus.Processed,
            "failed" => DocumentLifecycleStatus.Failed,
            "deleting" => DocumentLifecycleStatus.Deleting,
            "deleted" => DocumentLifecycleStatus.Deleted,
            "deletion_failed" => DocumentLifecycleStatus.DeletionFailed,
            _ => DocumentLifecycleStatus.Pending
        };
    }
}
