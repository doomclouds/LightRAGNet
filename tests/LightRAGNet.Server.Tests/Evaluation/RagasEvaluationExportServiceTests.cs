using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationExportServiceTests
{
    [Fact]
    public void ExportJson_ReturnsRunWithoutAddingHiddenText()
    {
        var service = new RagasEvaluationExportService();
        var run = CreateRun(includeFullText: false);
        run.Cases[0].AnswerText = "secret-key";
        run.Cases[0].Contexts[0].Text = "secret-key";
        run.Diagnostics.Add(CreateDiagnostic());
        run.Cases[0].Diagnostics.Add(CreateDiagnostic());

        var result = service.ExportJson(run);

        result.ContentType.Should().Be("application/json; charset=utf-8");
        result.FileName.Should().EndWith(".json");
        result.Content.Should().Contain(run.RunId);
        result.Content.Should().NotContain("secret-key");

        using var document = JsonDocument.Parse(result.Content);
        var root = document.RootElement;
        root.GetProperty("format").GetString().Should().Be("json");
        var exportedRun = root.GetProperty("run");
        exportedRun.GetProperty("runId").GetString().Should().Be(run.RunId);
        exportedRun.GetProperty("request").GetProperty("includeFullText").GetBoolean().Should().BeFalse();

        var exportedCase = exportedRun.GetProperty("cases")[0];
        exportedCase.GetProperty("answerText").ValueKind.Should().Be(JsonValueKind.Null);
        exportedCase.GetProperty("contexts")[0].GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);

        var runDiagnosticDetails = exportedRun.GetProperty("diagnostics")[0].GetProperty("details");
        runDiagnosticDetails.TryGetProperty("text", out _).Should().BeFalse();
        runDiagnosticDetails.GetProperty("preview").GetString().Should().Be("safe preview");
        runDiagnosticDetails.GetProperty("hash").GetString().Should().Be("safe hash");

        var caseDiagnosticDetails = exportedCase.GetProperty("diagnostics")[0].GetProperty("details");
        caseDiagnosticDetails.TryGetProperty("text", out _).Should().BeFalse();
        caseDiagnosticDetails.GetProperty("preview").GetString().Should().Be("safe preview");
        caseDiagnosticDetails.GetProperty("hash").GetString().Should().Be("safe hash");

        run.Diagnostics[0].Details.Should().ContainKey("text").WhoseValue.Should().Be("secret-key");
        run.Cases[0].Diagnostics[0].Details.Should().ContainKey("text").WhoseValue.Should().Be("secret-key");
    }

    [Fact]
    public void ExportCsv_EscapesValuesAndUsesSafeColumns()
    {
        var service = new RagasEvaluationExportService();
        var run = CreateRunWithCaseName("case, \"quoted\"");

        var result = service.ExportCsv(run);

        result.ContentType.Should().Be("text/csv; charset=utf-8");
        result.Content.Should().Contain("run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash");
        result.Content.Should().Contain("\"case, \"\"quoted\"\"\"");
        result.Content.Should().Contain("\r\n");
        result.Content.Should().NotContain("AnswerText");
        result.Content.Should().NotContain("secret-key");
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+formula", "'+formula")]
    [InlineData("-formula", "'-formula")]
    [InlineData("@formula", "'@formula")]
    [InlineData("\tformula", "'\tformula")]
    [InlineData("\rformula", "\"'\rformula\"")]
    [InlineData("\nformula", "\"'\nformula\"")]
    public void ExportCsv_NeutralizesFormulaLikeTextColumns(string caseName, string expectedCell)
    {
        var service = new RagasEvaluationExportService();
        var run = CreateRunWithCaseName(caseName);

        var result = service.ExportCsv(run);

        result.Content.Should().Contain(expectedCell);
    }

    private static RagasEvaluationRunRecord CreateRun(bool includeFullText)
    {
        return new RagasEvaluationRunRecord
        {
            RunId = "ragas-run-1",
            Status = RagasEvaluationRunStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero),
            Request = new RagasEvaluationRequestSnapshot
            {
                IncludeFullText = includeFullText,
                MaxCases = 1,
                PreviewMaxChars = 120
            },
            Summary = new RagasEvaluationSummaryDto
            {
                Total = 1,
                Succeeded = 1,
                AverageMetrics = new RagasEvaluationMetricsDto
                {
                    Faithfulness = 0.9,
                    AnswerRelevance = 0.8,
                    ContextRecall = 0.7,
                    ContextPrecision = 0.6,
                    RagasScore = 0.75
                }
            },
            Cases =
            [
                new RagasEvaluationCaseResultDto
                {
                    CaseName = "case-1",
                    Status = RagasEvaluationCaseStatus.Succeeded.ToString(),
                    QuestionPreview = "question preview",
                    GroundTruthPreview = "ground truth preview",
                    AnswerPreview = "answer preview",
                    AnswerHash = "answer-hash",
                    AnswerText = includeFullText ? "secret-key" : null,
                    Metrics = new RagasEvaluationMetricsDto
                    {
                        Faithfulness = 0.9,
                        AnswerRelevance = 0.8,
                        ContextRecall = 0.7,
                        ContextPrecision = 0.6,
                        RagasScore = 0.75
                    },
                    Contexts =
                    [
                        new RagasEvaluationContextSnapshotDto
                        {
                            Preview = "context preview",
                            Hash = "context-hash",
                            Text = includeFullText ? "secret-key" : null,
                            ChunkId = "chunk-1",
                            FilePath = "docs/file.md",
                            ReferenceId = "ref-1"
                        }
                    ]
                }
            ]
        };
    }

    private static RagasEvaluationRunRecord CreateRunWithCaseName(string caseName)
    {
        var run = CreateRun(includeFullText: true);
        run.Cases[0].CaseName = caseName;

        return run;
    }

    private static RagasEvaluationDiagnosticDto CreateDiagnostic()
    {
        return new RagasEvaluationDiagnosticDto
        {
            Code = "judge_snapshot",
            Message = "judge diagnostic",
            Details = new Dictionary<string, string>
            {
                ["text"] = "secret-key",
                ["preview"] = "safe preview",
                ["hash"] = "safe hash"
            }
        };
    }
}
