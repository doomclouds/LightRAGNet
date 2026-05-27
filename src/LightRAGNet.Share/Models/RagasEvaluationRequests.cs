using LightRAGNet.Core.Models;

namespace LightRAGNet.Share.Models;

public sealed class CreateRagasEvaluationRunRequest
{
    public List<string> CaseNames { get; set; } = [];
    public int? MaxCases { get; set; }
    public bool IncludeFullText { get; set; }
    public RagasEvaluationQueryOptions Query { get; set; } = new();
}

public sealed class RagasEvaluationQueryOptions
{
    public QueryMode Mode { get; set; } = QueryMode.Mix;
    public int TopK { get; set; } = 40;
    public int ChunkTopK { get; set; } = 20;
    public bool EnableRerank { get; set; } = true;
}

public sealed class CreateRagasEvaluationRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class RagasEvaluationRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EvaluationType { get; set; } = "ragas-compatible";
    public string EvaluatorBackend { get; set; } = "dotnet-native";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RagasEvaluationRequestSnapshot Request { get; set; } = new();
    public RagasEvaluationSummaryDto Summary { get; set; } = new();
    public List<RagasEvaluationCaseResultDto> Cases { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class RagasEvaluationRequestSnapshot
{
    public List<string> CaseNames { get; set; } = [];
    public int MaxCases { get; set; }
    public bool IncludeFullText { get; set; }
    public int PreviewMaxChars { get; set; }
    public RagasEvaluationQueryOptions Query { get; set; } = new();
}

public sealed class RagasEvaluationSummaryDto
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public RagasEvaluationMetricsDto AverageMetrics { get; set; } = new();
}

public sealed class RagasEvaluationMetricsDto
{
    public double? Faithfulness { get; set; }
    public double? AnswerRelevance { get; set; }
    public double? ContextRecall { get; set; }
    public double? ContextPrecision { get; set; }
    public double? RagasScore { get; set; }
}

public sealed class RagasEvaluationCaseResultDto
{
    public string CaseName { get; set; } = string.Empty;
    public string QuestionPreview { get; set; } = string.Empty;
    public string GroundTruthPreview { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public RagasEvaluationMetricsDto Metrics { get; set; } = new();
    public List<RagasEvaluationMetricReasonDto> Reasons { get; set; } = [];
    public string AnswerPreview { get; set; } = string.Empty;
    public string AnswerHash { get; set; } = string.Empty;
    public string? AnswerText { get; set; }
    public List<RagasEvaluationContextSnapshotDto> Contexts { get; set; } = [];
    public List<RagasEvaluationDiagnosticDto> Diagnostics { get; set; } = [];
}

public sealed class RagasEvaluationMetricReasonDto
{
    public string Metric { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RagasEvaluationContextSnapshotDto
{
    public string Preview { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
}

public sealed class RagasEvaluationDiagnosticDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Details { get; set; } = [];
}
