namespace LightRAGNet.Services.RetrievalContext;

internal sealed record TokenBudgetPlan(
    int MaxTotalTokens,
    int SystemTokens,
    int QueryTokens,
    int KnowledgeGraphTokens,
    int ReservedOutputTokens,
    int SafetyBufferTokens,
    int AvailableChunkTokens);
