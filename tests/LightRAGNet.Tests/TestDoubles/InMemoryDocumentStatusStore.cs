using System.Collections.Concurrent;
using LightRAGNet.Services.DocumentLifecycle;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryDocumentStatusStore : IDocumentStatusStore
{
    private readonly ConcurrentDictionary<(string Workspace, string DocId), DocumentStatusRecord> _records = [];

    public Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _records.TryGetValue((workspace, docId), out var record)
                ? Clone(record)
                : null);
    }

    public Task UpsertAsync(
        DocumentStatusRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _records[(record.Workspace, record.DocId)] = Clone(record);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _records.TryRemove((workspace, docId), out _);
        return Task.CompletedTask;
    }

    private static DocumentStatusRecord Clone(DocumentStatusRecord source)
    {
        return new DocumentStatusRecord
        {
            DocId = source.DocId,
            Workspace = source.Workspace,
            Status = source.Status,
            ContentSummary = source.ContentSummary,
            ContentLength = source.ContentLength,
            ChunksCount = source.ChunksCount,
            ChunksList = [.. source.ChunksList],
            ChunkSnapshots = [.. source.ChunkSnapshots],
            FilePath = source.FilePath,
            TrackId = source.TrackId,
            ErrorMessage = source.ErrorMessage,
            Metadata = source.Metadata.ToDictionary(
                pair => pair.Key,
                pair => pair.Value),
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
