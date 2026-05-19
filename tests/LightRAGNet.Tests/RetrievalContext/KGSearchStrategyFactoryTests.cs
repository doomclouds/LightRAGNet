using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class KGSearchStrategyFactoryTests
{
    [Theory]
    [InlineData(QueryMode.Local, typeof(LocalSearchStrategy))]
    [InlineData(QueryMode.Global, typeof(GlobalSearchStrategy))]
    [InlineData(QueryMode.Hybrid, typeof(MixSearchStrategy))]
    [InlineData(QueryMode.Mix, typeof(MixSearchStrategy))]
    public void GetStrategy_KnowledgeGraphMode_ReturnsExpectedStrategy(
        QueryMode mode,
        Type expectedStrategyType)
    {
        var factory = CreateFactory();

        var strategy = factory.GetStrategy(mode);

        strategy.Should().BeOfType(expectedStrategyType);
    }

    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public void GetStrategy_NonKnowledgeGraphMode_ThrowsNotSupported(QueryMode mode)
    {
        var factory = CreateFactory();

        var act = () => factory.GetStrategy(mode);

        act.Should()
            .Throw<NotSupportedException>()
            .WithMessage($"Query mode '{mode}' is not a knowledge graph search mode.");
    }

    private static KGSearchStrategyFactory CreateFactory()
    {
        return new KGSearchStrategyFactory(
            new InMemoryVectorStore(),
            new InMemoryGraphStore(),
            NullLoggerFactory.Instance);
    }
}
