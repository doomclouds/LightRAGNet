using LightRAGNet.Embedding;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class EmbeddingConfigHealthCheck(IOptions<AliyunEmbeddingOptions> options) : ISystemHealthCheck
{
    public string Id => "embedding-config";

    public string Name => "Embedding config";

    public string Category => "Providers";

    public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = options.Value;
        var (configured, source) = GetApiKeySource(value.ApiKey);
        var dimensionConfigured = value.Dimension > 0;
        var healthy = configured && dimensionConfigured;
        var evidence = new Dictionary<string, object?>
        {
            ["configured"] = configured,
            ["source"] = source,
            ["model"] = value.ModelName,
            ["baseUrl"] = value.BaseUrl,
            ["dimension"] = value.Dimension
        };

        var result = healthy
            ? SystemHealthCheckResult.Healthy(Id, Name, Category, "Embedding configuration is present.", evidence)
            : SystemHealthCheckResult.Unhealthy(
                Id,
                Name,
                Category,
                "Embedding configuration is incomplete.",
                "Configure Embedding:ApiKey or DASHSCOPE_API_KEY, and set Embedding:Dimension to a positive value.",
                ["Document Indexing"],
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
