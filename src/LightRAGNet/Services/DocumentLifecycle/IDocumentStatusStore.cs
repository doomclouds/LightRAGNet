namespace LightRAGNet.Services.DocumentLifecycle;

public interface IDocumentStatusStore
{
    Task<DocumentStatusRecord?> GetAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        DocumentStatusRecord record,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string workspace,
        string docId,
        CancellationToken cancellationToken = default);
}
