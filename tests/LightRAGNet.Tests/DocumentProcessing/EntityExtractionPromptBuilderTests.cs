using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class EntityExtractionPromptBuilderTests
{
    [Fact]
    public void Build_CreatesSystemAndUserPrompts()
    {
        var prompt = EntityExtractionPromptBuilder.Build(
            "alpha beta",
            ["Person", "Concept"],
            maxEntities: 5,
            maxRelationships: 7);

        prompt.SystemPrompt.Should().Contain("---Role---");
        prompt.SystemPrompt.Should().Contain("Entity_types");
        prompt.SystemPrompt.Should().Contain("5 entities");
        prompt.SystemPrompt.Should().Contain("7 relationships");
        prompt.UserPrompt.Should().Contain("---Data to be Processed---");
        prompt.UserPrompt.Should().Contain("alpha beta");
        prompt.UserPrompt.Should().Contain("<|COMPLETE|>");
    }

    [Fact]
    public void Build_CanonicalPrompt_IsStableForEntityTypesWithDifferentInputOrder()
    {
        var firstPrompt = EntityExtractionPromptBuilder.Build(
            "alpha beta",
            ["Concept", " Person ", "Concept"],
            maxEntities: 5,
            maxRelationships: 7);
        var secondPrompt = EntityExtractionPromptBuilder.Build(
            "alpha beta",
            ["Person", "Concept"],
            maxEntities: 5,
            maxRelationships: 7);

        firstPrompt.CanonicalPrompt.Should().Be(secondPrompt.CanonicalPrompt);
        firstPrompt.SystemPrompt.Should().Contain("Concept, Person");
        firstPrompt.UserPrompt.Should().Contain("\"Concept\"");
        firstPrompt.UserPrompt.Should().Contain("\"Person\"");
        firstPrompt.CanonicalPrompt.Should().NotContain(" Person ");
    }

    [Fact]
    public void CanonicalPrompt_JoinsUserThenSystemPrompt()
    {
        var prompt = new EntityExtractionPrompt("user prompt", "system prompt");

        prompt.CanonicalPrompt.Should().Be("user prompt\nsystem prompt");
    }
}
