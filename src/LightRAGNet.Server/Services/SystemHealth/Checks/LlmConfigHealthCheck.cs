using LightRAGNet.LLM;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth.Checks;

public sealed class LlmConfigHealthCheck(IOptions<DeepSeekOptions> options) : ISystemHealthCheck
{
    public string Id => "llm-config";

    public string Name => "LLM config";

    public string Category => "Providers";

    public Task<SystemHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = options.Value;
        var (configured, source) = GetApiKeySource(value.ApiKey, "DEEPSEEK_API_KEY");
        var evidence = new Dictionary<string, object?>
        {
            ["configured"] = configured,
            ["source"] = source,
            ["model"] = value.ModelName,
            ["baseUrl"] = value.BaseUrl
        };

        var result = configured
            ? SystemHealthCheckResult.Healthy(Id, Name, Category, "LLM configuration is present.", evidence)
            : SystemHealthCheckResult.Unhealthy(
                Id,
                Name,
                Category,
                "LLM API key is missing.",
                "Configure LLM:ApiKey or set the DEEPSEEK_API_KEY environment variable.",
                ["LLM Generation"],
                evidence);

        return Task.FromResult(result);
    }

    private static (bool Configured, string Source) GetApiKeySource(string? configuredValue, string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return (true, "options");
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable))
            ? (true, "environment")
            : (false, "missing");
    }
}
