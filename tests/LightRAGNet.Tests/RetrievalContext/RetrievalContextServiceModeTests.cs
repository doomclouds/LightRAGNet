using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextServiceModeTests
{
    [Theory]
    [InlineData(QueryMode.Naive)]
    [InlineData(QueryMode.Bypass)]
    public async Task BuildQueryContextAsync_NonKnowledgeGraphMode_ThrowsNotSupported(QueryMode mode)
    {
        var service = CreateService();
        var queryParam = new QueryParam { Mode = mode };

        var act = () => service.BuildQueryContextAsync(
            query: string.Empty,
            keywords: new KeywordsResult(),
            queryParam: queryParam);

        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage($"Query mode '{mode}' is not supported by RetrievalContextService.");
    }

    private static RetrievalContextService CreateService()
    {
        return new RetrievalContextService(
            Substitute.For<IEmbeddingService>(),
            new InMemoryVectorStore(),
            new InMemoryGraphStore(),
            Substitute.For<IRerankService>(),
            new FakeTokenizer(),
            new InMemoryKvStore(),
            Options.Create(new LightRAGOptions()),
            NullLoggerFactory.Instance);
    }
}
