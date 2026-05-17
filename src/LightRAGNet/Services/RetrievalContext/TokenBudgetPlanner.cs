using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class TokenBudgetPlanner(ITokenizer tokenizer)
{
    public TokenBudgetPlan Plan(
        int maxTotalTokens,
        string systemPrompt,
        string query,
        string knowledgeGraphContext,
        int reservedOutputTokens,
        int safetyBufferTokens)
    {
        var systemTokens = tokenizer.CountTokens(systemPrompt);
        var queryTokens = tokenizer.CountTokens(query);
        var knowledgeGraphTokens = tokenizer.CountTokens(knowledgeGraphContext);
        var availableChunkTokens =
            maxTotalTokens
            - systemTokens
            - queryTokens
            - knowledgeGraphTokens
            - reservedOutputTokens
            - safetyBufferTokens;

        return new TokenBudgetPlan(
            maxTotalTokens,
            systemTokens,
            queryTokens,
            knowledgeGraphTokens,
            reservedOutputTokens,
            safetyBufferTokens,
            Math.Max(0, availableChunkTokens));
    }
}
