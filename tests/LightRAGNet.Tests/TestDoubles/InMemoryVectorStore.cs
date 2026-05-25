using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, Dictionary<string, VectorDocument>> collections = [];

    public Dictionary<string, Dictionary<string, VectorDocument>> Collections => collections.ToDictionary(
        collection => collection.Key,
        collection => collection.Value.ToDictionary(
            document => document.Key,
            document => Clone(document.Value),
            StringComparer.Ordinal),
        StringComparer.Ordinal);

    public List<(string Collection, IReadOnlyList<string> Ids)> DeleteCalls { get; } = [];
    public List<(string Collection, IReadOnlyList<string> Ids)> GetByIdsCalls { get; } = [];
    public List<(string Collection, string Query, int TopK, float Threshold)> QueryCalls { get; } = [];
    public List<(string Collection, IReadOnlyList<VectorDocument> Documents)> UpsertCalls { get; } = [];
    public Dictionary<string, float> QueryScoresByDocumentId { get; } = new(StringComparer.Ordinal);

    public int? QueryCandidateCountOverride { get; set; }
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
        QueryCalls.Add((collection, query, topK, threshold));

        var scoredDocuments = GetCollection(collection)
            .Values
            .Select(document => new SearchResult
            {
                Id = document.Id,
                Score = QueryScoresByDocumentId.TryGetValue(document.Id, out var score) ? score : 1.0f,
                Metadata = Clone(document.Metadata),
                Content = document.Content
            });

        if (QueryScoresByDocumentId.Count > 0)
        {
            scoredDocuments = scoredDocuments
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id, StringComparer.Ordinal);
        }

        var results = scoredDocuments
            .Take(QueryCandidateCountOverride ?? topK)
            .ToList();

        return Task.FromResult(results);
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

        var idsList = ids.ToList();
        GetByIdsCalls.Add((collection, idsList));

        var documents = idsList
            .Select(id => Get(collection, id))
            .OfType<VectorDocument>()
            .ToList();

        return Task.FromResult(documents);
    }

    private Dictionary<string, VectorDocument> GetCollection(string collection)
    {
        if (!collections.TryGetValue(collection, out var items))
        {
            items = new Dictionary<string, VectorDocument>(StringComparer.Ordinal);
            collections[collection] = items;
        }

        return items;
    }

    private static VectorDocument Clone(VectorDocument document)
    {
        return new VectorDocument
        {
            Id = document.Id,
            Vector = [.. document.Vector],
            Metadata = Clone(document.Metadata),
            Content = document.Content
        };
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => CloneValue(pair.Value),
            StringComparer.Ordinal);
    }

    private static object CloneValue(object value)
    {
        return value switch
        {
            Dictionary<string, object> dictionary => Clone(dictionary),
            List<object> list => list.Select(CloneValue).ToList(),
            List<string> list => list.ToList(),
            float[] vector => vector.ToArray(),
            VectorDocument document => Clone(document),
            _ => value
        };
    }
}
