using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightRAGNet.Tests.Query;

public sealed class LightRAGQueryModeTests
{
    [Fact]
    public async Task QueryAsync_WhenBypassNonStreaming_CallsLlmWithOriginalQueryWithoutKeywords()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        llmService
            .GenerateAsync(
                "What is LightRAGNet?",
                Arg.Is<string?>(systemPrompt => systemPrompt == null),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("direct answer");
        var vectorStore = Substitute.For<IVectorStore>();
        var rerankService = Substitute.For<IRerankService>();
        var tokenizer = Substitute.For<ITokenizer>();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore, rerankService: rerankService, tokenizer: tokenizer);

        var result = await rag.QueryAsync(
            "What is LightRAGNet?",
            new QueryParam { Mode = QueryMode.Bypass });

        result.Content.Should().Be("direct answer");
        result.Metadata["query_mode"].Should().Be("Bypass");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await vectorStore.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default);
        await rerankService.DidNotReceiveWithAnyArgs().RerankAsync(default!, default!, default);
        tokenizer.DidNotReceiveWithAnyArgs().CountTokens(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenBypassOnlyNeedContext_ReturnsEmptyContextRawDataWithoutRetrieval()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        var vectorStore = Substitute.For<IVectorStore>();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "raw question",
            new QueryParam
            {
                Mode = QueryMode.Bypass,
                OnlyNeedContext = true
            });

        result.Content.Should().BeEmpty();
        result.Metadata["query_mode"].Should().Be("Bypass");
        result.RawData.Should().NotBeNull();
        var rawData = result.RawData!;
        var data = rawData["data"].Should().BeOfType<Dictionary<string, object>>().Subject!;
        data.Should().BeEmpty();
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
        await vectorStore.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default);
    }

    [Fact]
    public async Task QueryAsync_WhenBypassOnlyNeedPrompt_ReturnsOriginalQueryWithoutGenerate()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        var rag = CreateLightRag(llmService);

        var result = await rag.QueryAsync(
            "raw question",
            new QueryParam
            {
                Mode = QueryMode.Bypass,
                OnlyNeedPrompt = true
            });

        result.Content.Should().Be("raw question");
        result.Metadata["query_mode"].Should().Be("Bypass");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenBypassStreaming_CallsStreamingLlmWithOriginalQueryWithoutKeywords()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        llmService
            .GenerateStreamAsync(
                "raw question",
                Arg.Is<string?>(systemPrompt => systemPrompt == null),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(AsyncValues("direct chunk"));
        var vectorStore = Substitute.For<IVectorStore>();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "raw question",
            new QueryParam
            {
                Mode = QueryMode.Bypass,
                Stream = true
            });

        result.IsStreaming.Should().BeTrue();
        (await ReadAllAsync(result.ResponseIterator)).Should().Equal("direct chunk");
        result.Metadata["query_mode"].Should().Be("Bypass");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await vectorStore.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default);
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveOnlyNeedContext_ReturnsVectorContextWithoutKeywordsOrGenerate()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        var vectorStore = CreateVectorStoreWithChunk();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "alpha question",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                OnlyNeedContext = true,
                EnableRerank = false
            });

        result.Content.Should().Contain("alpha chunk content");
        result.Metadata["query_mode"].Should().Be("Naive");
        vectorStore.QueryCalls.Should().ContainSingle(call =>
            call.Collection == "chunks" &&
            call.Query == "alpha question");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveOnlyNeedPrompt_ReturnsNaivePromptWithoutGenerate()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        var vectorStore = CreateVectorStoreWithChunk();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "alpha question",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                OnlyNeedPrompt = true,
                EnableRerank = false
            });

        result.Content.Should().Contain("You are an expert AI assistant answering from retrieved document chunks.");
        result.Content.Should().Contain("alpha chunk content");
        result.Content.Should().Contain("---User Query---");
        result.Content.Should().Contain("alpha question");
        result.Metadata["query_mode"].Should().Be("Naive");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await llmService.DidNotReceiveWithAnyArgs().GenerateAsync(default!);
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveNonStreaming_CallsLlmWithNaivePrompt()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        llmService
            .GenerateAsync(
                "alpha question",
                Arg.Is<string?>(systemPrompt =>
                    systemPrompt != null &&
                    systemPrompt.Contains("retrieved document chunks", StringComparison.Ordinal) &&
                    systemPrompt.Contains("alpha chunk content", StringComparison.Ordinal)),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns("naive answer");
        var vectorStore = CreateVectorStoreWithChunk();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "alpha question",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                EnableRerank = false
            });

        result.Content.Should().Be("naive answer");
        result.Metadata["query_mode"].Should().Be("Naive");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
        await llmService.Received(1).GenerateAsync(
            "alpha question",
            Arg.Any<string?>(),
            Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
            Arg.Any<float>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WhenNaiveStreaming_CallsStreamingLlmWithNaivePrompt()
    {
        var llmService = Substitute.For<ILLMService>();
        llmService
            .ExtractKeywordsAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeywordsResult>>(_ => throw new InvalidOperationException("Keyword extraction should be skipped."));
        llmService
            .GenerateStreamAsync(
                "alpha question",
                Arg.Is<string?>(systemPrompt =>
                    systemPrompt != null &&
                    systemPrompt.Contains("retrieved document chunks", StringComparison.Ordinal) &&
                    systemPrompt.Contains("alpha chunk content", StringComparison.Ordinal)),
                Arg.Any<List<Microsoft.Extensions.AI.ChatMessage>?>(),
                Arg.Any<float>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(AsyncValues("naive chunk"));
        var vectorStore = CreateVectorStoreWithChunk();
        var rag = CreateLightRag(llmService, vectorStore: vectorStore);

        var result = await rag.QueryAsync(
            "alpha question",
            new QueryParam
            {
                Mode = QueryMode.Naive,
                Stream = true,
                EnableRerank = false
            });

        result.IsStreaming.Should().BeTrue();
        (await ReadAllAsync(result.ResponseIterator)).Should().Equal("naive chunk");
        result.Metadata["query_mode"].Should().Be("Naive");
        await llmService.DidNotReceiveWithAnyArgs().ExtractKeywordsAsync(default!);
    }

    private static InMemoryVectorStore CreateVectorStoreWithChunk()
    {
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Seed("chunks", new VectorDocument
        {
            Id = "chunk-a",
            Content = "alpha chunk content",
            Metadata = new Dictionary<string, object>
            {
                ["file_path"] = "docs/a.md"
            }
        });

        return vectorStore;
    }

    private static async IAsyncEnumerable<string> AsyncValues(params string[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async Task<List<string>> ReadAllAsync(IAsyncEnumerable<string>? values)
    {
        values.Should().NotBeNull();

        var result = new List<string>();
        await foreach (var value in values!)
        {
            result.Add(value);
        }

        return result;
    }

    private static LightRAG CreateLightRag(
        ILLMService llmService,
        IVectorStore? vectorStore = null,
        IRerankService? rerankService = null,
        ITokenizer? tokenizer = null)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = 3,
            ChunkOverlapTokenSize = 1
        });
        tokenizer ??= new FakeTokenizer();
        var graphStore = new InMemoryGraphStore();
        rerankService ??= Substitute.For<IRerankService>();
        var textChunksStore = new InMemoryKvStore();
        var fullDocsStore = new InMemoryKvStore();
        var fullEntitiesStore = new InMemoryKvStore();
        var fullRelationsStore = new InMemoryKvStore();
        var entityChunksStore = new InMemoryKvStore();
        var relationChunksStore = new InMemoryKvStore();
        var llmCacheStore = new InMemoryKvStore();
        var statusStore = Substitute.For<IDocumentStatusStore>();
        var lifecycleService = new DocumentLifecycleService(
            statusStore,
            options,
            NullLogger<DocumentLifecycleService>.Instance);

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);
        vectorStore ??= Substitute.For<IVectorStore>();

        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheStore,
            options,
            NullLogger<DocumentProcessingService>.Instance);

        var loggerFactory = NullLoggerFactory.Instance;
        var knowledgeGraphMergeService = new KnowledgeGraphMergeService(
            graphStore,
            vectorStore,
            embeddingService,
            llmService,
            tokenizer,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            options,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);

        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankService,
            tokenizer,
            textChunksStore,
            options,
            loggerFactory);

        var documentDeletionService = new DocumentDeletionService(
            vectorStore,
            graphStore,
            embeddingService,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            NullLogger<DocumentDeletionService>.Instance);
        var llmCacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            new LightRagCacheKeyBuilder(),
            NullLogger<LightRagLlmCacheService>.Instance);

        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(vectorStore, rerankService, tokenizer),
            llmCacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            llmCacheStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);
    }
}
