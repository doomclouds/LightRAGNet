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
        var normalized = value?.Trim().ToLowerInvariant();
        var displayValue = value is null ? "<null>" : value.Length == 0 ? "<empty>" : value;

        return normalized switch
        {
            "pending" => DocumentLifecycleStatus.Pending,
            "processing" => DocumentLifecycleStatus.Processing,
            "processed" => DocumentLifecycleStatus.Processed,
            "failed" => DocumentLifecycleStatus.Failed,
            "deleting" => DocumentLifecycleStatus.Deleting,
            "deleted" => DocumentLifecycleStatus.Deleted,
            "deletion_failed" => DocumentLifecycleStatus.DeletionFailed,
            _ => throw new ArgumentException($"Unknown lifecycle status wire value: '{displayValue}'.", nameof(value))
        };
    }
}
