using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class OpenAiCompatibleRagasEvaluator(
    HttpClient httpClient,
    IOptions<RagasEvaluationOptions> options,
    RagasJudgeResponseParser parser) : IRagasEvaluator
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    public async Task<RagasEvaluatorResult> EvaluateAsync(
        RagasEvaluationCaseInput input,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(input);
        var rawJudgeContent = await RequestJudgeAsync(prompt, cancellationToken);

        return new RagasEvaluatorResult(rawJudgeContent, parser.Parse(rawJudgeContent), prompt);
    }

    private async Task<string> RequestJudgeAsync(string prompt, CancellationToken cancellationToken)
    {
        var evaluationOptions = options.Value;
        var baseUrl = string.IsNullOrWhiteSpace(evaluationOptions.BaseUrl)
            ? DefaultBaseUrl
            : evaluationOptions.BaseUrl.TrimEnd('/');
        var endpoint = $"{baseUrl}/chat/completions";

        var payload = new
        {
            model = evaluationOptions.EvaluatorModel,
            temperature = 0,
            messages = new[]
            {
                new { role = "system", content = "You are a RAG evaluation judge. Return strict JSON only." },
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", evaluationOptions.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractJudgeContent(responseContent);
    }

    private static string ExtractJudgeContent(string responseContent)
    {
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string BuildPrompt(RagasEvaluationCaseInput input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Evaluate this RAG answer. Return strict JSON only.");
        builder.AppendLine();
        builder.AppendLine("Required output JSON schema:");
        builder.AppendLine("""
                           {
                             "faithfulness": { "score": 0.0, "reason": "..." },
                             "answer_relevance": { "score": 0.0, "reason": "..." },
                             "context_recall": { "score": 0.0, "reason": "..." },
                             "context_precision": { "score": 0.0, "reason": "..." }
                           }
                           """);
        builder.AppendLine("Scores must be numbers between 0 and 1.");
        builder.AppendLine();
        builder.AppendLine("Question:");
        builder.AppendLine(input.Question);
        builder.AppendLine();
        builder.AppendLine("Answer:");
        builder.AppendLine(input.Answer);
        builder.AppendLine();
        builder.AppendLine("Ground truth:");
        builder.AppendLine(input.GroundTruth);
        builder.AppendLine();
        builder.AppendLine("Retrieved contexts:");

        if (input.Contexts.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            for (var i = 0; i < input.Contexts.Count; i++)
            {
                var context = input.Contexts[i];
                builder.AppendLine($"Context {i + 1}:");
                builder.AppendLine($"ChunkId: {context.ChunkId}");
                builder.AppendLine($"FilePath: {context.FilePath}");
                builder.AppendLine($"ReferenceId: {context.ReferenceId}");
                builder.AppendLine("Content:");
                builder.AppendLine(context.Content);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
