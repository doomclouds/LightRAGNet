using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.DocumentProcessing;

public sealed class DocumentProcessingServiceTests
{
    [Fact]
    public void ChunkDocument_TrimsContentBeforeTokenization()
    {
        var service = CreateService(chunkSize: 10, overlap: 2);

        var chunks = service.ChunkDocument("  alpha beta  ", "doc-1", "file.md");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("t1 t2");
        chunks[0].Tokens.Should().Be(2);
        chunks[0].FullDocId.Should().Be("doc-1");
        chunks[0].FilePath.Should().Be("file.md");
    }

    [Fact]
    public void ChunkDocument_UsesSlidingTokenWindowWithOverlap()
    {
        var service = CreateService(chunkSize: 4, overlap: 1);

        var chunks = service.ChunkDocument(
            "one two three four five six seven eight",
            "doc-1");

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 2);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t7 t8");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void ChunkDocument_MergesTinyTrailingFragmentIntoPreviousChunk()
    {
        var service = CreateService(chunkSize: 4, overlap: 1);

        var chunks = service.ChunkDocument(
            "one two three four five six seven eight nine ten",
            "doc-1");

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 5);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t1 t2 t3 t4 t10");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void ChunkDocument_SplitsByCharacter()
    {
        var service = CreateService(chunkSize: 3, overlap: 1);

        var chunks = service.ChunkDocument(
            "alpha beta|gamma delta epsilon zeta|eta",
            "doc-1",
            splitByCharacter: "|");

        chunks.Should().HaveCount(4);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(2, 3, 2, 1);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "t1 t2 t3",
            "t3 t4",
            "eta");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void ChunkDocument_WhenSplitByCharacterOnlyAndSegmentExceedsLimit_Throws()
    {
        var service = CreateService(chunkSize: 2, overlap: 1);

        var act = () => service.ChunkDocument(
            "alpha beta gamma|delta",
            "doc-1",
            splitByCharacter: "|",
            splitByCharacterOnly: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ChunkDocumentAsync_UsesChunkingServiceSnapshot()
    {
        var service = CreateService(chunkSize: 3, overlap: 1);
        var snapshot = new LightRAGOptions
        {
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1,
            Chunking = new LightRagChunkingOptions
            {
                FixedToken = new FixedTokenChunkingOptions
                {
                    SplitByCharacter = "|"
                }
            }
        }.CreateChunkingSnapshot();

        var chunks = await service.ChunkDocumentAsync(
            "alpha beta|gamma delta epsilon zeta|eta",
            "doc-1",
            snapshot: snapshot);

        chunks.Should().HaveCount(4);
        chunks.Select(chunk => chunk.Tokens).Should().Equal(2, 3, 2, 1);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "t1 t2 t3",
            "t3 t4",
            "eta");
        chunks.Select(chunk => chunk.ChunkOrderIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task ProcessChunkAsync_WhenExtractCacheMiss_GeneratesEmbeddingAndSavesExtractCacheKey()
    {
        var llmService = Substitute.For<ILLMService>();
        const string rawResponse = """
                                   entity<|#|>Alpha<|#|>Concept<|#|>Alpha is a concept.
                                   relation<|#|>Alpha<|#|>Beta<|#|>association<|#|>Alpha relates to Beta.
                                   <|COMPLETE|>
                                   """;
        llmService.GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(rawResponse);
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        var llmCacheStore = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var service = CreateService(
            chunkSize: 10,
            overlap: 2,
            llmService,
            embeddingService,
            llmCacheStore,
            keyBuilder);
        var chunk = new Chunk
        {
            Id = "chunk-a",
            Content = "Alpha connects Beta.",
            FilePath = "file.md"
        };
        var prompt = EntityExtractionPromptBuilder.Build(
            chunk.Content,
            DefaultEntityTypes(),
            maxEntities: 45,
            maxRelationships: 60);
        var expectedKey = keyBuilder.BuildExtractKey(prompt.CanonicalPrompt);

        var result = await service.ProcessChunkAsync(chunk);

        result.Embedding.Should().Equal(1.0f, 0.5f);
        result.Entities.Should().ContainSingle(entity =>
            entity.Name == "Alpha" &&
            entity.Type == "concept" &&
            entity.SourceId == chunk.Id &&
            entity.FilePath == chunk.FilePath);
        result.Relationships.Should().ContainSingle(relation =>
            relation.SourceId == "Alpha" &&
            relation.TargetId == "Beta" &&
            relation.SourceChunkId == chunk.Id &&
            relation.FilePath == chunk.FilePath);
        result.LlmCacheKeys.Should().Equal(expectedKey);
        await llmService.Received(1).GenerateAsync(
            prompt.UserPrompt,
            prompt.SystemPrompt,
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            0.3f,
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        llmCacheStore.Items.Should().ContainKey(expectedKey);
        var entry = llmCacheStore.Items[expectedKey];
        entry["cache_type"].Should().Be(LightRagCacheKeyBuilder.ExtractCacheType);
        entry["chunk_id"].Should().Be(chunk.Id);
        entry["return"].Should().Be(rawResponse);
    }

    [Fact]
    public async Task ProcessChunkAsync_WhenExtractCacheHit_GeneratesEmbeddingAndParsesCachedResponse()
    {
        var llmService = Substitute.For<ILLMService>();
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([0.25f, 0.75f]);
        var llmCacheStore = new InMemoryKvStore();
        var keyBuilder = new LightRagCacheKeyBuilder();
        var chunk = new Chunk
        {
            Id = "chunk-hit",
            Content = "Gamma contains Delta.",
            FilePath = "cached.md"
        };
        var prompt = EntityExtractionPromptBuilder.Build(
            chunk.Content,
            DefaultEntityTypes(),
            maxEntities: 45,
            maxRelationships: 60);
        var expectedKey = keyBuilder.BuildExtractKey(prompt.CanonicalPrompt);
        llmCacheStore.Seed(
            expectedKey,
            new LightRagCacheEntry(
                """
                entity<|#|>Gamma<|#|>Concept<|#|>Gamma is cached.
                relation<|#|>Gamma<|#|>Delta<|#|>contains<|#|>Gamma contains Delta.
                <|COMPLETE|>
                """,
                LightRagCacheKeyBuilder.ExtractCacheType,
                prompt.CanonicalPrompt,
                null,
                123,
                chunk.Id)
            .ToDictionary());
        var service = CreateService(
            chunkSize: 10,
            overlap: 2,
            llmService,
            embeddingService,
            llmCacheStore,
            keyBuilder);

        var result = await service.ProcessChunkAsync(chunk);

        result.Embedding.Should().Equal(0.25f, 0.75f);
        result.Entities.Should().ContainSingle(entity =>
            entity.Name == "Gamma" &&
            entity.SourceId == chunk.Id &&
            entity.FilePath == chunk.FilePath);
        result.Relationships.Should().ContainSingle(relation =>
            relation.SourceId == "Gamma" &&
            relation.TargetId == "Delta" &&
            relation.SourceChunkId == chunk.Id &&
            relation.FilePath == chunk.FilePath);
        result.LlmCacheKeys.Should().Equal(expectedKey);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default, default, default, default, default);
        await embeddingService.Received(1).GenerateEmbeddingAsync(chunk.Content, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessChunkAsync_WhenLegacyChunkIdCacheExists_IgnoresLegacyEntry()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService.GenerateAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("entity<|#|>Fresh<|#|>Concept<|#|>Fresh response.\n<|COMPLETE|>");
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([9.0f, 8.0f]);
        var llmCacheStore = new InMemoryKvStore();
        llmCacheStore.Seed("chunk-legacy", new Dictionary<string, object>
        {
            ["chunk_id"] = "chunk-legacy",
            ["embedding"] = new List<object> { 1.0f, 2.0f },
            ["entities"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["name"] = "Legacy",
                    ["type"] = "Concept",
                    ["description"] = "Old cached result"
                }
            },
            ["relationships"] = new List<object>()
        });
        var service = CreateService(
            chunkSize: 10,
            overlap: 2,
            llmService,
            embeddingService,
            llmCacheStore);

        var result = await service.ProcessChunkAsync(new Chunk
        {
            Id = "chunk-legacy",
            Content = "Fresh content.",
            FilePath = "fresh.md"
        });

        result.Embedding.Should().Equal(9.0f, 8.0f);
        result.Entities.Should().ContainSingle(entity => entity.Name == "Fresh");
        result.Entities.Should().NotContain(entity => entity.Name == "Legacy");
        result.LlmCacheKeys.Should().ContainSingle(key =>
            key.StartsWith("default:extract:", StringComparison.Ordinal));
        llmCacheStore.GetByIdCalls.Should().NotContain("chunk-legacy");
        await llmService.Received(1).GenerateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            0.3f,
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessChunkAsync_WhenManyExtractCacheMisses_LimitsConcurrentGenerateAsyncCalls()
    {
        var llmService = new TrackingLlmService();
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f]);
        var service = CreateService(
            chunkSize: 10,
            overlap: 2,
            llmService,
            embeddingService,
            options: new LightRAGOptions
            {
                ChunkTokenSize = 10,
                ChunkOverlapTokenSize = 2,
                EnableLlmCacheForEntityExtract = false
            });
        var chunks = Enumerable.Range(0, 12)
            .Select(index => new Chunk
            {
                Id = $"chunk-{index}",
                Content = $"content {index}",
                FilePath = "file.md"
            })
            .ToList();

        await Task.WhenAll(chunks.Select(chunk => service.ProcessChunkAsync(chunk)));

        llmService.GenerateCallCount.Should().Be(12);
        llmService.MaxConcurrentGenerateCalls.Should().BeLessThanOrEqualTo(10);
    }

    private static DocumentProcessingService CreateService(
        int chunkSize,
        int overlap,
        ILLMService? llmService = null,
        IEmbeddingService? embeddingService = null,
        InMemoryKvStore? llmCacheStore = null,
        LightRagCacheKeyBuilder? keyBuilder = null,
        LightRAGOptions? options = null)
    {
        llmCacheStore ??= new InMemoryKvStore();
        keyBuilder ??= new LightRagCacheKeyBuilder();
        var lightRagOptions = options ?? new LightRAGOptions
        {
            ChunkTokenSize = chunkSize,
            ChunkOverlapTokenSize = overlap
        };
        var tokenizer = new FakeTokenizer();
        var optionsAccessor = Options.Create(lightRagOptions);
        var chunkingService = new LightRagChunkingService(
            [new FixedTokenChunkingStrategy()],
            tokenizer,
            optionsAccessor,
            NullLogger<LightRagChunkingService>.Instance);

        return new DocumentProcessingService(
            llmService ?? Substitute.For<ILLMService>(),
            embeddingService ?? Substitute.For<IEmbeddingService>(),
            tokenizer,
            new LightRagLlmCacheService(
                llmCacheStore,
                optionsAccessor,
                keyBuilder,
                NullLogger<LightRagLlmCacheService>.Instance),
            optionsAccessor,
            NullLogger<DocumentProcessingService>.Instance,
            chunkingService);
    }

    private static List<string> DefaultEntityTypes()
    {
        return
        [
            "Person", "Creature", "Organization", "Location", "Event",
            "Concept", "Method", "Content", "Data", "Artifact", "NaturalObject"
        ];
    }

    private sealed class TrackingLlmService : ILLMService
    {
        private int _currentGenerateCalls;
        private int _generateCallCount;
        private int _maxConcurrentGenerateCalls;

        public int GenerateCallCount => _generateCallCount;
        public int MaxConcurrentGenerateCalls => _maxConcurrentGenerateCalls;

        public async Task<string> GenerateAsync(
            string prompt,
            string? systemPrompt = null,
            List<Microsoft.Extensions.AI.ChatMessage>? historyMessages = null,
            float temperature = 1,
            bool enableCot = false,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _currentGenerateCalls);
            Interlocked.Increment(ref _generateCallCount);
            try
            {
                UpdateMaxConcurrentGenerateCalls(current);
                await Task.Delay(50, cancellationToken);
                return "<|COMPLETE|>";
            }
            finally
            {
                Interlocked.Decrement(ref _currentGenerateCalls);
            }
        }

        private void UpdateMaxConcurrentGenerateCalls(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrentGenerateCalls);
                if (current <= observed)
                {
                    return;
                }

                var original = Interlocked.CompareExchange(
                    ref _maxConcurrentGenerateCalls,
                    current,
                    observed);
                if (original == observed)
                {
                    return;
                }
            }
        }

        public IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            string? systemPrompt = null,
            List<Microsoft.Extensions.AI.ChatMessage>? historyMessages = null,
            float temperature = 1,
            bool enableCot = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<EntityExtractionResult> ExtractEntitiesAsync(
            string text,
            List<string> entityTypes,
            float temperature = 0.3f,
            int? maxEntities = null,
            int? maxRelationships = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<KeywordsResult> ExtractKeywordsAsync(
            string query,
            float temperature = 0.3f,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> SummarizeAsync(
            string descriptionType,
            string descriptionName,
            List<string> descriptionList,
            int summaryLengthRecommended,
            float temperature = 0.3f,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
