using FluentAssertions;
using LightRAGNet.Services.KnowledgeGraphMerge;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class SummaryPromptBuilderTests
{
    [Fact]
    public void Build_UsesJsonLinesForDescriptions()
    {
        var prompt = SummaryPromptBuilder.Build(
            "entity",
            "Alpha",
            ["第一段", "第二段"],
            summaryLengthRecommended: 50);

        prompt.Should().Contain("{\"Description\":\"第一段\"}\n{\"Description\":\"第二段\"}");
        prompt.Should().NotContain("[\n");
        prompt.Should().Contain("entity Name: Alpha");
    }
}
