using System.Globalization;
using FluentAssertions;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Tests.KnowledgeGraphMerge;

public sealed class StorageUpdateStageTests
{
    [Fact]
    public async Task ExecuteAsync_StoresFullRelationPairsUsingGraphSourceReferenceParserOrder()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        try
        {
            var fullEntitiesStore = new InMemoryKvStore();
            var fullRelationsStore = new InMemoryKvStore();
            var stage = new StorageUpdateStage(
                fullEntitiesStore,
                fullRelationsStore,
                NullLogger<StorageUpdateStage>.Instance,
                "doc-a",
                [],
                [
                    new RelationMergeData
                    {
                        SourceId = "I",
                        TargetId = "ı"
                    }
                ],
                []);

            await stage.ExecuteAsync();

            var stored = await fullRelationsStore.GetByIdAsync("doc-a");
            var relationPairs = (List<string[]>)stored!["relation_pairs"];
            var expectedPair = GraphSourceReferenceParser
                .MakeRelationKey("I", "ı")
                .Split(GraphSourceReferenceParser.GraphFieldSep);

            relationPairs.Should().ContainSingle();
            relationPairs[0].Should().Equal(expectedPair);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
