using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.TestDoubles;

internal static class TestChunkingServiceFactory
{
    public static LightRagChunkingService Create(
        ITokenizer tokenizer,
        IOptions<LightRAGOptions> options,
        IEmbeddingService? embeddingService = null)
    {
        var recursive = new RecursiveCharacterChunkingStrategy();

        return new LightRagChunkingService(
            [
                new FixedTokenChunkingStrategy(),
                recursive,
                new SemanticVectorChunkingStrategy(
                    embeddingService ?? Substitute.For<IEmbeddingService>(),
                    recursive,
                    NullLogger<SemanticVectorChunkingStrategy>.Instance),
                new ParagraphSemanticChunkingStrategy(recursive)
            ],
            tokenizer,
            options,
            NullLogger<LightRagChunkingService>.Instance);
    }
}
