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
        var baseline = CreateRun(
            "baseline",
            ragasScore: 0.5,
            faithfulness: 0.61,
            answerRelevance: 0.42,
            contextRecall: 0.32,
            contextPrecision: 0.24);
        var current = CreateRun(
            "current",
            ragasScore: 0.91,
            faithfulness: 0.74,
            answerRelevance: 0.68,
            contextRecall: 0.55,
            contextPrecision: 0.49);

        var result = service.Compare(current, baseline);

        result.Metrics.Keys.Should().BeEquivalentTo(
            ["ragasScore", "faithfulness", "answerRelevance", "contextRecall", "contextPrecision"]);
        AssertMetric(result, "ragasScore", baseline: 0.5, current: 0.91, delta: 0.41, direction: "Improved");
        AssertMetric(result, "faithfulness", baseline: 0.61, current: 0.74, delta: 0.13, direction: "Improved");
        AssertMetric(result, "answerRelevance", baseline: 0.42, current: 0.68, delta: 0.26, direction: "Improved");
        AssertMetric(result, "contextRecall", baseline: 0.32, current: 0.55, delta: 0.23, direction: "Improved");
        AssertMetric(result, "contextPrecision", baseline: 0.24, current: 0.49, delta: 0.25, direction: "Improved");
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

    [Fact]
    public void Compare_WhenCurrentContainsDuplicateCaseNames_CountsAllCasesAndAddsDiagnostic()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ["case-a"], ragasScore: 0.8);
        var current = CreateRun("current", ["case-a", "case-a"], ragasScore: 0.8);

        var result = service.Compare(current, baseline);

        result.CaseCounts.BaselineTotal.Should().Be(1);
        result.CaseCounts.CurrentTotal.Should().Be(2);
        result.CaseCounts.MatchedCases.Should().Be(1);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "case_set_differs");
    }

    [Fact]
    public void Compare_WhenBothRunsContainSameDuplicateCaseNames_MatchesAllCases()
    {
        var service = new RagasEvaluationComparisonService();
        var baseline = CreateRun("baseline", ["case-a", "case-a"], ragasScore: 0.8);
        var current = CreateRun("current", ["case-a", "case-a"], ragasScore: 0.8);

        var result = service.Compare(current, baseline);

        result.CaseCounts.BaselineTotal.Should().Be(2);
        result.CaseCounts.CurrentTotal.Should().Be(2);
        result.CaseCounts.MatchedCases.Should().Be(2);
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "case_set_differs");
    }

    private static RagasEvaluationRunRecord CreateRun(
        string runId,
        double? ragasScore,
        double? faithfulness = 0.9,
        double? answerRelevance = 0.8,
        double? contextRecall = 0.7,
        double? contextPrecision = 0.6) =>
        CreateRun(runId, ["case-a"], ragasScore, faithfulness, answerRelevance, contextRecall, contextPrecision);

    private static RagasEvaluationRunRecord CreateRun(
        string runId,
        IReadOnlyList<string> caseNames,
        double? ragasScore,
        double? faithfulness = 0.9,
        double? answerRelevance = 0.8,
        double? contextRecall = 0.7,
        double? contextPrecision = 0.6)
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
                    Faithfulness = faithfulness,
                    AnswerRelevance = answerRelevance,
                    ContextRecall = contextRecall,
                    ContextPrecision = contextPrecision,
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

    private static void AssertMetric(
        RagasEvaluationComparisonResponse result,
        string metric,
        double baseline,
        double current,
        double delta,
        string direction)
    {
        result.Metrics[metric].Baseline.Should().Be(baseline);
        result.Metrics[metric].Current.Should().Be(current);
        result.Metrics[metric].Delta.Should().BeApproximately(delta, 0.0001);
        result.Metrics[metric].Direction.Should().Be(direction);
    }
}
