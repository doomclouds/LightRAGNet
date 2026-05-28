using System.Globalization;
using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed record RagasEvaluationExportResult(
    string Content,
    string ContentType,
    string FileName);

internal sealed class RagasEvaluationExportService
{
    public RagasEvaluationExportResult ExportJson(RagasEvaluationRunRecord run)
    {
        var payload = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            format = "json",
            run
        };
        var content = JsonSerializer.Serialize(payload, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);

        return new RagasEvaluationExportResult(
            content,
            "application/json; charset=utf-8",
            $"{run.RunId}.json");
    }

    public RagasEvaluationExportResult ExportCsv(RagasEvaluationRunRecord run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash");

        foreach (var item in run.Cases)
        {
            builder.AppendJoin(
                ',',
                Csv(run.RunId),
                Csv(item.CaseName),
                Csv(item.Status),
                Csv(Number(item.Metrics.Faithfulness)),
                Csv(Number(item.Metrics.AnswerRelevance)),
                Csv(Number(item.Metrics.ContextRecall)),
                Csv(Number(item.Metrics.ContextPrecision)),
                Csv(Number(item.Metrics.RagasScore)),
                Csv(item.Contexts.Count.ToString(CultureInfo.InvariantCulture)),
                Csv(item.AnswerHash));
            builder.AppendLine();
        }

        return new RagasEvaluationExportResult(
            builder.ToString(),
            "text/csv; charset=utf-8",
            $"{run.RunId}.csv");
    }

    private static string Number(double? value) =>
        value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static string Csv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(',', StringComparison.Ordinal)
            || escaped.Contains('\"', StringComparison.Ordinal)
            || escaped.Contains('\n', StringComparison.Ordinal)
            || escaped.Contains('\r', StringComparison.Ordinal)
            ? $"\"{escaped}\""
            : escaped;
    }
}
