using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Tests.TestDoubles;

public sealed class InMemoryKvStore : IKVStore
{
    private readonly Dictionary<string, Dictionary<string, object>> items = [];

    public Dictionary<string, Dictionary<string, object>> Items => items.ToDictionary(
        pair => pair.Key,
        pair => Clone(pair.Value),
        StringComparer.Ordinal);
    public List<IReadOnlyList<string>> DeleteCalls { get; } = [];
    public List<Dictionary<string, Dictionary<string, object>>> UpsertCalls { get; } = [];

    public string? ThrowOnDeleteKey { get; set; }
    public string? ThrowOnUpsertKey { get; set; }

    public void Seed(string id, Dictionary<string, object> value)
    {
        items[id] = Clone(value);
    }

    public Task<Dictionary<string, object>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.TryGetValue(id, out var item) ? Clone(item) : null);
    }

    public Task<List<Dictionary<string, object>>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = ids
            .Where(items.ContainsKey)
            .Select(id => Clone(items[id]))
            .ToList();

        return Task.FromResult(values);
    }

    public Task<HashSet<string>> FilterKeysAsync(
        HashSet<string> keys,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(keys.Where(key => !items.ContainsKey(key)).ToHashSet(StringComparer.Ordinal));
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
            items[id] = Clone(value);
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
            items.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count == 0);
    }

    public Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DropAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        items.Clear();
        return Task.CompletedTask;
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
            _ => value
        };
    }
}
