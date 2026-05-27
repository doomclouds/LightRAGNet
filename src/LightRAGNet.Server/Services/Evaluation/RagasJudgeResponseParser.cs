using System.Text.Json;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasJudgeResponseParser
{
    public RagasJudgeParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RagasJudgeParseResult.Failed("invalid_json", "Judge response was empty or not valid JSON.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return RagasJudgeParseResult.Failed("invalid_json", "Judge response was not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RagasJudgeParseResult.Failed(
                    "missing_metric",
                    "Judge response root was missing metric objects.");
            }

            if (!TryReadMetric(root, "faithfulness", out var faithfulness, out var failure) ||
                !TryReadMetric(root, "answer_relevance", out var answerRelevance, out failure) ||
                !TryReadMetric(root, "context_recall", out var contextRecall, out failure) ||
                !TryReadMetric(root, "context_precision", out var contextPrecision, out failure))
            {
                return failure!;
            }

            return RagasJudgeParseResult.Succeeded(new RagasMetricSet(
                faithfulness!,
                answerRelevance!,
                contextRecall!,
                contextPrecision!));
        }
    }

    private static bool TryReadMetric(
        JsonElement root,
        string jsonName,
        out RagasMetricScore? metric,
        out RagasJudgeParseResult? failure)
    {
        metric = null;
        failure = null;

        if (!root.TryGetProperty(jsonName, out var metricElement) ||
            metricElement.ValueKind != JsonValueKind.Object)
        {
            failure = RagasJudgeParseResult.Failed(
                "missing_metric",
                $"Judge response metric '{jsonName}' was missing or not an object.");
            return false;
        }

        if (!metricElement.TryGetProperty("score", out var scoreElement))
        {
            failure = RagasJudgeParseResult.Failed(
                "missing_score",
                $"Judge response metric '{jsonName}' was missing a score.");
            return false;
        }

        if (scoreElement.ValueKind != JsonValueKind.Number ||
            !scoreElement.TryGetDouble(out var score) ||
            score < 0 ||
            score > 1)
        {
            failure = RagasJudgeParseResult.Failed(
                "invalid_score",
                $"Judge response metric '{jsonName}' score must be a number between 0 and 1.");
            return false;
        }

        if (!metricElement.TryGetProperty("reason", out var reasonElement) ||
            reasonElement.ValueKind != JsonValueKind.String)
        {
            failure = RagasJudgeParseResult.Failed(
                "missing_reason",
                $"Judge response metric '{jsonName}' was missing a reason.");
            return false;
        }

        var reason = reasonElement.GetString();
        if (string.IsNullOrWhiteSpace(reason))
        {
            failure = RagasJudgeParseResult.Failed(
                "missing_reason",
                $"Judge response metric '{jsonName}' reason was empty.");
            return false;
        }

        metric = new RagasMetricScore(score, reason);
        return true;
    }
}
