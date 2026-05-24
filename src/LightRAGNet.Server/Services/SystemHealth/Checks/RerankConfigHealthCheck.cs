using LightRAGNet.Rerank;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class RerankConfigHealthCheck(IOptions<AliyunRerankOptions> options) : ISystemHealthCheck
{
    public string Id => "rerank-config";

    public string Name => "Rerank config";

    public string Category => "Providers";

    public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = options.Value;
        var (configured, source) = GetApiKeySource(value.ApiKey);
        var evidence = new Dictionary<string, object?>
        {
            ["configured"] = configured,
            ["source"] = source,
            ["model"] = value.ModelName,
            ["baseUrl"] = value.BaseUrl
        };

        var result = configured
            ? SystemHealthCheckResult.Healthy(Id, Name, Category, "Rerank configuration is present.", evidence)
            : SystemHealthCheckResult.Degraded(
                Id,
                Name,
                Category,
                "Rerank API key is missing.",
                "Configure Rerank:ApiKey or set the DASHSCOPE_API_KEY environment variable.",
                ["Rerank Quality"],
                evidence);

        return Task.FromResult(result);
    }

    private static (bool Configured, string Source) GetApiKeySource(string? configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return (true, "options");
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY"))
            ? (true, "environment")
            : (false, "missing");
    }
}
