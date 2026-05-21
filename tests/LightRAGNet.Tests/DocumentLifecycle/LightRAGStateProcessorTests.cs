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

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class LightRAGStateProcessorTests
{
    [Fact]
    public async Task InsertAsync_WhenStartedConcurrently_PublishesTaskStatesSeriallyFromSingleProcessor()
    {
        var rag = CreateLightRagForStateProcessorTest(progressChunkCount: 2);
        var states = new List<TaskState>();
        var gate = new object();
        using var firstCallbackEntered = new ManualResetEventSlim(false);
        using var releaseFirstCallback = new ManualResetEventSlim(false);
        var docACompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var docBCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        var inside = 0;
        var maxInside = 0;
        rag.TaskStateChanged += (_, state) =>
        {
            var currentInside = Interlocked.Increment(ref inside);
            UpdateMaxInside(ref maxInside, currentInside);

            try
            {
                if (Interlocked.Increment(ref callbackCount) == 1)
                {
                    firstCallbackEntered.Set();
                    releaseFirstCallback.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                }

                lock (gate)
                {
                    states.Add(state);
                }

                if (state.Stage == TaskStage.Completed && state.DocId == "doc-a")
                {
                    docACompleted.TrySetResult();
                }

                if (state.Stage == TaskStage.Completed && state.DocId == "doc-b")
                {
                    docBCompleted.TrySetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        };

        var insertA = Task.Run(() => rag.InsertAsync("alpha beta gamma", docId: "doc-a", filePath: "docs/a.md"));
        var insertB = Task.Run(() => rag.InsertAsync("delta epsilon zeta", docId: "doc-b", filePath: "docs/b.md"));

        firstCallbackEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        try
        {
            await Task.WhenAll(insertA, insertB).WaitAsync(TimeSpan.FromSeconds(5));
            SpinWait.SpinUntil(() => Volatile.Read(ref maxInside) > 1, TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            releaseFirstCallback.Set();
        }

        await Task.WhenAll(docACompleted.Task, docBCompleted.Task).WaitAsync(TimeSpan.FromSeconds(5));

        maxInside.Should().Be(1);
        lock (gate)
        {
            states.Should().Contain(state => state.DocId == "doc-a" && state.Stage == TaskStage.Completed);
            states.Should().Contain(state => state.DocId == "doc-b" && state.Stage == TaskStage.Completed);
        }
    }

    [Fact]
    public async Task TaskStateChanged_WhenOneSubscriberThrows_StillNotifiesOtherSubscribers()
    {
        var rag = CreateLightRagForStateProcessorTest(progressChunkCount: 2);
        var delivered = new List<TaskStage>();
        var gate = new object();
        var completedDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rag.TaskStateChanged += (_, state) =>
        {
            if (state.Stage == TaskStage.Completed)
            {
                throw new InvalidOperationException("subscriber failed");
            }
        };
        rag.TaskStateChanged += (_, state) =>
        {
            lock (gate)
            {
                delivered.Add(state.Stage);
            }

            if (state.Stage == TaskStage.Completed)
            {
                completedDelivered.TrySetResult();
            }
        };

        await rag.InsertAsync("alpha beta gamma", docId: "doc-a", filePath: "docs/a.md");

        await completedDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (gate)
        {
            delivered.Should().Contain(TaskStage.Completed);
        }
    }

    private static LightRAG CreateLightRagForStateProcessorTest(int progressChunkCount)
    {
        var options = Options.Create(new LightRAGOptions
        {
            Workspace = "workspace-a",
            ChunkTokenSize = Math.Max(1, progressChunkCount),
            ChunkOverlapTokenSize = 0
        });

        var tokenizer = new FakeTokenizer();
        var llmService = Substitute.For<ILLMService>();
        llmService.ExtractEntitiesAsync(
                Arg.Any<string>(),
                Arg.Any<List<string>>(),
                Arg.Any<float>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new EntityExtractionResult());

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([1.0f, 0.5f]);

        var vectorStore = new SynchronizedVectorStore(new InMemoryVectorStore());
        var graphStore = Substitute.For<IGraphStore>();
        var rerankService = Substitute.For<IRerankService>();
        var textChunksStore = new SynchronizedKvStore(new InMemoryKvStore());
        var fullDocsStore = new SynchronizedKvStore(new InMemoryKvStore());
        var fullEntitiesStore = new SynchronizedKvStore(new InMemoryKvStore());
        var fullRelationsStore = new SynchronizedKvStore(new InMemoryKvStore());
        var entityChunksStore = new SynchronizedKvStore(new InMemoryKvStore());
        var relationChunksStore = new SynchronizedKvStore(new InMemoryKvStore());
        var llmCacheStore = new SynchronizedKvStore(new InMemoryKvStore());
        var lifecycleService = new DocumentLifecycleService(
            new InMemoryDocumentStatusStore(),
            options,
            NullLogger<DocumentLifecycleService>.Instance);
        var cacheKeyBuilder = new LightRagCacheKeyBuilder();
        var llmCacheService = new LightRagLlmCacheService(
            llmCacheStore,
            options,
            cacheKeyBuilder,
            NullLogger<LightRagLlmCacheService>.Instance);

        var documentProcessingService = new DocumentProcessingService(
            llmService,
            embeddingService,
            tokenizer,
            llmCacheService,
            cacheKeyBuilder,
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
            llmCacheService,
            NullLogger<KnowledgeGraphMergeService>.Instance,
            loggerFactory);

        var rerankOptions = Options.Create(new RerankChunkingOptions { EnableChunking = false });
        var rerankCoordinator = new RerankCoordinator(
            rerankService,
            new RerankDocumentChunker(tokenizer, rerankOptions),
            rerankOptions);
        var retrievalContextService = new RetrievalContextService(
            embeddingService,
            vectorStore,
            graphStore,
            rerankCoordinator,
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
        return new LightRAG(
            llmService,
            vectorStore,
            documentProcessingService,
            knowledgeGraphMergeService,
            retrievalContextService,
            new NaiveQueryService(
                vectorStore,
                rerankCoordinator,
                tokenizer),
            llmCacheService,
            tokenizer,
            textChunksStore,
            fullDocsStore,
            fullEntitiesStore,
            fullRelationsStore,
            entityChunksStore,
            relationChunksStore,
            lifecycleService,
            documentDeletionService,
            NullLogger<LightRAG>.Instance);
    }

    private static void UpdateMaxInside(ref int maxInside, int currentInside)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maxInside);
            if (currentInside <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref maxInside, currentInside, observed) == observed)
            {
                return;
            }
        }
    }

    private sealed class SynchronizedKvStore(InMemoryKvStore inner) : IKVStore
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public async Task<Dictionary<string, object>?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.GetByIdAsync(id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<List<Dictionary<string, object>>> GetByIdsAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.GetByIdsAsync(ids, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<HashSet<string>> FilterKeysAsync(
            HashSet<string> keys,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.FilterKeysAsync(keys, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task UpsertAsync(
            Dictionary<string, Dictionary<string, object>> data,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.UpsertAsync(data, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.DeleteAsync(ids, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.IsEmptyAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.IndexDoneCallbackAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task DropAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.DropAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private sealed class SynchronizedVectorStore(InMemoryVectorStore inner) : IVectorStore
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public async Task<List<SearchResult>> QueryAsync(
            string collection,
            string query,
            int topK,
            float[]? queryEmbedding = null,
            float threshold = 0.2F,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.QueryAsync(collection, query, topK, queryEmbedding, threshold, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task UpsertAsync(
            string collection,
            IEnumerable<VectorDocument> documents,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.UpsertAsync(collection, documents, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task DeleteAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await inner.DeleteAsync(collection, ids, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<VectorDocument?> GetByIdAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.GetByIdAsync(collection, id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<List<VectorDocument>> GetByIdsAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await inner.GetByIdsAsync(collection, ids, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
