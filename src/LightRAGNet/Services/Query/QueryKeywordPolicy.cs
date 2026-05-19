using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.Query;

internal sealed record QueryKeywordDecision(KeywordsResult Keywords, bool ShouldFail);

internal static class QueryKeywordPolicy
{
    public static QueryKeywordDecision NormalizeForKg(
        string query,
        KeywordsResult keywords,
        QueryMode mode)
    {
        if (!IsKnowledgeGraphMode(mode))
        {
            throw new ArgumentException($"Query mode '{mode}' is not a KG query mode.");
        }

        if (HasAnyKeyword(keywords))
        {
            return new QueryKeywordDecision(keywords, ShouldFail: false);
        }

        if (query.Length < 50)
        {
            return new QueryKeywordDecision(
                new KeywordsResult
                {
                    LowLevelKeywords = [query]
                },
                ShouldFail: false);
        }

        return new QueryKeywordDecision(keywords, ShouldFail: true);
    }

    private static bool HasAnyKeyword(KeywordsResult keywords)
    {
        return keywords.HighLevelKeywords.Any(keyword => !string.IsNullOrWhiteSpace(keyword))
            || keywords.LowLevelKeywords.Any(keyword => !string.IsNullOrWhiteSpace(keyword));
    }

    private static bool IsKnowledgeGraphMode(QueryMode mode)
    {
        return mode is QueryMode.Local or QueryMode.Global or QueryMode.Hybrid or QueryMode.Mix;
    }
}
