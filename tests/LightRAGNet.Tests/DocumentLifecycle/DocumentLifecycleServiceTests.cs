using FluentAssertions;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.DocumentLifecycle;

public sealed class DocumentLifecycleServiceTests
{
    [Fact]
    public void DocumentLifecycleStatus_ToWireValue_UsesPythonStyleValues()
    {
        DocumentLifecycleStatus.Pending.ToWireValue().Should().Be("pending");
        DocumentLifecycleStatus.Processing.ToWireValue().Should().Be("processing");
        DocumentLifecycleStatus.Processed.ToWireValue().Should().Be("processed");
        DocumentLifecycleStatus.Failed.ToWireValue().Should().Be("failed");
        DocumentLifecycleStatus.Deleting.ToWireValue().Should().Be("deleting");
        DocumentLifecycleStatus.Deleted.ToWireValue().Should().Be("deleted");
        DocumentLifecycleStatus.DeletionFailed.ToWireValue().Should().Be("deletion_failed");
    }

    [Fact]
    public void DocumentLifecycleStatus_FromWireValue_ParsesPythonStyleValues()
    {
        DocumentLifecycleStatusExtensions.FromWireValue("pending").Should().Be(DocumentLifecycleStatus.Pending);
        DocumentLifecycleStatusExtensions.FromWireValue("processing").Should().Be(DocumentLifecycleStatus.Processing);
        DocumentLifecycleStatusExtensions.FromWireValue("processed").Should().Be(DocumentLifecycleStatus.Processed);
        DocumentLifecycleStatusExtensions.FromWireValue("failed").Should().Be(DocumentLifecycleStatus.Failed);
        DocumentLifecycleStatusExtensions.FromWireValue("deleting").Should().Be(DocumentLifecycleStatus.Deleting);
        DocumentLifecycleStatusExtensions.FromWireValue("deleted").Should().Be(DocumentLifecycleStatus.Deleted);
        DocumentLifecycleStatusExtensions.FromWireValue("deletion_failed").Should().Be(DocumentLifecycleStatus.DeletionFailed);
    }

    [Fact]
    public void DocumentLifecycleStatus_FromWireValue_WhenUnknown_Throws()
    {
        var act = () => DocumentLifecycleStatusExtensions.FromWireValue("unknown");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*unknown*");
    }

    [Fact]
    public void PublicStateMutationMethods_ReturnTask()
    {
        var methodNames = new[]
        {
            nameof(DocumentLifecycleService.StartProcessingAsync),
            nameof(DocumentLifecycleService.RecordChunksAsync),
            nameof(DocumentLifecycleService.MarkProcessedAsync),
            nameof(DocumentLifecycleService.MarkFailedAsync)
        };

        foreach (var methodName in methodNames)
        {
            typeof(DocumentLifecycleService)
                .GetMethods()
                .Single(method => method.Name == methodName)
                .ReturnType
                .Should()
                .Be(typeof(Task));
        }
    }

