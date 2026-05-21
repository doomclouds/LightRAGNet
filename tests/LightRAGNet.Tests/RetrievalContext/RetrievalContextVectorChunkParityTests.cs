using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.RetrievalContext;

public sealed class RetrievalContextVectorChunkParityTests
{
    private const string Sep = "<SEP>";

    [Fact]
    public async Task BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsEntityChunksByCosineSimilarity()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid",
            ["file_path"] = "docs/alpha.md"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Ids.Order().SequenceEqual(new[] { "chunk-far", "chunk-mid", "chunk-near" }.Order()));
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenChunkVectorContainsNonFiniteValue_FallsBackToWeightedPolling()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid",
            ["file_path"] = "docs/alpha.md"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { float.NaN, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenChunkVectorMissing_FallsBackToWeightedPolling()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-near", new[] { 1.0f, 0.0f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().ContainSingle(call => call.Collection == "chunks");
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenQueryEmbeddingFails_FallsBackToWeightedPolling()
    {
        var harness = CreateHarness(failingQuery: "alpha question");
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenKgChunkPickMethodWeight_DoesNotReadChunkVectors()
    {
        var harness = CreateHarness(new LightRAGOptions
        {
            KgChunkPickMethod = "WEIGHT",
            RelatedChunkNumber = 4
        });
        harness.VectorStore.Seed("entities", new VectorDocument
        {
            Id = "entity-alpha",
            Metadata = new Dictionary<string, object>
            {
                ["entity_name"] = "Alpha"
            },
            Content = "Alpha entity"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = $"chunk-far{Sep}chunk-near{Sep}chunk-mid"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far content", "docs/far.md"),
            ("chunk-near", "near content", "docs/near.md"),
            ("chunk-mid", "mid content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "alpha question",
            new KeywordsResult { LowLevelKeywords = ["alpha"] },
            new QueryParam
            {
                Mode = QueryMode.Local,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenVectorChunkPickEnabled_SelectsRelationChunksByCosineSimilarity()
    {
        var harness = CreateHarness();
        SeedGlobalRelation(harness, relationSourceId: $"chunk-far{Sep}chunk-near{Sep}chunk-mid");
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far relation content", "docs/far.md"),
            ("chunk-near", "near relation content", "docs/near.md"),
            ("chunk-mid", "mid relation content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "relation question",
            new KeywordsResult { HighLevelKeywords = ["relation"] },
            new QueryParam
            {
                Mode = QueryMode.Global,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-near", "chunk-mid");
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenRelationQueryEmbeddingFails_FallsBackToWeightedPolling()
    {
        var harness = CreateHarness(failingQuery: "relation question");
        SeedGlobalRelation(harness, relationSourceId: $"chunk-far{Sep}chunk-near{Sep}chunk-mid");
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-far", "far relation content", "docs/far.md"),
            ("chunk-near", "near relation content", "docs/near.md"),
            ("chunk-mid", "mid relation content", "docs/mid.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-far", new[] { 0.0f, 1.0f }),
            ("chunk-near", new[] { 1.0f, 0.0f }),
            ("chunk-mid", new[] { 0.8f, 0.6f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "relation question",
            new KeywordsResult { HighLevelKeywords = ["relation"] },
            new QueryParam
            {
                Mode = QueryMode.Global,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-far", "chunk-near", "chunk-mid");
        harness.VectorStore.GetByIdsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenRelationVectorChunksOverlapEntityChunks_ExcludesEntityChunks()
    {
        var harness = CreateHarness();
        harness.VectorStore.Seed("relationships", new VectorDocument
        {
            Id = "rel-alpha-beta",
            Metadata = new Dictionary<string, object>
            {
                ["src_id"] = "Alpha",
                ["tgt_id"] = "Beta"
            },
            Content = "Alpha Beta relation"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description",
            ["source_id"] = "chunk-entity"
        });
        harness.GraphStore.SeedNode("Beta", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Beta description"
        });
        harness.GraphStore.SeedEdge("Alpha", "Beta", new Dictionary<string, object>
        {
            ["keywords"] = "relation",
            ["description"] = "Alpha relates to Beta",
            ["weight"] = 1.0d,
            ["source_id"] = $"chunk-entity{Sep}chunk-relation-far{Sep}chunk-relation-near"
        });
        await SeedTextChunksAsync(harness.TextChunks, [
            ("chunk-entity", "entity content", "docs/entity.md"),
            ("chunk-relation-far", "far relation content", "docs/far.md"),
            ("chunk-relation-near", "near relation content", "docs/near.md")
        ]);
        SeedChunkVectors(harness.VectorStore, [
            ("chunk-entity", new[] { 1.0f, 0.0f }),
            ("chunk-relation-far", new[] { 0.0f, 1.0f }),
            ("chunk-relation-near", new[] { 0.9f, 0.1f })
        ]);

        var result = await harness.Service.BuildQueryContextAsync(
            "relation question",
            new KeywordsResult { HighLevelKeywords = ["relation"] },
            new QueryParam
            {
                Mode = QueryMode.Global,
                EnableRerank = false,
                TopK = 5,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        var chunkIds = GetChunkIds(result!);
        chunkIds.Should().Equal("chunk-entity", "chunk-relation-near", "chunk-relation-far");
        chunkIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BuildQueryContextAsync_WhenMixVectorRerankChunksDocuments_AggregatesScoresByOriginalChunk()
    {
        var rerankService = new RecordingRerankService(
        [
            new RerankResult { Index = 4, RelevanceScore = 0.95f },
            new RerankResult { Index = 0, RelevanceScore = 0.8f },
            new RerankResult { Index = 5, RelevanceScore = 0.2f }
        ]);
        var harness = CreateHarness(
            rerankService: rerankService,
            tokenizer: new RoundTripWhitespaceTokenizer(),
            rerankChunkingOptions: new RerankChunkingOptions
            {
                EnableChunking = true,
                MaxTokensPerDocument = 2,
                OverlapTokens = 0
            });
        harness.VectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-b",
            Content = "b1 b2 b3 b4 b5 b6",
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/b.md"
            }
        });
        harness.VectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "a1 a2 a3 a4 a5 a6",
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/a.md"
            }
        });

        var result = await harness.Service.BuildQueryContextAsync(
            "mix question",
            new KeywordsResult(),
            new QueryParam
            {
                Mode = QueryMode.Mix,
                EnableRerank = true,
                TopK = 2,
                ChunkTopK = 6,
                MaxTotalTokens = 30000
            });

        result.Should().NotBeNull();
        GetChunkIds(result!).Should().Equal("chunk-a", "chunk-b");
        rerankService.Calls.Should().ContainSingle();
        rerankService.Calls[0].Documents.Should().Equal(
            "b1 b2",
            "b3 b4",
            "b5 b6",
            "a1 a2",
            "a3 a4",
            "a5 a6");
        rerankService.Calls[0].TopN.Should().Be(6);
    }

    private static TestHarness CreateHarness(
        LightRAGOptions? options = null,
        string? failingQuery = null,
        IRerankService? rerankService = null,
        ITokenizer? tokenizer = null,
        RerankChunkingOptions? rerankChunkingOptions = null)
    {
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<string>(0) == failingQuery)
                {
                    throw new InvalidOperationException("Query embedding failed.");
                }

                return [1.0f, 0.0f];
            });

        var vectorStore = new InMemoryVectorStore();
        var graphStore = new InMemoryGraphStore();
        var textChunks = new InMemoryKvStore();
        tokenizer ??= new FakeTokenizer();
        var chunkingOptions = Options.Create(rerankChunkingOptions ?? new RerankChunkingOptions
        {
            EnableChunking = false
        });
        var service = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            new RerankCoordinator(
                rerankService ?? Substitute.For<IRerankService>(),
                new RerankDocumentChunker(tokenizer, chunkingOptions),
                chunkingOptions),
            tokenizer,
            textChunks,
            Options.Create(options ?? new LightRAGOptions
            {
                KgChunkPickMethod = "VECTOR",
                RelatedChunkNumber = 4
            }),
            NullLoggerFactory.Instance);

        return new TestHarness(service, vectorStore, graphStore, textChunks);
    }

    private sealed class RecordingRerankService(List<RerankResult> resultsToReturn) : IRerankService
    {
        public List<RerankCall> Calls { get; } = [];

        public Task<List<RerankResult>> RerankAsync(
            string query,
            List<string> documents,
            int topN,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RerankCall(query, [.. documents], topN, cancellationToken));
            return Task.FromResult(resultsToReturn);
        }
    }

    private sealed record RerankCall(
        string Query,
        List<string> Documents,
        int TopN,
        CancellationToken CancellationToken);

    private sealed class RoundTripWhitespaceTokenizer : ITokenizer
    {
        private readonly Dictionary<string, int> _tokenIdsByWord = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _wordsByTokenId = [];
        private int _nextTokenId = 1;

        public List<int> Encode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(GetTokenId)
                .ToList();
        }

        public string Decode(List<int> tokens)
        {
            return string.Join(' ', tokens.Select(token => _wordsByTokenId[token]));
        }

        public int CountTokens(string text)
        {
            return Encode(text).Count;
        }

        private int GetTokenId(string word)
        {
            if (_tokenIdsByWord.TryGetValue(word, out var tokenId))
            {
                return tokenId;
            }

            tokenId = _nextTokenId++;
            _tokenIdsByWord[word] = tokenId;
            _wordsByTokenId[tokenId] = word;
            return tokenId;
        }
    }

    private static async Task SeedTextChunksAsync(
        InMemoryKvStore textChunks,
        IEnumerable<(string Id, string Content, string FilePath)> chunks)
    {
        await textChunks.UpsertAsync(chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => new Dictionary<string, object>
            {
                ["content"] = chunk.Content,
                ["file_path"] = chunk.FilePath
            }));
    }

    private static void SeedChunkVectors(
        InMemoryVectorStore vectorStore,
        IEnumerable<(string Id, float[] Vector)> chunks)
    {
        foreach (var (id, vector) in chunks)
        {
            vectorStore.Seed("chunks", new VectorDocument
            {
                Id = id,
                Vector = vector,
                Content = $"{id} content",
                Metadata = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["file_path"] = $"docs/{id}.md"
                }
            });
        }
    }

    private static void SeedGlobalRelation(TestHarness harness, string relationSourceId)
    {
        harness.VectorStore.Seed("relationships", new VectorDocument
        {
            Id = "rel-alpha-beta",
            Metadata = new Dictionary<string, object>
            {
                ["src_id"] = "Alpha",
                ["tgt_id"] = "Beta"
            },
            Content = "Alpha Beta relation"
        });
        harness.GraphStore.SeedNode("Alpha", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Alpha description"
        });
        harness.GraphStore.SeedNode("Beta", new Dictionary<string, object>
        {
            ["entity_type"] = "Concept",
            ["description"] = "Beta description"
        });
        harness.GraphStore.SeedEdge("Alpha", "Beta", new Dictionary<string, object>
        {
            ["keywords"] = "relation",
            ["description"] = "Alpha relates to Beta",
            ["weight"] = 1.0d,
            ["source_id"] = relationSourceId
        });
    }

    private static List<string> GetChunkIds(QueryContextResult result)
    {
        var data = result.RawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var chunks = data["chunks"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        return chunks.Select(chunk => chunk["chunk_id"].ToString()!).ToList();
    }

    private sealed record TestHarness(
        RetrievalContextService Service,
        InMemoryVectorStore VectorStore,
        InMemoryGraphStore GraphStore,
        InMemoryKvStore TextChunks);
}
