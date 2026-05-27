using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal enum RagasEvaluationRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

internal enum RagasEvaluationCaseStatus
{
    Succeeded,
    Failed,
    Cancelled
}

internal sealed record RagasDatasetCase(
    string CaseName,
    string Question,
    string GroundTruth,
    string Project);

internal sealed record RagasMetricScore(double Score, string Reason);

internal sealed record RagasMetricSet(
    RagasMetricScore Faithfulness,
    RagasMetricScore AnswerRelevance,
    RagasMetricScore ContextRecall,
    RagasMetricScore ContextPrecision)
{
    public double RagasScore =>
        (Faithfulness.Score + AnswerRelevance.Score + ContextRecall.Score + ContextPrecision.Score) / 4.0;
}

internal sealed record RagasJudgeParseResult(
    bool Success,
    RagasMetricSet? Metrics,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static RagasJudgeParseResult Succeeded(RagasMetricSet metrics) =>
        new(true, metrics, null, null);

    public static RagasJudgeParseResult Failed(string code, string message) =>
        new(false, null, code, message);
}

internal sealed record RagasTextSnapshot(
    string Preview,
    string Hash,
    string? Text);

internal sealed record RagasContextSnapshot(
    string Preview,
    string Hash,
    string? Text,
    string ChunkId,
    string FilePath,
    string ReferenceId);

internal sealed class RagasEvaluationRunRecord
{
    public string RunId { get; set; } = string.Empty;
    public RagasEvaluationRunStatus Status { get; set; } = RagasEvaluationRunStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RagasEvaluationRequestSnapshot Request { get; set; } = new();
    public RagasEvaluationSummaryDto Summary { get; set; } = new();
    public List<RagasEvaluationCaseResultDto> Cases { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
    public string? Error { get; set; }
}

internal sealed record RagasEvaluationOperationResult<T>(
    bool Success,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    int StatusCode)
{
    public static RagasEvaluationOperationResult<T> Ok(T value) =>
        new(true, value, null, null, StatusCodes.Status200OK);

    public static RagasEvaluationOperationResult<T> Fail(string code, string message, int statusCode) =>
        new(false, default, code, message, statusCode);
}

internal sealed record RagasRetrievedContext(
    string Content,
    string ChunkId,
    string FilePath,
    string ReferenceId);

internal sealed record RagasQueryExecutionResult(
    string Answer,
    IReadOnlyList<RagasRetrievedContext> Contexts,
    QueryMode Mode);

internal sealed record RagasEvaluationCaseInput(
    string CaseName,
    string Question,
    string GroundTruth,
    IReadOnlyList<RagasRetrievedContext> Contexts,
    string Answer);

internal sealed record RagasEvaluatorResult(
    string RawResponse,
    RagasJudgeParseResult ParseResult,
    string Prompt);
