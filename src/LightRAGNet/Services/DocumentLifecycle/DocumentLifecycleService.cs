using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.DocumentLifecycle;

public sealed class DocumentLifecycleService
{
    private const string DefaultWorkspace = "_";
    private const string DefaultFilePath = "unknown_source";
    private const int SummaryMaxLength = 120;

    private readonly IDocumentStatusStore _statusStore;
    private readonly LightRAGOptions _options;
    private readonly ILogger<DocumentLifecycleService> _logger;

    public DocumentLifecycleService(
        IDocumentStatusStore statusStore,
        IOptions<LightRAGOptions> options,
        ILogger<DocumentLifecycleService> logger)
    {
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentIngestionResult> PrepareIngestionAsync(
        string content,
        string? docId = null,
        string? filePath = null,
        string? trackId = null,
        CancellationToken cancellationToken = default)
    {
        var workspace = NormalizeWorkspace(_options.Workspace);
        var resolvedDocId = string.IsNullOrWhiteSpace(docId)
            ? HashUtils.ComputeMd5Hash(content, "doc-")
            : docId;

        var existing = await _statusStore.GetAsync(workspace, resolvedDocId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == DocumentLifecycleStatus.Failed)
            {
                _logger.LogDebug(
                    "Document {DocId} in workspace {Workspace} is {Status}; refreshing lifecycle metadata for retry.",
                    resolvedDocId,
                    workspace,
                    existing.Status);

                existing.Status = DocumentLifecycleStatus.Pending;
                existing.ContentSummary = CreateSummary(content);
                existing.ContentLength = content.Length;
                existing.FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
                existing.TrackId = string.IsNullOrWhiteSpace(trackId) ? $"track-{resolvedDocId}" : trackId;
                existing.ErrorMessage = string.Empty;
                existing.Metadata.Remove("failure_stage");
                Touch(existing);

                await _statusStore.UpsertAsync(existing, cancellationToken);

                return new DocumentIngestionResult(
                    resolvedDocId,
                    workspace,
                    IsDuplicate: false,
                    existing);
            }

            _logger.LogDebug(
                "Document {DocId} already exists in workspace {Workspace}.",
                resolvedDocId,
                workspace);

            return new DocumentIngestionResult(
                resolvedDocId,
                workspace,
                IsDuplicate: true,
                existing);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new DocumentStatusRecord
        {
            DocId = resolvedDocId,
            Workspace = workspace,
            Status = DocumentLifecycleStatus.Pending,
            ContentSummary = CreateSummary(content),
            ContentLength = content.Length,
            FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath,
            TrackId = string.IsNullOrWhiteSpace(trackId) ? $"track-{resolvedDocId}" : trackId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _statusStore.UpsertAsync(record, cancellationToken);

        return new DocumentIngestionResult(
            resolvedDocId,
            workspace,
            IsDuplicate: false,
            record);
    }

    public async Task StartProcessingAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            LogMissingStatusMutation(normalizedWorkspace, docId, nameof(StartProcessingAsync));
            return;
        }

        record.Status = DocumentLifecycleStatus.Processing;
        record.ErrorMessage = string.Empty;
        record.Metadata.Remove("failure_stage");
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
    }

    public async Task RecordChunksAsync(
        string workspace,
        string docId,
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            LogMissingStatusMutation(normalizedWorkspace, docId, nameof(RecordChunksAsync));
            return;
        }

