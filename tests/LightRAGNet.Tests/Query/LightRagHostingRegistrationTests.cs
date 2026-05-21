using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Hosting;
using LightRAGNet.Services.RetrievalContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class LightRagHostingRegistrationTests
{
    [Fact]
    public void AddLightRag_CanResolveRetrievalContextService()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "lightragnet-tests", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LLM:ApiKey"] = "test-key",
                ["Rerank:ApiKey"] = "test-key",
                ["Embedding:ApiKey"] = "test-key",
                ["Embedding:EmbeddingDimension"] = "2",
                ["Qdrant:Host"] = "localhost",
                ["Qdrant:Port"] = "6334",
                ["Neo4j:Uri"] = "bolt://localhost:7687",
                ["Neo4j:User"] = "neo4j",
                ["Neo4j:Password"] = "password",
                ["LightRAG:WorkingDir"] = workingDir
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLightRAG(configuration);
        services.RemoveAll<ILLMService>();
        services.RemoveAll<IEmbeddingService>();
        services.RemoveAll<IVectorStore>();
        services.RemoveAll<IGraphStore>();
        services.RemoveAll<IRerankService>();
        services.RemoveAll<ITokenizer>();
        services.AddSingleton(Substitute.For<ILLMService>());
        services.AddSingleton(Substitute.For<IEmbeddingService>());
        services.AddSingleton(Substitute.For<IVectorStore>());
        services.AddSingleton(Substitute.For<IGraphStore>());
        services.AddSingleton(Substitute.For<IRerankService>());
        services.AddSingleton<ITokenizer, SingleTokenTokenizer>();

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<RetrievalContextService>();

        service.Should().NotBeNull();
    }

    private sealed class SingleTokenTokenizer : ITokenizer
    {
        public List<int> Encode(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? [] : [1];
        }

        public string Decode(List<int> tokens)
        {
            return tokens.Count == 0 ? string.Empty : "token";
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }
    }
}