    [Fact]
    public async Task PrepareIngestion_WhenWorkspaceIsBlank_UsesDefaultWorkspace()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store, workspace: " ");

        var result = await service.PrepareIngestionAsync(
            "  Alpha document body  ",
            docId: "doc-1",
            filePath: "alpha.md",
            trackId: "track-1");

        result.IsDuplicate.Should().BeFalse();
        result.DocId.Should().Be("doc-1");
        result.Workspace.Should().Be("_");
        result.StatusRecord.Status.Should().Be(DocumentLifecycleStatus.Pending);
        result.StatusRecord.ContentSummary.Should().Be("Alpha document body");
        result.StatusRecord.ContentLength.Should().Be("  Alpha document body  ".Length);
        result.StatusRecord.FilePath.Should().Be("alpha.md");
        result.StatusRecord.TrackId.Should().Be("track-1");

        var stored = await store.GetAsync("_", "doc-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DocumentLifecycleStatus.Pending);
    }

    [Fact]
    public void GetDefaultWorkspace_ReturnsNormalizedConfiguredWorkspace()
    {
        CreateService(new InMemoryDocumentStatusStore(), workspace: " workspace-a ")
            .GetDefaultWorkspace()
            .Should()
            .Be("workspace-a");

        CreateService(new InMemoryDocumentStatusStore(), workspace: " ")
            .GetDefaultWorkspace()
            .Should()
            .Be("_");
    }

    [Fact]
    public async Task StartProcessing_PendingDocument_MarksProcessing()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1");
        await service.MarkFailedAsync("workspace-a", "doc-1", "previous", "old error");

        await service.StartProcessingAsync("workspace-a", "doc-1");

        var record = await store.GetAsync("workspace-a", "doc-1");
        record.Should().NotBeNull();
        record!.Status.Should().Be(DocumentLifecycleStatus.Processing);
        record.ErrorMessage.Should().BeEmpty();
        record.Metadata.Should().NotContainKey("failure_stage");
    }

    [Fact]
    public async Task StartProcessing_BlankWorkspace_UsesDefaultWorkspace()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store, workspace: " ");
        await service.PrepareIngestionAsync("content", docId: "doc-1");

        await service.StartProcessingAsync("  ", "doc-1");

        var record = await store.GetAsync("_", "doc-1");
        record.Should().NotBeNull();
        record!.Workspace.Should().Be("_");
        record.Status.Should().Be(DocumentLifecycleStatus.Processing);
    }

    [Fact]
    public async Task StartProcessing_MissingDocument_LogsWarning()
    {
        var store = new InMemoryDocumentStatusStore();
        var logger = new TestLogger();
        var service = CreateService(store, logger: logger);

        await service.StartProcessingAsync(" workspace-a ", "missing-doc");

        var record = await store.GetAsync("workspace-a", "missing-doc");
        record.Should().BeNull();
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("workspace-a", StringComparison.Ordinal)
            && entry.Message.Contains("missing-doc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordChunks_AfterChunking_PreservesChunkSnapshot()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "doc.md");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        var chunks = CreateChunks("doc-1");

        await service.RecordChunksAsync("workspace-a", "doc-1", chunks);

        var record = await store.GetAsync("workspace-a", "doc-1");
        record.Should().NotBeNull();
        record!.ChunksCount.Should().Be(2);
        record.ChunksList.Should().Equal("chunk-1", "chunk-2");
        record.ChunkSnapshots.Should().Equal(
            new DocumentChunkSnapshot("chunk-1", 10, 0, "doc.md"),
            new DocumentChunkSnapshot("chunk-2", 8, 1, "doc.md"));
    }

    [Fact]
    public async Task FailAfterChunking_ProcessingDocument_PreservesChunksAndError()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "doc.md");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.RecordChunksAsync("workspace-a", "doc-1", CreateChunks("doc-1"));

        await service.MarkFailedAsync("workspace-a", "doc-1", "embedding", "boom");

        var record = await store.GetAsync("workspace-a", "doc-1");
        record.Should().NotBeNull();
        record!.Status.Should().Be(DocumentLifecycleStatus.Failed);
        record.ErrorMessage.Should().Be("boom");
        record.Metadata.Should().Contain("failure_stage", "embedding");
        record.ChunksList.Should().Equal("chunk-1", "chunk-2");
        record.ChunkSnapshots.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailBeforeChunking_ExistingFailedDocument_PreservesPreviousChunkSnapshot()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "doc.md");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.RecordChunksAsync("workspace-a", "doc-1", CreateChunks("doc-1"));
        await service.MarkFailedAsync("workspace-a", "doc-1", "embedding", "old error");
        await service.StartProcessingAsync("workspace-a", "doc-1");

        await service.MarkFailedAsync("workspace-a", "doc-1", "chunking", "chunking failed");

        var record = await store.GetAsync("workspace-a", "doc-1");
        record.Should().NotBeNull();
        record!.Status.Should().Be(DocumentLifecycleStatus.Failed);
        record.ErrorMessage.Should().Be("chunking failed");
        record.Metadata.Should().Contain("failure_stage", "chunking");
        record.ChunksList.Should().Equal("chunk-1", "chunk-2");
        record.ChunkSnapshots.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkProcessed_ProcessingDocument_WritesProcessedStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.MarkFailedAsync("workspace-a", "doc-1", "previous", "old error");
        await service.StartProcessingAsync("workspace-a", "doc-1");

        await service.MarkProcessedAsync("workspace-a", "doc-1");

        var record = await store.GetAsync("workspace-a", "doc-1");
        record.Should().NotBeNull();
        record!.Status.Should().Be(DocumentLifecycleStatus.Processed);
        record.ErrorMessage.Should().BeEmpty();
        record.Metadata.Should().NotContainKey("failure_stage");
    }

    [Fact]
    public async Task DuplicateDocument_SameWorkspace_ReturnsExistingStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        var first = await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "first.md");
        await service.MarkProcessedAsync("workspace-a", "doc-1");

        var duplicate = await service.PrepareIngestionAsync("replacement", docId: "doc-1", filePath: "second.md");

        duplicate.IsDuplicate.Should().BeTrue();
        duplicate.StatusRecord.Status.Should().Be(DocumentLifecycleStatus.Processed);
        duplicate.StatusRecord.FilePath.Should().Be("first.md");
        duplicate.StatusRecord.CreatedAt.Should().Be(first.StatusRecord.CreatedAt);
        duplicate.StatusRecord.ContentLength.Should().Be("content".Length);
    }

    [Fact]
    public async Task PrepareIngestion_FailedDocument_RefreshesRetryMetadataAndPreservesChunkSnapshots()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        var first = await service.PrepareIngestionAsync(
            "old content",
            docId: "doc-1",
            filePath: "old.md",
            trackId: "old-track");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.RecordChunksAsync("workspace-a", "doc-1", CreateChunks("doc-1"));
        await service.MarkFailedAsync("workspace-a", "doc-1", "embedding", "old error");
        var failed = await store.GetAsync("workspace-a", "doc-1");
        failed.Should().NotBeNull();
        await Task.Delay(10);

        var retry = await service.PrepareIngestionAsync(
            "  new replacement content  ",
            docId: "doc-1",
            filePath: "new.md",
            trackId: "new-track");

        retry.IsDuplicate.Should().BeFalse();
        retry.StatusRecord.Status.Should().Be(DocumentLifecycleStatus.Pending);
        retry.StatusRecord.ContentSummary.Should().Be("new replacement content");
        retry.StatusRecord.ContentLength.Should().Be("  new replacement content  ".Length);
        retry.StatusRecord.FilePath.Should().Be("new.md");
        retry.StatusRecord.TrackId.Should().Be("new-track");
        retry.StatusRecord.ErrorMessage.Should().BeEmpty();
        retry.StatusRecord.Metadata.Should().NotContainKey("failure_stage");
        retry.StatusRecord.CreatedAt.Should().Be(first.StatusRecord.CreatedAt);
        retry.StatusRecord.UpdatedAt.Should().BeAfter(failed!.UpdatedAt);
        retry.StatusRecord.ChunksList.Should().Equal("chunk-1", "chunk-2");
        retry.StatusRecord.ChunkSnapshots.Should().Equal(
            new DocumentChunkSnapshot("chunk-1", 10, 0, "doc.md"),
            new DocumentChunkSnapshot("chunk-2", 8, 1, "doc.md"));
    }

    [Fact]
    public async Task DuplicateDocument_DifferentWorkspace_AllowsSeparateStatus()
    {
        var store = new InMemoryDocumentStatusStore();
        var workspaceA = CreateService(store, workspace: "workspace-a");
        var workspaceB = CreateService(store, workspace: "workspace-b");

        var first = await workspaceA.PrepareIngestionAsync("content", docId: "doc-1", filePath: "a.md");
        var second = await workspaceB.PrepareIngestionAsync("content", docId: "doc-1", filePath: "b.md");

        first.IsDuplicate.Should().BeFalse();
        second.IsDuplicate.Should().BeFalse();
        first.Workspace.Should().Be("workspace-a");
        second.Workspace.Should().Be("workspace-b");
        second.StatusRecord.FilePath.Should().Be("b.md");
    }

    [Fact]
    public async Task CreateDeletionPlan_ProcessedDocument_IncludesKnownChunkIds()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);

        var plan = await service.CreateDeletionPlanAsync(" workspace-a ", "doc-1", deleteLlmCache: true);

        plan.Found.Should().BeTrue();
        plan.DocId.Should().Be("doc-1");
        plan.Workspace.Should().Be("workspace-a");
        plan.ChunkIds.Should().Equal("chunk-1", "chunk-2");
        plan.ChunkSnapshots.Should().HaveCount(2);
        plan.DeleteFullDocument.Should().BeTrue();
        plan.DeleteTextChunks.Should().BeTrue();
        plan.DeleteChunkVectors.Should().BeTrue();
        plan.DeleteDocumentGraphMetadata.Should().BeTrue();
        plan.DeleteLlmCache.Should().BeTrue();
    }

    [Fact]
    public async Task MarkDeletionFailed_WhenStageFails_RecordsRetryMetadata()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);

        var result = await service.MarkDeletionFailedAsync(
            "workspace-a",
            "doc-1",
            "vectors",
            "delete failed");

        result.Found.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Stage.Should().Be("vectors");
        result.Message.Should().Be("delete failed");

        var stored = await store.GetAsync("workspace-a", "doc-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        stored.ErrorMessage.Should().Be("delete failed");
        stored.Metadata.Should().Contain("deletion_failed", true);
        stored.Metadata.Should().Contain("deletion_failure_stage", "vectors");
    }

    [Fact]
    public async Task MarkDeletionStartedAsync_WhenProcessed_MarksDeletingAndClearsPreviousFailure()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);
        await service.MarkDeletionFailedAsync(
            "workspace-a",
            "doc-1",
            "delete_chunk_vectors",
            "qdrant failed",
            ["cache-a"]);

        await service.MarkDeletionStartedAsync("workspace-a", "doc-1");

        var stored = await store.GetAsync("workspace-a", "doc-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DocumentLifecycleStatus.Deleting);
        stored.ErrorMessage.Should().BeEmpty();
        stored.Metadata.Should().NotContainKey("deletion_failed");
        stored.Metadata.Should().NotContainKey("deletion_failure_stage");
        stored.Metadata.Should().NotContainKey("deletion_llm_cache_ids");
    }

    [Fact]
    public async Task MarkDeletionSucceededAsync_WhenDeleting_DeletesStatusRecord()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);
        await service.MarkDeletionStartedAsync("workspace-a", "doc-1");

        await service.MarkDeletionSucceededAsync("workspace-a", "doc-1");

        var stored = await store.GetAsync("workspace-a", "doc-1");
        stored.Should().BeNull();
    }

    [Fact]
    public async Task MarkDeletionFailedAsync_WithCacheIds_PreservesRetryMetadata()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);

        await service.MarkDeletionFailedAsync(
            "workspace-a",
            "doc-1",
            "delete_llm_cache",
            "cache failed",
            ["cache-a", "cache-b"]);

        var stored = await store.GetAsync("workspace-a", "doc-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        stored.Metadata["deletion_failure_stage"].Should().Be("delete_llm_cache");
        stored.Metadata["deletion_llm_cache_ids"].Should().BeEquivalentTo(new[] { "cache-a", "cache-b" });
    }

    [Fact]
    public async Task CreateDeletionPlan_DeletionFailedDocument_RemainsRetryable()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "doc.md");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.RecordChunksAsync("workspace-a", "doc-1", CreateChunks("doc-1"));
        await service.MarkDeletionFailedAsync("workspace-a", "doc-1", "vectors", "delete failed");

        var plan = await service.CreateDeletionPlanAsync("workspace-a", "doc-1");

        plan.Found.Should().BeTrue();
        plan.ChunkIds.Should().Equal("chunk-1", "chunk-2");
        plan.DeleteTextChunks.Should().BeTrue();
        plan.ChunkSnapshots.Should().Equal(
            new DocumentChunkSnapshot("chunk-1", 10, 0, "doc.md"),
            new DocumentChunkSnapshot("chunk-2", 8, 1, "doc.md"));
    }

    [Fact]
    public async Task PrepareIngestion_DeletionFailedDocument_DoesNotResetDeletionStateAndMetadata()
    {
        var store = new InMemoryDocumentStatusStore();
        var service = CreateService(store);
        await PrepareProcessedDocumentAsync(service);
        await service.MarkDeletionFailedAsync("workspace-a", "doc-1", "vectors", "delete failed");

        var result = await service.PrepareIngestionAsync(
            "replacement content",
            docId: "doc-1",
            filePath: "replacement.md",
            trackId: "replacement-track");

        result.IsDuplicate.Should().BeTrue();
        result.StatusRecord.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        result.StatusRecord.FilePath.Should().Be("doc.md");
        result.StatusRecord.ContentSummary.Should().Be("content");
        result.StatusRecord.ContentLength.Should().Be("content".Length);
        result.StatusRecord.TrackId.Should().Be("track-doc-1");
        result.StatusRecord.ErrorMessage.Should().Be("delete failed");
        result.StatusRecord.Metadata.Should().Contain("deletion_failed", true);
        result.StatusRecord.Metadata.Should().Contain("deletion_failure_stage", "vectors");
        result.StatusRecord.Metadata.Should().NotContainKey("failure_stage");

        var stored = await store.GetAsync("workspace-a", "doc-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DocumentLifecycleStatus.DeletionFailed);
        stored.Metadata.Should().Contain("deletion_failed", true);
        stored.Metadata.Should().Contain("deletion_failure_stage", "vectors");
    }

    [Fact]
    public async Task CreateDeletionPlan_UnknownDocument_ReturnsNotFound()
    {
        var service = CreateService(new InMemoryDocumentStatusStore());

        var plan = await service.CreateDeletionPlanAsync("workspace-a", "missing-doc");

        plan.Found.Should().BeFalse();
        plan.DocId.Should().Be("missing-doc");
        plan.Workspace.Should().Be("workspace-a");
        plan.ChunkIds.Should().BeEmpty();
        plan.ChunkSnapshots.Should().BeEmpty();
        plan.DeleteFullDocument.Should().BeFalse();
    }

    private static DocumentLifecycleService CreateService(
        InMemoryDocumentStatusStore store,
        string workspace = "workspace-a",
        ILogger<DocumentLifecycleService>? logger = null)
    {
        return new DocumentLifecycleService(
            store,
            Options.Create(new LightRAGOptions
            {
                Workspace = workspace
            }),
            logger ?? NullLogger<DocumentLifecycleService>.Instance);
    }

    private static async Task PrepareProcessedDocumentAsync(DocumentLifecycleService service)
    {
        await service.PrepareIngestionAsync("content", docId: "doc-1", filePath: "doc.md");
        await service.StartProcessingAsync("workspace-a", "doc-1");
        await service.RecordChunksAsync("workspace-a", "doc-1", CreateChunks("doc-1"));
        await service.MarkProcessedAsync("workspace-a", "doc-1");
    }

    private static IReadOnlyList<Chunk> CreateChunks(string docId)
    {
        return
        [
            new Chunk
            {
                Id = "chunk-1",
                FullDocId = docId,
                Tokens = 10,
                ChunkOrderIndex = 0,
                FilePath = "doc.md"
            },
            new Chunk
            {
                Id = "chunk-2",
                FullDocId = docId,
                Tokens = 8,
                ChunkOrderIndex = 1,
                FilePath = "doc.md"
            }
        ];
    }

    private sealed class TestLogger : ILogger<DocumentLifecycleService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
