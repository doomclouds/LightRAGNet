using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryKvStore : IKVStore
{
    public Dictionary<string, Dictionary<string, object>> Items { get; } = [];
    public List<IReadOnlyList<string>> DeleteCalls { get; } = [];
    public List<Dictionary<string, Dictionary<string, object>>> UpsertCalls { get; } = [];

    public string? ThrowOnDeleteKey { get; set; }
    public string? ThrowOnUpsertKey { get; set; }

    public void Seed(string id, Dictionary<string, object> value)
    {
        Items[id] = Clone(value);
    }

    public Task<Dictionary<string, object>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Items.TryGetValue(id, out var item) ? Clone(item) : null);
    }

    public Task<List<Dictionary<string, object>>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = ids
            .Where(Items.ContainsKey)
            .Select(id => Clone(Items[id]))
            .ToList();

        return Task.FromResult(items);
    }

    public Task<HashSet<string>> FilterKeysAsync(
        HashSet<string> keys,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(keys.Where(Items.ContainsKey).ToHashSet(StringComparer.Ordinal));
    }

    public Task UpsertAsync(
        Dictionary<string, Dictionary<string, object>> data,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clonedData = data.ToDictionary(
            pair => pair.Key,
            pair => Clone(pair.Value),
            StringComparer.Ordinal);

        UpsertCalls.Add(clonedData);

        if (ThrowOnUpsertKey is not null && clonedData.ContainsKey(ThrowOnUpsertKey))
        {
            throw new InvalidOperationException($"Upsert failed for key '{ThrowOnUpsertKey}'.");
        }

        foreach (var (id, value) in clonedData)
        {
            Items[id] = Clone(value);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idsList = ids.ToList();
        DeleteCalls.Add(idsList);

        if (ThrowOnDeleteKey is not null && idsList.Contains(ThrowOnDeleteKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Delete failed for key '{ThrowOnDeleteKey}'.");
        }

        foreach (var id in idsList)
        {
            Items.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Items.Count == 0);
    }

    public Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DropAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Items.Clear();
        return Task.CompletedTask;
    }

    private static Dictionary<string, object> Clone(Dictionary<string, object> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
