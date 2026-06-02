using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class LightRagChunkingOptionsTests
{
    [Fact]
    public void Normalize_WhenUnset_DefaultsToFixedToken()
    {
        var options = new LightRAGOptions();

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.FixedToken);
        snapshot.ChunkTokenSize.Should().Be(1200);
        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(100);
    }

    [Fact]
    public void Normalize_ParagraphSemantic_DefaultsToTwoThousandTokens()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 1200,
            Chunking = new LightRagChunkingOptions
            {
                Strategy = LightRagChunkingStrategy.ParagraphSemantic
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.ParagraphSemantic);
        snapshot.ParagraphSemantic.ChunkTokenSize.Should().Be(2000);
    }

    [Fact]
    public void Normalize_WhenOverlapExceedsChunkSize_ClampsOverlap()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 5,
            ChunkOverlapTokenSize = 12
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(4);
    }

    [Fact]
    public void Normalize_WhenChunkSizeIsZero_Throws()
    {
        var options = new LightRAGOptions { ChunkTokenSize = 0 };

        var act = () => options.CreateChunkingSnapshot();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ChunkTokenSize*greater than zero*");
    }

    [Fact]
    public void Normalize_WhenOptionsBranchesAreNull_FallsBackToDefaults()
    {
        var options = new LightRAGOptions
        {
            Chunking = new LightRagChunkingOptions
            {
                Strategy = LightRagChunkingStrategy.SemanticVector,
                FixedToken = null,
                RecursiveCharacter = null,
                SemanticVector = new SemanticVectorChunkingOptions
                {
                    SentenceSplitRegex = "   "
                },
                ParagraphSemantic = null
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.SemanticVector);
        snapshot.FixedToken.ChunkTokenSize.Should().Be(1200);
        snapshot.RecursiveCharacter.Separators.Should().Equal("\n\n", "\n", "。", "！", "？", "；", "，", " ", "");
        snapshot.SemanticVector.SentenceSplitRegex.Should().Be(@"(?<=[。？！.!?])\s+");
        snapshot.ParagraphSemantic.ChunkTokenSize.Should().Be(2000);
    }

    [Fact]
    public void Normalize_WhenChunkingIsNull_FallsBackToDefaultChunking()
    {
        var options = new LightRAGOptions
        {
            Chunking = null
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.FixedToken);
        snapshot.FixedToken.ChunkTokenSize.Should().Be(1200);
        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(100);
    }

    [Fact]
    public void Normalize_WhenSeparatorsAreEmpty_FallsBackToDefaultCascade()
    {
        var options = new LightRAGOptions
        {
            Chunking = new LightRagChunkingOptions
            {
                RecursiveCharacter = new RecursiveCharacterChunkingOptions
                {
                    Separators = []
                }
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.RecursiveCharacter.Separators.Should().Equal("\n\n", "\n", "。", "！", "？", "；", "，", " ", "");
    }

    [Fact]
    public void Normalize_WhenNullableBranchValuesAreNull_FallsBackToDefaults()
    {
        var options = new LightRAGOptions
        {
            Chunking = new LightRagChunkingOptions
            {
                RecursiveCharacter = new RecursiveCharacterChunkingOptions
                {
                    Separators = null
                },
                SemanticVector = new SemanticVectorChunkingOptions
                {
                    SentenceSplitRegex = null
                }
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.RecursiveCharacter.Separators.Should().Equal("\n\n", "\n", "。", "！", "？", "；", "，", " ", "");
        snapshot.SemanticVector.SentenceSplitRegex.Should().Be(@"(?<=[。？！.!?])\s+");
    }

    [Fact]
    public void Normalize_WhenSourceOptionsMutateAfterSnapshot_DoesNotDrift()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 7,
            Chunking = new LightRagChunkingOptions
            {
                RecursiveCharacter = new RecursiveCharacterChunkingOptions
                {
                    Separators = ["A"]
                },
                SemanticVector = new SemanticVectorChunkingOptions
                {
                    SentenceSplitRegex = "original"
                }
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        options.ChunkTokenSize = 99;
        options.Chunking!.RecursiveCharacter!.Separators![0] = "B";
        options.Chunking.RecursiveCharacter.Separators.Add("C");
        options.Chunking.SemanticVector!.SentenceSplitRegex = "mutated";

        snapshot.ChunkTokenSize.Should().Be(7);
        snapshot.RecursiveCharacter.Separators.Should().Equal("A");
        snapshot.SemanticVector.SentenceSplitRegex.Should().Be("original");
    }
}
