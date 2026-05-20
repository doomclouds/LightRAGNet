using System.Globalization;
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class RelationBuilderTests
{
    [Fact]
    public async Task BuildAsync_UsesGraphSourceReferenceParserRelationKeyForRelationChunks()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        try
        {
            var builder = CreateBuilder();
            var relationChunksStore = new InMemoryKvStore();

            await builder.BuildAsync(
                "I",
                "ı",
                [
                    new Relationship
                    {
                        SourceId = "I",
                        TargetId = "ı",
                        Description = "description",
                        Keywords = "keyword",
                        SourceChunkId = "chunk-a"
                    }
                ],
                relationChunksStore);

            var expectedKey = GraphSourceReferenceParser.MakeRelationKey("I", "ı");
            relationChunksStore.Items.Should().ContainKey(expectedKey);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static RelationBuilder CreateBuilder()
    {
        var llmService = Substitute.For<ILLMService>();
        var tokenizer = Substitute.For<ITokenizer>();
        tokenizer.CountTokens(Arg.Any<string>()).Returns(call => call.Arg<string>().Length);
        var options = Options.Create(new LightRAGOptions
        {
            MaxSourceIdsPerRelation = 10,
            MaxFilePaths = 10,
            SourceIdsLimitMethod = "KEEP"
        });

        return new RelationBuilder(
            new InMemoryGraphStore(),
            new DescriptionMerger(
                llmService,
                tokenizer,
                options,
                new LightRagLlmCacheService(
                    new InMemoryKvStore(),
                    options,
                    new LightRagCacheKeyBuilder(),
                    NullLogger<LightRagLlmCacheService>.Instance),
                NullLogger<DescriptionMerger>.Instance),
            new SourceIdsLimiter(options, NullLogger<SourceIdsLimiter>.Instance),
            options,
            NullLogger<RelationBuilder>.Instance);
    }
}
