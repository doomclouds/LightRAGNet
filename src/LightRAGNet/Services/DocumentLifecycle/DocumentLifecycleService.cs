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

    public async Task<DocumentStatusRecord?> StartProcessingAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        record.Status = DocumentLifecycleStatus.Processing;
        record.ErrorMessage = string.Empty;
        record.Metadata.Remove("failure_stage");
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
        return record;
    }

    public async Task<DocumentStatusRecord?> RecordChunksAsync(
        string workspace,
        string docId,
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return null;
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
        return record;
    }

    public async Task<DocumentStatusRecord?> MarkProcessedAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        record.Status = DocumentLifecycleStatus.Processed;
        record.ErrorMessage = string.Empty;
        record.Metadata.Remove("failure_stage");
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
        return record;
    }

    public async Task<DocumentStatusRecord?> MarkFailedAsync(
        string workspace,
        string docId,
        string stage,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        record.Status = DocumentLifecycleStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.Metadata["failure_stage"] = stage;
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);
        return record;
    }

    public async Task<DocumentDeletionPlan> CreateDeletionPlanAsync(
        string workspace,
        string docId,
        bool deleteLlmCache = false,
        CancellationToken cancellationToken = default)
    {
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionPlan
            {
                DocId = docId,
                Workspace = workspace,
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
            Workspace = workspace,
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
        var record = await _statusStore.GetAsync(workspace, docId, cancellationToken);
        if (record is null)
        {
            return new DocumentDeletionResult(
                docId,
                workspace,
                Found: false,
                Succeeded: false,
                stage,
                errorMessage);
        }

        record.Status = DocumentLifecycleStatus.DeletionFailed;
        record.ErrorMessage = errorMessage;
        record.Metadata["deletion_failed"] = true;
        record.Metadata["deletion_failure_stage"] = stage;
        Touch(record);

        await _statusStore.UpsertAsync(record, cancellationToken);

        return new DocumentDeletionResult(
            docId,
            workspace,
            Found: true,
            Succeeded: false,
            stage,
            errorMessage);
    }

    private static string NormalizeWorkspace(string? workspace)
    {
        return string.IsNullOrWhiteSpace(workspace) ? DefaultWorkspace : workspace;
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
}
