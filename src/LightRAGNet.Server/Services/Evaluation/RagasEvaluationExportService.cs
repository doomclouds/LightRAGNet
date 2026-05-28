using System.Globalization;
using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Utils;
using LightRAGNet.Share.Models;

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
            run = GetJsonRun(run)
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
        builder.Append("run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash\r\n");

        foreach (var item in run.Cases)
        {
            builder.AppendJoin(
                ',',
                CsvText(run.RunId),
                CsvText(item.CaseName),
                CsvText(item.Status),
                CsvRaw(Number(item.Metrics.Faithfulness)),
                CsvRaw(Number(item.Metrics.AnswerRelevance)),
                CsvRaw(Number(item.Metrics.ContextRecall)),
                CsvRaw(Number(item.Metrics.ContextPrecision)),
                CsvRaw(Number(item.Metrics.RagasScore)),
                CsvRaw(item.Contexts.Count.ToString(CultureInfo.InvariantCulture)),
                CsvText(item.AnswerHash));
            builder.Append("\r\n");
        }

        return new RagasEvaluationExportResult(
            builder.ToString(),
            "text/csv; charset=utf-8",
            $"{run.RunId}.csv");
    }

    private static string Number(double? value) =>
        value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static object GetJsonRun(RagasEvaluationRunRecord run)
    {
        if (run.Request.IncludeFullText)
        {
            return run;
        }

        return new
        {
            run.RunId,
            run.Status,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.Request,
            run.Summary,
            Cases = run.Cases.Select(RedactCaseFullText).ToList(),
            run.Diagnostics,
            run.Error
        };
    }

    private static RagasEvaluationCaseResultDto RedactCaseFullText(RagasEvaluationCaseResultDto item)
    {
        return new RagasEvaluationCaseResultDto
        {
            CaseName = item.CaseName,
            QuestionPreview = item.QuestionPreview,
            GroundTruthPreview = item.GroundTruthPreview,
            Status = item.Status,
            Metrics = item.Metrics,
            Reasons = item.Reasons,
            AnswerPreview = item.AnswerPreview,
            AnswerHash = item.AnswerHash,
            AnswerText = null,
            Contexts = item.Contexts.Select(RedactContextFullText).ToList(),
            Diagnostics = item.Diagnostics
        };
    }

    private static RagasEvaluationContextSnapshotDto RedactContextFullText(RagasEvaluationContextSnapshotDto item)
    {
        return new RagasEvaluationContextSnapshotDto
        {
            Preview = item.Preview,
            Hash = item.Hash,
            Text = null,
            ChunkId = item.ChunkId,
            FilePath = item.FilePath,
            ReferenceId = item.ReferenceId
        };
    }

    private static string CsvText(string value) => CsvRaw(NeutralizeSpreadsheetFormula(value));

    private static string NeutralizeSpreadsheetFormula(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            ? $"'{value}"
            : value;
    }

    private static string CsvRaw(string value)
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
