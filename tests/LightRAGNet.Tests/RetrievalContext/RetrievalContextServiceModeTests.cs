using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.Query;
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
        var tokenizer = new FakeTokenizer();
        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        return new RetrievalContextService(
            Substitute.For<IEmbeddingService>(),
            new InMemoryVectorStore(),
            new InMemoryGraphStore(),
            new RerankCoordinator(
                Substitute.For<IRerankService>(),
                new RerankDocumentChunker(tokenizer, rerankOptions),
                rerankOptions),
            tokenizer,
            new InMemoryKvStore(),
            Options.Create(new LightRAGOptions()),
            NullLoggerFactory.Instance);
    }
}
