using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationSecretProvider(
    IOptions<RagasEvaluationOptions> options,
    Func<string, string?>? getEnvironmentVariable = null)
{
    internal const string DeepSeekApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";

    private readonly Func<string, string?> getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    public string GetEvaluatorApiKey()
    {
        var configured = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return getEnvironmentVariable(DeepSeekApiKeyEnvironmentVariable) ?? string.Empty;
    }

    public IReadOnlyList<string> GetSecretValues()
    {
        return new[]
            {
                options.Value.AdminToken,
                options.Value.ApiKey,
                getEnvironmentVariable(DeepSeekApiKeyEnvironmentVariable),
                GetEvaluatorApiKey()
            }
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Select(secret => secret!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
