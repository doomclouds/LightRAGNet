using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.RetrievalContext;

/// <summary>
/// Knowledge graph search strategy factory
/// </summary>
internal class KGSearchStrategyFactory
{
    private readonly Dictionary<QueryMode, IKGSearchStrategy> _strategies;

    public KGSearchStrategyFactory(
        IVectorStore vectorStore,
        IGraphStore graphStore,
        ILoggerFactory loggerFactory)
    {
        var localStrategy = new LocalSearchStrategy(
            vectorStore,
            graphStore,
            loggerFactory.CreateLogger<LocalSearchStrategy>());

        var globalStrategy = new GlobalSearchStrategy(
            vectorStore,
            graphStore,
            loggerFactory.CreateLogger<GlobalSearchStrategy>());

        var mixStrategy = new MixSearchStrategy(
            vectorStore,
            graphStore,
            loggerFactory.CreateLogger<MixSearchStrategy>(),
            localStrategy,
            globalStrategy);

        _strategies = new Dictionary<QueryMode, IKGSearchStrategy>
        {
            [QueryMode.Local] = localStrategy,
            [QueryMode.Global] = globalStrategy,
            [QueryMode.Mix] = mixStrategy,
            [QueryMode.Hybrid] = mixStrategy
        };
    }

    public IKGSearchStrategy GetStrategy(QueryMode mode)
    {
        return _strategies.TryGetValue(mode, out var strategy)
            ? strategy
            : _strategies[QueryMode.Mix]; // Default to Mix strategy
    }
}

