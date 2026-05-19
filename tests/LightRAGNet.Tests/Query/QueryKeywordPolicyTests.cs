using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;

namespace LightRAGNet.Tests.Query;

public sealed class QueryKeywordPolicyTests
{
    [Theory]
    [InlineData(QueryMode.Local)]
    [InlineData(QueryMode.Global)]
    [InlineData(QueryMode.Hybrid)]
    [InlineData(QueryMode.Mix)]
    public void NormalizeForKg_SuppliedKeywords_PassesThroughUnchanged(QueryMode mode)
    {
        var keywords = new KeywordsResult
        {
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["service"]
        };

        var decision = QueryKeywordPolicy.NormalizeForKg("query", keywords, mode);

        decision.ShouldFail.Should().BeFalse();
        decision.Keywords.Should().BeSameAs(keywords);
        decision.Keywords.HighLevelKeywords.Should().Equal("architecture");
        decision.Keywords.LowLevelKeywords.Should().Equal("service");
    }

    [Theory]
    [InlineData(QueryMode.Local)]
    [InlineData(QueryMode.Global)]
    [InlineData(QueryMode.Hybrid)]
    [InlineData(QueryMode.Mix)]
    public void NormalizeForKg_EmptyKeywordsAndShortQuery_UsesOriginalQueryAsLowLevelKeyword(QueryMode mode)
    {
        const string query = "What is LightRAG?";
        var keywords = new KeywordsResult();

        var decision = QueryKeywordPolicy.NormalizeForKg(query, keywords, mode);

        decision.ShouldFail.Should().BeFalse();
        decision.Keywords.HighLevelKeywords.Should().BeEmpty();
        decision.Keywords.LowLevelKeywords.Should().Equal(query);
    }

    [Theory]
    [InlineData(QueryMode.Local)]
    [InlineData(QueryMode.Global)]
    [InlineData(QueryMode.Hybrid)]
    [InlineData(QueryMode.Mix)]
    public void NormalizeForKg_EmptyKeywordsAndQueryLengthExactly50_Fails(QueryMode mode)
    {
        var query = new string('a', 50);
        var keywords = new KeywordsResult();

        var decision = QueryKeywordPolicy.NormalizeForKg(query, keywords, mode);

        decision.ShouldFail.Should().BeTrue();
        decision.Keywords.HighLevelKeywords.Should().BeEmpty();
        decision.Keywords.LowLevelKeywords.Should().BeEmpty();
    }

    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public void NormalizeForKg_NonKnowledgeGraphMode_ThrowsArgumentException(QueryMode mode)
    {
        var act = () => QueryKeywordPolicy.NormalizeForKg("query", new KeywordsResult(), mode);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage($"Query mode '{mode}' is not a KG query mode.");
    }
}
