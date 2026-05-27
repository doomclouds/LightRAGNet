namespace LightRAGNet.Server.Services.Evaluation;

public sealed class RagasEvaluationOptions
{
    public bool Enabled { get; set; }
    public string AdminToken { get; set; } = string.Empty;
    public string EvaluatorModel { get; set; } = "deepseek-v4-flash";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxConcurrentCases { get; set; } = 1;
    public int MaxCasesPerRun { get; set; } = 5;
    public bool AllowPersistFullText { get; set; }
    public int PreviewMaxChars { get; set; } = 500;
    public bool PersistJudgePrompts { get; set; } = true;
    public bool PersistJudgeResponses { get; set; } = true;
}
