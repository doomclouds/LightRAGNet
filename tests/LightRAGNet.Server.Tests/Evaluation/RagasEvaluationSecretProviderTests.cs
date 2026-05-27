using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationSecretProviderTests
{
    [Fact]
    public void GetEvaluatorApiKey_WhenConfigured_UsesConfiguredValue()
    {
        var provider = CreateProvider(
            new RagasEvaluationOptions { ApiKey = "configured-key" },
            _ => "environment-key");

        var apiKey = provider.GetEvaluatorApiKey();

        apiKey.Should().Be("configured-key");
    }

    [Fact]
    public void GetEvaluatorApiKey_WhenConfigIsBlank_UsesDeepSeekEnvironmentVariable()
    {
        var provider = CreateProvider(
            new RagasEvaluationOptions { ApiKey = " " },
            name => name == "DEEPSEEK_API_KEY" ? "environment-key" : null);

        var apiKey = provider.GetEvaluatorApiKey();

        apiKey.Should().Be("environment-key");
    }

    [Fact]
    public void GetSecretValues_IncludesConfiguredAndEnvironmentSecrets()
    {
        var provider = CreateProvider(
            new RagasEvaluationOptions
            {
                AdminToken = "admin-token",
                ApiKey = "configured-key"
            },
            name => name == "DEEPSEEK_API_KEY" ? "environment-key" : null);

        var secrets = provider.GetSecretValues();

        secrets.Should().BeEquivalentTo("admin-token", "configured-key", "environment-key");
    }

    private static RagasEvaluationSecretProvider CreateProvider(
        RagasEvaluationOptions options,
        Func<string, string?> getEnvironmentVariable) =>
        new(Options.Create(options), getEnvironmentVariable);
}