        record.ChunksList = [.. chunks.Select(chunk => chunk.Id)];
        record.ChunksCount = chunks.Count;
        record.ChunkSnapshots =
        [
            .. chunks.Select(chunk => new DocumentChunkSnapshot(
                chunk.Id,
                chunk.Tokens,
                chunk.ChunkOrderIndex,
                string.IsNullOrWhiteSpace(chunk.FilePath) ? record.FilePath : chunk.FilePath))
        ];
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
    }

    public async Task MarkProcessedAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            LogMissingStatusMutation(normalizedWorkspace, docId, nameof(MarkProcessedAsync));
            return;
        }

        record.Status = DocumentLifecycleStatus.Processed;
        record.ErrorMessage = string.Empty;
        record.Metadata.Remove("failure_stage");
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
    }

    public async Task MarkFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            LogMissingStatusMutation(normalizedWorkspace, docId, nameof(MarkFailedAsync));
            return;
        }

        record.Status = DocumentLifecycleStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.Metadata["failure_stage"] = stage;
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
    }

    public async Task<DocumentDeletionPlan> CreateDeletionPlanAsync(
        string workspace,
        string docId,
        bool deleteLlmCache = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionPlan
            {
                DocId = docId,
                Workspace = normalizedWorkspace,
                Found = false
            };
        }

        List<string> chunkIds = record.ChunksList.Count > 0
            ? [.. record.ChunksList]
            : [.. record.ChunkSnapshots.Select(snapshot => snapshot.ChunkId)];
        var hasChunks = chunkIds.Count > 0 || record.ChunksCount > 0;

        return new DocumentDeletionPlan
        {
            DocId = docId,
            Workspace = normalizedWorkspace,
            Found = true,
            ChunkIds = chunkIds,
            ChunkSnapshots = [.. record.ChunkSnapshots],
            DeleteFullDocument = true,
            DeleteTextChunks = hasChunks,
            DeleteChunkVectors = hasChunks,
            DeleteDocumentGraphMetadata = true,
            DeleteLlmCache = deleteLlmCache
        };
    }

    public async Task<DocumentDeletionResult> MarkDeletionFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        return await MarkDeletionFailedAsync(
            workspace,
            docId,
            stage,
            errorMessage,
            llmCacheIds: null,
            cancellationToken);
    }

    public async Task MarkDeletionStartedAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            LogMissingStatusMutation(normalizedWorkspace, docId, nameof(MarkDeletionStartedAsync));
            return;
        }

        record.Status = DocumentLifecycleStatus.Deleting;
        record.ErrorMessage = string.Empty;
        record.Metadata.Remove("deletion_failed");
        record.Metadata.Remove("deletion_failure_stage");
        Touch(record);
        await _statusStore.UpsertAsync(record, cancellationToken);
    }

    public async Task MarkDeletionSucceededAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        await _statusStore.DeleteAsync(normalizedWorkspace, docId, cancellationToken);
    }

    public async Task<DocumentDeletionResult> MarkDeletionFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        IReadOnlyCollection<string>? llmCacheIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspace);
        var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionResult(
                docId,
                normalizedWorkspace,
                Found: false,
                Succeeded: false,
                stage,
                errorMessage);
        }

        record.Status = DocumentLifecycleStatus.DeletionFailed;
        record.ErrorMessage = errorMessage;
        record.Metadata["deletion_failed"] = true;
        record.Metadata["deletion_failure_stage"] = stage;
        if (llmCacheIds is not null)
        {
            record.Metadata["deletion_llm_cache_ids"] = llmCacheIds.ToArray();
        }
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);

        return new DocumentDeletionResult(
            docId,
            normalizedWorkspace,
            Found: true,
            Succeeded: false,
            stage,
            errorMessage);
    }

    private static string NormalizeWorkspace(string? workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? DefaultWorkspace : workspace.Trim();
    }

    private static string CreateSummary(string content)
    {
        var summary = content.Trim();
        return summary.Length <= SummaryMaxLength
            ? summary
            : summary[..SummaryMaxLength];
    }

    private static void Touch(DocumentStatusRecord record)
    {
        record.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void LogMissingStatusMutation(string workspace, string docId, string operation)
    {
        _logger.LogWarning(
            "Document lifecycle status mutation {Operation} skipped because document {DocId} was not found in workspace {Workspace}.",
            operation,
            docId,
            workspace);
    }
}
