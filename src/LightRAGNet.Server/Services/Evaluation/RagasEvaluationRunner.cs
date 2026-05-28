using LightRAGNet.Share.Models;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationRunner(
    RagasEvaluationRunStore store,
    IRagasRagQueryClient queryClient,
    IRagasEvaluator evaluator,
    RagasEvaluationTextSnapshotter snapshotter,
    IOptions<RagasEvaluationOptions> options,
    RagasEvaluationSecretProvider secretProvider,
    ILogger<RagasEvaluationRunner> logger)
{
    public async Task ExecuteAsync(
        RagasEvaluationRunRecord run,
        IReadOnlyList<RagasDatasetCase> cases,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(cases);

        try
        {
            run.Status = RagasEvaluationRunStatus.Running;
            run.StartedAt = DateTimeOffset.UtcNow;
            run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
            await store.UpsertAsync(run, CancellationToken.None);

            foreach (var dataSetCase in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queryResult = await queryClient.QueryAsync(dataSetCase, run.Request.Query, cancellationToken);
                if (queryResult.Contexts.Count == 0)
                {
                    run.Cases.Add(CreateFailedCaseResult(
                        dataSetCase,
                        queryResult.Answer,
                        queryResult.Contexts,
                        [
                            new RagasEvaluationDiagnosticDto
                            {
                                Code = "no_contexts",
                                Message = "RAG query returned no retrieved contexts."
                            }
                        ],
                        run.Request.IncludeFullText));
                    run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
                    await store.UpsertAsync(run, CancellationToken.None);
                    continue;
                }

                var input = new RagasEvaluationCaseInput(
                    dataSetCase.CaseName,
                    dataSetCase.Question,
                    dataSetCase.GroundTruth,
                    queryResult.Contexts,
                    queryResult.Answer);
                var evaluatorResult = await evaluator.EvaluateAsync(input, cancellationToken);
                if (!evaluatorResult.ParseResult.Success || evaluatorResult.ParseResult.Metrics is null)
                {
                    run.Cases.Add(CreateFailedCaseResult(
                        dataSetCase,
                        queryResult.Answer,
                        queryResult.Contexts,
                        AppendJudgeDiagnostics(
                            [
                                new RagasEvaluationDiagnosticDto
                                {
                                    Code = evaluatorResult.ParseResult.ErrorCode ?? "parser_failed",
                                    Message = evaluatorResult.ParseResult.ErrorMessage ?? "Judge response could not be parsed."
                                }
                            ],
                            evaluatorResult,
                            run.Request.IncludeFullText),
                        run.Request.IncludeFullText));
                    run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
                    await store.UpsertAsync(run, CancellationToken.None);
                    continue;
                }

                run.Cases.Add(CreateSucceededCaseResult(
                    dataSetCase,
                    queryResult.Answer,
                    queryResult.Contexts,
                    evaluatorResult.ParseResult.Metrics,
                    AppendJudgeDiagnostics([], evaluatorResult, run.Request.IncludeFullText),
                    run.Request.IncludeFullText));
                run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
                await store.UpsertAsync(run, CancellationToken.None);
            }

            run.Status = RagasEvaluationRunStatus.Completed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
            await store.UpsertAsync(run, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = RagasEvaluationRunStatus.Cancelled;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Summary = CreateSummary(
                cases.Count,
                run.Cases,
                Math.Max(0, cases.Count - run.Cases.Count),
                run.StartedAt,
                run.CompletedAt);
            await store.UpsertAsync(run, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "RAGAS evaluation run {RunId} failed.", run.RunId);
            run.Status = RagasEvaluationRunStatus.Failed;
            run.Error = exception.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Summary = CreateSummary(cases.Count, run.Cases, cancelledRemaining: 0, run.StartedAt, run.CompletedAt);
            await store.UpsertAsync(run, CancellationToken.None);
        }
    }

    private List<RagasEvaluationDiagnosticDto> AppendJudgeDiagnostics(
        List<RagasEvaluationDiagnosticDto> diagnostics,
        RagasEvaluatorResult evaluatorResult,
        bool includeFullText)
    {
        var value = options.Value;
        if (value.PersistJudgePrompts)
        {
            diagnostics.Add(CreateJudgeSnapshotDiagnostic(
                "judge_prompt",
                "Judge prompt snapshot.",
                evaluatorResult.Prompt,
                includeFullText));
        }

        if (value.PersistJudgeResponses)
        {
            diagnostics.Add(CreateJudgeSnapshotDiagnostic(
                "judge_response",
                "Judge raw response snapshot.",
                evaluatorResult.RawResponse,
                includeFullText));
        }

        return diagnostics;
    }

    private RagasEvaluationDiagnosticDto CreateJudgeSnapshotDiagnostic(
        string code,
        string message,
        string value,
        bool includeFullText)
    {
        var snapshot = snapshotter.Snapshot(SanitizeJudgeText(value), includeFullText);
        var details = new Dictionary<string, string>
        {
            ["preview"] = snapshot.Preview,
            ["hash"] = snapshot.Hash
        };

        if (snapshot.Text is not null)
        {
            details["text"] = snapshot.Text;
        }

        return new RagasEvaluationDiagnosticDto
        {
            Code = code,
            Message = message,
            Details = details
        };
    }

    private string SanitizeJudgeText(string value)
    {
        var sanitized = value;
        foreach (var secret in secretProvider.GetSecretValues())
        {
            sanitized = sanitized.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return sanitized;
    }

    private RagasEvaluationCaseResultDto CreateSucceededCaseResult(
        RagasDatasetCase dataSetCase,
        string answer,
        IReadOnlyList<RagasRetrievedContext> contexts,
        RagasMetricSet metrics,
        IReadOnlyList<RagasEvaluationDiagnosticDto> diagnostics,
        bool includeFullText)
    {
        var result = CreateBaseCaseResult(
            dataSetCase,
            answer,
            contexts,
            RagasEvaluationCaseStatus.Succeeded,
            includeFullText);
        result.Metrics = ToDto(metrics);
        result.Reasons =
        [
            new RagasEvaluationMetricReasonDto
            {
                Metric = "faithfulness",
                Reason = SanitizeJudgeText(metrics.Faithfulness.Reason)
            },
            new RagasEvaluationMetricReasonDto
            {
                Metric = "answer_relevance",
                Reason = SanitizeJudgeText(metrics.AnswerRelevance.Reason)
            },
            new RagasEvaluationMetricReasonDto
            {
                Metric = "context_recall",
                Reason = SanitizeJudgeText(metrics.ContextRecall.Reason)
            },
            new RagasEvaluationMetricReasonDto
            {
                Metric = "context_precision",
                Reason = SanitizeJudgeText(metrics.ContextPrecision.Reason)
            }
        ];
        result.Diagnostics = diagnostics.ToList();

        return result;
    }

    private RagasEvaluationCaseResultDto CreateFailedCaseResult(
        RagasDatasetCase dataSetCase,
        string answer,
        IReadOnlyList<RagasRetrievedContext> contexts,
        IReadOnlyList<RagasEvaluationDiagnosticDto> diagnostics,
        bool includeFullText)
    {
        var result = CreateBaseCaseResult(
            dataSetCase,
            answer,
            contexts,
            RagasEvaluationCaseStatus.Failed,
            includeFullText);
        result.Diagnostics = diagnostics.ToList();

        return result;
    }

    private RagasEvaluationCaseResultDto CreateBaseCaseResult(
        RagasDatasetCase dataSetCase,
        string answer,
        IReadOnlyList<RagasRetrievedContext> contexts,
        RagasEvaluationCaseStatus status,
        bool includeFullText)
    {
        var question = snapshotter.Snapshot(dataSetCase.Question, includeFullText: false);
        var groundTruth = snapshotter.Snapshot(dataSetCase.GroundTruth, includeFullText: false);
        var answerSnapshot = snapshotter.Snapshot(answer, includeFullText);

        return new RagasEvaluationCaseResultDto
        {
            CaseName = dataSetCase.CaseName,
            QuestionPreview = question.Preview,
            GroundTruthPreview = groundTruth.Preview,
            Status = status.ToString(),
            AnswerPreview = answerSnapshot.Preview,
            AnswerHash = answerSnapshot.Hash,
            AnswerText = answerSnapshot.Text,
            Contexts = contexts
                .Select(context => ToContextDto(context, includeFullText))
                .ToList()
        };
    }

    private RagasEvaluationContextSnapshotDto ToContextDto(
        RagasRetrievedContext context,
        bool includeFullText)
    {
        var snapshot = snapshotter.Snapshot(context.Content, includeFullText);

        return new RagasEvaluationContextSnapshotDto
        {
            Preview = snapshot.Preview,
            Hash = snapshot.Hash,
            Text = snapshot.Text,
            ChunkId = context.ChunkId,
            FilePath = context.FilePath,
            ReferenceId = context.ReferenceId
        };
    }

    private static RagasEvaluationMetricsDto ToDto(RagasMetricSet metrics)
    {
        return new RagasEvaluationMetricsDto
        {
            Faithfulness = metrics.Faithfulness.Score,
            AnswerRelevance = metrics.AnswerRelevance.Score,
            ContextRecall = metrics.ContextRecall.Score,
            ContextPrecision = metrics.ContextPrecision.Score,
            RagasScore = metrics.RagasScore
        };
    }

    private static RagasEvaluationSummaryDto CreateSummary(
        int total,
        IReadOnlyList<RagasEvaluationCaseResultDto> results,
        int cancelledRemaining,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt)
    {
        var succeeded = results
            .Where(result => result.Status == RagasEvaluationCaseStatus.Succeeded.ToString())
            .ToArray();
        var failed = results
            .Where(result => result.Status == RagasEvaluationCaseStatus.Failed.ToString())
            .ToArray();
        var elapsed = startedAt is not null && completedAt is not null
            ? (completedAt.Value - startedAt.Value).TotalSeconds
            : (double?)null;
        var scores = succeeded
            .Select(result => result.Metrics.RagasScore)
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToArray();

        return new RagasEvaluationSummaryDto
        {
            Total = total,
            Succeeded = succeeded.Length,
            Failed = failed.Length,
            Cancelled = results.Count(result => result.Status == RagasEvaluationCaseStatus.Cancelled.ToString())
                + cancelledRemaining,
            AverageMetrics = AverageMetrics(succeeded),
            SuccessRate = total > 0 ? (double)succeeded.Length / total : null,
            ElapsedTimeSeconds = elapsed,
            AverageSecondsPerCase = elapsed is not null && total > 0 ? elapsed.Value / total : null,
            MinRagasScore = scores.Length > 0 ? scores.Min() : null,
            MaxRagasScore = scores.Length > 0 ? scores.Max() : null,
            FailureReasons = failed
                .SelectMany(result => result.Diagnostics)
                .GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static RagasEvaluationMetricsDto AverageMetrics(IReadOnlyList<RagasEvaluationCaseResultDto> succeeded)
    {
        if (succeeded.Count == 0)
        {
            return new RagasEvaluationMetricsDto();
        }

        return new RagasEvaluationMetricsDto
        {
            Faithfulness = succeeded.Average(result => result.Metrics.Faithfulness!.Value),
            AnswerRelevance = succeeded.Average(result => result.Metrics.AnswerRelevance!.Value),
            ContextRecall = succeeded.Average(result => result.Metrics.ContextRecall!.Value),
            ContextPrecision = succeeded.Average(result => result.Metrics.ContextPrecision!.Value),
            RagasScore = succeeded.Average(result => result.Metrics.RagasScore!.Value)
        };
    }
}
