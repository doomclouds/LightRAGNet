using FluentAssertions;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class TokenBudgetPlannerTests
{
    [Fact]
    public void Plan_ReservesSystemQueryKgOutputAndSafetyTokens()
    {
        var planner = new TokenBudgetPlanner(new FakeTokenizer());

        var plan = planner.Plan(
            maxTotalTokens: 100,
            systemPrompt: "one two three",
            query: "four five",
            knowledgeGraphContext: "six seven eight nine",
            reservedOutputTokens: 20,
            safetyBufferTokens: 10);

        plan.MaxTotalTokens.Should().Be(100);
        plan.SystemTokens.Should().Be(3);
        plan.QueryTokens.Should().Be(2);
        plan.KnowledgeGraphTokens.Should().Be(4);
        plan.ReservedOutputTokens.Should().Be(20);
        plan.SafetyBufferTokens.Should().Be(10);
        plan.AvailableChunkTokens.Should().Be(61);
    }

    [Fact]
    public void Plan_ClampsAvailableChunkTokensAtZero()
    {
        var planner = new TokenBudgetPlanner(new FakeTokenizer());

        var plan = planner.Plan(
            maxTotalTokens: 10,
            systemPrompt: "one two three",
            query: "four five",
            knowledgeGraphContext: "six seven eight nine",
            reservedOutputTokens: 20,
            safetyBufferTokens: 10);

        plan.AvailableChunkTokens.Should().Be(0);
    }
}
