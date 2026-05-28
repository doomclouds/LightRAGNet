using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationComparisonService
{
    private const double Epsilon = 0.0001;

    public RagasEvaluationComparisonResponse Compare(
        RagasEvaluationRunRecord current,
        RagasEvaluationRunRecord baseline)
    {
        var response = new RagasEvaluationComparisonResponse
        {
            RunId = current.RunId,
            BaselineRunId = baseline.RunId,
            Metrics =
            {
                ["ragasScore"] = CompareMetric(current.Summary.AverageMetrics.RagasScore, baseline.Summary.AverageMetrics.RagasScore),
                ["faithfulness"] = CompareMetric(current.Summary.AverageMetrics.Faithfulness, baseline.Summary.AverageMetrics.Faithfulness),
                ["answerRelevance"] = CompareMetric(current.Summary.AverageMetrics.AnswerRelevance, baseline.Summary.AverageMetrics.AnswerRelevance),
                ["contextRecall"] = CompareMetric(current.Summary.AverageMetrics.ContextRecall, baseline.Summary.AverageMetrics.ContextRecall),
                ["contextPrecision"] = CompareMetric(current.Summary.AverageMetrics.ContextPrecision, baseline.Summary.AverageMetrics.ContextPrecision)
            },
            CaseCounts = CountCases(current, baseline)
        };

        if (response.CaseCounts.MatchedCases != response.CaseCounts.BaselineTotal
            || response.CaseCounts.MatchedCases != response.CaseCounts.CurrentTotal)
        {
            response.Diagnostics.Add(new RagasEvaluationDiagnosticDto
            {
                Code = "case_set_differs",
                Message = "Current and baseline runs do not contain the same case set."
            });
        }

        return response;
    }

    private static RagasEvaluationMetricComparisonDto CompareMetric(double? current, double? baseline)
    {
        if (!current.HasValue || !baseline.HasValue)
        {
            return new RagasEvaluationMetricComparisonDto
            {
                Baseline = baseline,
                Current = current,
                Direction = "NotMeasured"
            };
        }

        var delta = current.Value - baseline.Value;
        var direction = delta switch
        {
            > Epsilon => "Improved",
            < -Epsilon => "Regressed",
            _ => "Unchanged"
        };

        return new RagasEvaluationMetricComparisonDto
        {
            Baseline = baseline,
            Current = current,
            Delta = delta,
            Direction = direction
        };
    }

    private static RagasEvaluationCaseCountComparisonDto CountCases(
        RagasEvaluationRunRecord current,
        RagasEvaluationRunRecord baseline)
    {
        var currentCases = current.Cases.Select(item => item.CaseName).ToHashSet(StringComparer.Ordinal);
        var baselineCases = baseline.Cases.Select(item => item.CaseName).ToHashSet(StringComparer.Ordinal);

        return new RagasEvaluationCaseCountComparisonDto
        {
            BaselineTotal = baseline.Cases.Count,
            CurrentTotal = current.Cases.Count,
            MatchedCases = currentCases.Intersect(baselineCases, StringComparer.Ordinal).Count()
        };
    }
}
