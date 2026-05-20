using System.Globalization;
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class EntityExtractionResultParserTests
{
    [Fact]
    public void Parse_ReadsEntitiesAndRelationships()
    {
        const string response = """
                                entity<|#|>"Alpha"<|#|>"Concept Type"<|#|>Alpha is the source concept.
                                relation<|#|>"Alpha"<|#|>"Beta"<|#|>"related, paired"<|#|>Alpha relates to Beta.<|#|>0.75
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 5, maxRelationships: 5);

        result.Entities.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Name = "Alpha",
                Type = "concepttype",
                Description = "Alpha is the source concept."
            });
        result.Relationships.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                SourceId = "Alpha",
                TargetId = "Beta",
                Keywords = "related, paired",
                Description = "Alpha relates to Beta.",
                Weight = 0.75f
            });
    }

    [Fact]
    public void Parse_RemovesThinkTagsBeforeParsing()
    {
        const string response = """
                                <think>internal reasoning</think>
                                entity<|#|>Alpha<|#|>Concept<|#|>Alpha description.
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 5, maxRelationships: 5);

        result.Entities.Should().ContainSingle()
            .Which.Name.Should().Be("Alpha");
    }

    [Fact]
    public void Parse_RemovesOrphanThinkClosePrefixBeforeParsing()
    {
        const string response = """
                                partial hidden reasoning</think>entity<|#|>Alpha<|#|>Concept<|#|>Alpha description.
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 5, maxRelationships: 5);

        result.Entities.Should().ContainSingle()
            .Which.Name.Should().Be("Alpha");
    }

    [Fact]
    public void Parse_DefaultsInvalidQuotedRelationshipWeights()
    {
        const string response = """
                                relation<|#|>Alpha<|#|>Beta<|#|>related<|#|>Alpha relates to Beta.<|#|>'not-a-number'
                                relation<|#|>Gamma<|#|>Delta<|#|>related<|#|>Gamma relates to Delta.<|#|>"not-a-number"
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 5, maxRelationships: 5);

        result.Relationships.Should().HaveCount(2);
        result.Relationships.Should().OnlyContain(relationship => relationship.Weight == 1.0f);
    }

    [Fact]
    public void Parse_ParsesRelationshipWeightUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            const string response = """
                                    relation<|#|>Alpha<|#|>Beta<|#|>related<|#|>Alpha relates to Beta.<|#|>0.75
                                    <|COMPLETE|>
                                    """;

            var result = EntityExtractionResultParser.Parse(response, maxEntities: 5, maxRelationships: 5);

            result.Relationships.Should().ContainSingle()
                .Which.Weight.Should().Be(0.75f);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Parse_AppliesEntityAndRelationshipLimits()
    {
        const string response = """
                                entity<|#|>Alpha<|#|>Concept<|#|>Alpha description.
                                entity<|#|>Beta<|#|>Concept<|#|>Beta description.
                                relation<|#|>Alpha<|#|>Beta<|#|>related<|#|>Alpha relates to Beta.
                                relation<|#|>Beta<|#|>Gamma<|#|>related<|#|>Beta relates to Gamma.
                                <|COMPLETE|>
                                """;

        var result = EntityExtractionResultParser.Parse(response, maxEntities: 1, maxRelationships: 1);

        result.Entities.Should().ContainSingle()
            .Which.Name.Should().Be("Alpha");
        result.Relationships.Should().ContainSingle()
            .Which.SourceId.Should().Be("Alpha");
    }
}
