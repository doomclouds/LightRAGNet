namespace LightRAGNet.Server.Services;

public interface IRagExternalStorageCleaner
{
    Task<IReadOnlyList<string>> ClearAsync(CancellationToken cancellationToken = default);
}
