using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasJudgeResponseParserTests
{
    [Fact]
    public void Parse_WhenJsonContainsAllMetrics_ReturnsScoresReasonsAndAverageRagasScore()
    {
        var parser = new RagasJudgeResponseParser();

        var result = parser.Parse(
            """
            {
              "faithfulness": {
                "score": 0.8,
                "reason": "The answer is grounded in the supplied context."
              },
              "answer_relevance": {
                "score": 0.9,
                "reason": "The answer directly addresses the question."
              },
              "context_recall": {
                "score": 0.7,
                "reason": "Most required context was retrieved."
              },
              "context_precision": {
                "score": 0.6,
                "reason": "Some retrieved context was extra but useful."
              }
            }
            """);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Metrics.Should().NotBeNull();
        result.Metrics!.Faithfulness.Should().Be(new RagasMetricScore(
            0.8,
            "The answer is grounded in the supplied context."));
        result.Metrics.AnswerRelevance.Should().Be(new RagasMetricScore(
            0.9,
            "The answer directly addresses the question."));
        result.Metrics.ContextRecall.Should().Be(new RagasMetricScore(
            0.7,
            "Most required context was retrieved."));
        result.Metrics.ContextPrecision.Should().Be(new RagasMetricScore(
            0.6,
            "Some retrieved context was extra but useful."));
        result.Metrics.RagasScore.Should().BeApproximately(0.75, 0.000001);
    }

    [Fact]
    public void Parse_WhenJsonIsInvalid_ReturnsInvalidJsonFailure()
    {
        var result = new RagasJudgeResponseParser().Parse("{ not-json");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_json");
        result.Metrics.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenMetricIsMissing_ReturnsMissingMetricFailure()
    {
        var result = new RagasJudgeResponseParser().Parse(
            """
            {
              "faithfulness": { "score": 0.8, "reason": "Grounded." },
              "answer_relevance": { "score": 0.9, "reason": "Relevant." },
              "context_recall": { "score": 0.7, "reason": "Recovered." }
            }
            """);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing_metric");
        result.Metrics.Should().BeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"not object\"")]
    public void Parse_WhenRootIsNotAnObject_ReturnsMissingMetricFailureWithoutThrowing(string json)
    {
        var parser = new RagasJudgeResponseParser();

        var act = () => parser.Parse(json);

        var result = act.Should().NotThrow().Subject;
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing_metric");
        result.Metrics.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "faithfulness": null }""")]
    [InlineData("""{ "faithfulness": 0.8 }""")]
    public void Parse_WhenMetricIsNotAnObject_ReturnsMissingMetricFailure(string faithfulnessJson)
    {
        var result = new RagasJudgeResponseParser().Parse(WithFaithfulness(faithfulnessJson));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing_metric");
        result.Metrics.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenScoreIsMissing_ReturnsMissingScoreFailure()
    {
        var result = new RagasJudgeResponseParser().Parse(WithFaithfulness(
            """{ "faithfulness": { "reason": "Grounded." } }"""));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing_score");
        result.Metrics.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "faithfulness": { "score": 1.1, "reason": "Too high." } }""")]
    [InlineData("""{ "faithfulness": { "score": -0.1, "reason": "Too low." } }""")]
    [InlineData("""{ "faithfulness": { "score": "0.8", "reason": "Not numeric." } }""")]
    public void Parse_WhenScoreIsInvalid_ReturnsInvalidScoreFailure(string faithfulnessJson)
    {
        var result = new RagasJudgeResponseParser().Parse(WithFaithfulness(faithfulnessJson));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_score");
        result.Metrics.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "faithfulness": { "score": 0.8 } }""")]
    [InlineData("""{ "faithfulness": { "score": 0.8, "reason": "" } }""")]
    [InlineData("""{ "faithfulness": { "score": 0.8, "reason": "   " } }""")]
    public void Parse_WhenReasonIsMissingOrBlank_ReturnsMissingReasonFailure(string faithfulnessJson)
    {
        var result = new RagasJudgeResponseParser().Parse(WithFaithfulness(faithfulnessJson));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing_reason");
        result.Metrics.Should().BeNull();
    }

    private static string WithFaithfulness(string faithfulnessJson)
    {
        return $$"""
                 {
                   "faithfulness": {{ReadFaithfulnessMetric(faithfulnessJson)}},
                   "answer_relevance": { "score": 0.9, "reason": "Relevant." },
                   "context_recall": { "score": 0.7, "reason": "Recovered." },
                   "context_precision": { "score": 0.6, "reason": "Precise." }
                 }
                 """;
    }

    private static string ReadFaithfulnessMetric(string faithfulnessJson)
    {
        using var document = JsonDocument.Parse(faithfulnessJson);
        return document.RootElement.GetProperty("faithfulness").GetRawText();
    }
}
