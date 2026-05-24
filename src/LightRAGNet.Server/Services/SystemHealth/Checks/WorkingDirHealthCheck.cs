using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class WorkingDirHealthCheck(
    IOptions<DocumentArtifactStoreOptions> options,
    ILogger<WorkingDirHealthCheck> logger) : ISystemHealthCheck
{
    public string Id => "working-dir";

    public string Name => "WorkingDir";

    public string Category => "Storage";

    public async Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = options.Value.RootPath;
        var probePath = Path.Combine(path, $".health-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);

            return SystemHealthCheckResult.Healthy(
                Id,
                Name,
                Category,
                "Working directory is writable.",
                new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["writable"] = true
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogWarning(exception, "Working directory health probe failed for {Path}.", path);

            return SystemHealthCheckResult.Unhealthy(
                Id,
                Name,
                Category,
                "Working directory is not writable.",
                "Verify LightRAG:WorkingDir exists and the server process can write to it.",
                ["RAG Storage and Artifacts"],
                new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["writable"] = false
                });
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to delete working directory health probe {ProbePath}.", probePath);
            }
        }
    }
}
