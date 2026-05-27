using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services.Evaluation;

internal interface IRagasRagQueryClient
{
    Task<RagasQueryExecutionResult> QueryAsync(
        RagasDatasetCase dataSetCase,
        RagasEvaluationQueryOptions options,
        CancellationToken cancellationToken);
}
