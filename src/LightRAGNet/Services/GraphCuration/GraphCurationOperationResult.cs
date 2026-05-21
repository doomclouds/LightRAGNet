namespace LightRAGNet.Services.GraphCuration;

public sealed record GraphCurationOperationSummary(
    bool Merged,
    string MergeStatus,
    string? MergeError,
    string OperationStatus,
    string? TargetEntity,
    string FinalEntity,
    bool Renamed);

public sealed record GraphCurationOperationResult(
    bool Succeeded,
    string Status,
    string Message,
    Dictionary<string, object>? Data = null,
    GraphCurationOperationSummary? OperationSummary = null,
    string? FailureStage = null)
{
    public static GraphCurationOperationResult Success(
        string message,
        Dictionary<string, object>? data = null,
        GraphCurationOperationSummary? summary = null) =>
        new(true, "success", message, data, summary);

    public static GraphCurationOperationResult Failure(
        string message,
        string failureStage,
        string status = "failure",
        GraphCurationOperationSummary? summary = null) =>
        new(false, status, message, null, summary, failureStage);
}
