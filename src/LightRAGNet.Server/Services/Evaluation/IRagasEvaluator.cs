namespace LightRAGNet.Server.Services.Evaluation;

internal interface IRagasEvaluator
{
    Task<RagasEvaluatorResult> EvaluateAsync(
        RagasEvaluationCaseInput input,
        CancellationToken cancellationToken);
}
