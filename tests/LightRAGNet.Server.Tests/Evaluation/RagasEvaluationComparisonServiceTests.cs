using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationComparisonServiceTests
{
    [Fact]
    public void Compare_WhenCurrentScoreHigher_ReportsImproved()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ragasScore: 0.8);
        var current = CreateRun("current", ragasScore: 0.85);

        var result = service.Compare(current, baseline);

        result.Metrics["ragasScore"].Direction.Should().Be("Improved");
        result.Metrics["ragasScore"].Delta.Should().BeApproximately(0.05, 0.0001);
    }

    [Fact]
    public void Compare_WhenCurrentScoreLower_ReportsRegressed()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ragasScore: 0.8);
        var current = CreateRun("current", ragasScore: 0.75);

        var result = service.Compare(current, baseline);

        result.Metrics["ragasScore"].Direction.Should().Be("Regressed");
        result.Metrics["ragasScore"].Delta.Should().BeApproximately(-0.05, 0.0001);
    }

    [Fact]
    public void Compare_WhenCurrentScoreWithinEpsilon_ReportsUnchanged()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ragasScore: 0.8);
        var current = CreateRun("current", ragasScore: 0.80005);

        var result = service.Compare(current, baseline);

        result.Metrics["ragasScore"].Direction.Should().Be("Unchanged");
        result.Metrics["ragasScore"].Delta.Should().BeApproximately(0.00005, 0.000001);
    }

    [Fact]
    public void Compare_WhenMetricMissing_ReportsNotMeasured()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ragasScore: 0.8);
        var current = CreateRun("current", ragasScore: null);

        var result = service.Compare(current, baseline);

        result.Metrics["ragasScore"].Direction.Should().Be("NotMeasured");
        result.Metrics["ragasScore"].Baseline.Should().Be(0.8);
        result.Metrics["ragasScore"].Current.Should().BeNull();
        result.Metrics["ragasScore"].Delta.Should().BeNull();
    }

    [Fact]
    public void Compare_WhenCaseSetsDiffer_AddsDiagnostic()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ["case-a"], ragasScore: 0.8);
        var current = CreateRun("current", ["case-b"], ragasScore: 0.8);

        var result = service.Compare(current, baseline);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "case_set_differs");
        result.CaseCounts.MatchedCases.Should().Be(0);
    }

    private static RagasEvaluationRunRecord CreateRun(string runId, double? ragasScore) =>
        CreateRun(runId, ["case-a"], ragasScore);

    private static RagasEvaluationRunRecord CreateRun(
        string runId,
        IReadOnlyList<string> caseNames,
        double? ragasScore)
    {
        return new RagasEvaluationRunRecord
        {
            RunId = runId,
            Status = RagasEvaluationRunStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero),
            Summary = new RagasEvaluationSummaryDto
            {
                Total = caseNames.Count,
                Succeeded = caseNames.Count,
                AverageMetrics = new RagasEvaluationMetricsDto
                {
                    Faithfulness = 0.9,
                    AnswerRelevance = 0.8,
                    ContextRecall = 0.7,
                    ContextPrecision = 0.6,
                    RagasScore = ragasScore
                }
            },
            Cases = caseNames
                .Select(caseName => new RagasEvaluationCaseResultDto
                {
                    CaseName = caseName,
                    Status = RagasEvaluationCaseStatus.Succeeded.ToString()
                })
                .ToList()
        };
    }
}
