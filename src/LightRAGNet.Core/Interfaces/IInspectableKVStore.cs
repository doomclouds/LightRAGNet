namespace LightRAGNet.Core.Interfaces;

public interface IInspectableKVStore
{
    Task<IReadOnlyDictionary<string, Dictionary<string, object>>> SnapshotAsync(
        CancellationToken cancellationToken = default);
}
