using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Example;

namespace LightRAGNet.Tests.Example;

public sealed class QueryCommandOptionsTests
{
    [Fact]
    public void Parse_KeepsLegacyQuestionOnlyCommand()
    {
        var options = QueryCommandOptions.Parse("tbReviseRatio 973E 983E");

        options.Question.Should().Be("tbReviseRatio 973E 983E");
        options.QueryParam.Mode.Should().Be(QueryMode.Mix);
        options.QueryParam.Stream.Should().BeTrue();
        options.QueryParam.TopK.Should().Be(10);
        options.QueryParam.ChunkTopK.Should().Be(5);
        options.QueryParam.EnableRerank.Should().BeTrue();
    }

    [Fact]
    public void Parse_MapsAllSupportedQueryOptions()
    {
        var options = QueryCommandOptions.Parse(
            "--mode naive --stream false --references true --response \"Bullet Points\" " +
            "--top-k 12 --chunk-top-k 6 --rerank false --hl system,workflow --ll queue，status " +
            "--context-only tbReviseRatio 973E 983E");

        options.Question.Should().Be("tbReviseRatio 973E 983E");
        options.QueryParam.Mode.Should().Be(QueryMode.Naive);
        options.QueryParam.Stream.Should().BeFalse();
        options.QueryParam.IncludeReferences.Should().BeTrue();
        options.QueryParam.ResponseType.Should().Be("Bullet Points");
        options.QueryParam.TopK.Should().Be(12);
        options.QueryParam.ChunkTopK.Should().Be(6);
        options.QueryParam.EnableRerank.Should().BeFalse();
        options.QueryParam.HighLevelKeywords.Should().Equal("system", "workflow");
        options.QueryParam.LowLevelKeywords.Should().Equal("queue", "status");
        options.QueryParam.OnlyNeedContext.Should().BeTrue();
        options.QueryParam.OnlyNeedPrompt.Should().BeFalse();
    }

    [Fact]
    public void Parse_PromptOnlyClearsContextOnlyWhenBothAreSpecified()
    {
        var options = QueryCommandOptions.Parse("--context-only --prompt-only --mode bypass hello");

        options.QueryParam.Mode.Should().Be(QueryMode.Bypass);
        options.QueryParam.OnlyNeedContext.Should().BeFalse();
        options.QueryParam.OnlyNeedPrompt.Should().BeTrue();
        options.Question.Should().Be("hello");
    }
}
