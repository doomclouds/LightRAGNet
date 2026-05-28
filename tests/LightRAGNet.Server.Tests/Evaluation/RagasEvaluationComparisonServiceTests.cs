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
        var current = CreateRun("current", ragasScore: 0.85, faithfulness: 0.95);

        var result = service.Compare(current, baseline);

        result.Metrics.Keys.Should().BeEquivalentTo(
            ["ragasScore", "faithfulness", "answerRelevance", "contextRecall", "contextPrecision"]);
        result.Metrics["ragasScore"].Direction.Should().Be("Improved");
        result.Metrics["ragasScore"].Delta.Should().BeApproximately(0.05, 0.0001);
        result.Metrics["faithfulness"].Baseline.Should().Be(0.9);
        result.Metrics["faithfulness"].Current.Should().Be(0.95);
        result.Metrics["faithfulness"].Delta.Should().BeApproximately(0.05, 0.0001);
        result.Metrics["faithfulness"].Direction.Should().Be("Improved");
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
}
