using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryVectorStore : IVectorStore
{
    public Dictionary<string, Dictionary<string, VectorDocument>> Collections { get; } = [];

    public List<(string Collection, IReadOnlyList<string> Ids)> DeleteCalls { get; } = [];
    public List<(string Collection, IReadOnlyList<VectorDocument> Documents)> UpsertCalls { get; } = [];

    public string? ThrowOnDeleteCollection { get; set; }
    public string? ThrowOnUpsertCollection { get; set; }

    public void Seed(string collection, VectorDocument document)
    {
        GetCollection(collection)[document.Id] = Clone(document);
    }

    public VectorDocument? Get(string collection, string id)
    {
        return GetCollection(collection).TryGetValue(id, out var document)
            ? Clone(document)
            : null;
    }

    public Task<List<SearchResult>> QueryAsync(
        string collection,
        string query,
        int topK,
        float[]? queryEmbedding = null,
        float threshold = 0.2f,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new List<SearchResult>());
    }

    public Task UpsertAsync(
        string collection,
        IEnumerable<VectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var docsList = documents.Select(Clone).ToList();
        UpsertCalls.Add((collection, docsList));

        if (string.Equals(ThrowOnUpsertCollection, collection, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Upsert failed for collection '{collection}'.");
        }

        var collectionItems = GetCollection(collection);
        foreach (var document in docsList)
        {
            collectionItems[document.Id] = Clone(document);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idsList = ids.ToList();
        DeleteCalls.Add((collection, idsList));

        if (string.Equals(ThrowOnDeleteCollection, collection, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Delete failed for collection '{collection}'.");
        }

        var collectionItems = GetCollection(collection);
        foreach (var id in idsList)
        {
            collectionItems.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<VectorDocument?> GetByIdAsync(
        string collection,
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Get(collection, id));
    }

    public Task<List<VectorDocument>> GetByIdsAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var documents = ids
            .Select(id => Get(collection, id))
            .OfType<VectorDocument>()
            .ToList();

        return Task.FromResult(documents);
    }

    private Dictionary<string, VectorDocument> GetCollection(string collection)
    {
        if (!Collections.TryGetValue(collection, out var items))
        {
            items = new Dictionary<string, VectorDocument>(StringComparer.Ordinal);
            Collections[collection] = items;
        }

        return items;
    }

    private static VectorDocument Clone(VectorDocument document)
    {
        return new VectorDocument
        {
            Id = document.Id,
            Vector = [.. document.Vector],
            Metadata = document.Metadata.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            Content = document.Content
        };
    }
}
