using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Services.Evaluation;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class OpenAiCompatibleRagasEvaluatorTests
{
    private const string ApiKey = "sk-test-secret";

    [Fact]
    public async Task EvaluateAsync_WhenJudgeReturnsValidContent_SendsStrictJsonPromptAndReturnsParsedMetrics()
    {
        var handler = new CapturingHttpMessageHandler(CreateJudgeResponse());
        using var httpClient = new HttpClient(handler);
        var evaluator = CreateEvaluator(httpClient, new RagasEvaluationOptions
        {
            ApiKey = ApiKey,
            BaseUrl = "https://judge.example/v1/",
            EvaluatorModel = "judge-model"
        });
        var input = CreateInput();

        var result = await evaluator.EvaluateAsync(input, CancellationToken.None);

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri.Should().Be("https://judge.example/v1/chat/completions");
        handler.Request.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", ApiKey));
        handler.Request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        handler.RequestContent.Should().NotBeNullOrWhiteSpace();

        using var payloadDocument = JsonDocument.Parse(handler.RequestContent!);
        var payload = payloadDocument.RootElement;
        payload.GetProperty("model").GetString().Should().Be("judge-model");
        payload.GetProperty("temperature").GetInt32().Should().Be(0);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        messages.Should().HaveCount(2);
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be(
            "You are a RAG evaluation judge. Return strict JSON only.");
        messages[1].GetProperty("role").GetString().Should().Be("user");

        var prompt = messages[1].GetProperty("content").GetString();
        prompt.Should().NotBeNull();
        prompt.Should().Contain("Question");
        prompt.Should().Contain(input.Question);
        prompt.Should().Contain("Answer");
        prompt.Should().Contain(input.Answer);
        prompt.Should().Contain("Ground truth");
        prompt.Should().Contain(input.GroundTruth);
        prompt.Should().Contain("Retrieved contexts");
        prompt.Should().Contain(input.Contexts[0].Content);
        prompt.Should().Contain(input.Contexts[0].ChunkId);
        prompt.Should().Contain(input.Contexts[0].FilePath);
        prompt.Should().Contain(input.Contexts[0].ReferenceId);
        prompt.Should().Contain("strict JSON");
        prompt.Should().Contain("faithfulness");
        prompt.Should().Contain("answer_relevance");
        prompt.Should().Contain("context_recall");
        prompt.Should().Contain("context_precision");

        result.RawResponse.Should().Contain("\"faithfulness\"");
        result.RawResponse.Should().NotContain(ApiKey);
        result.Prompt.Should().Be(prompt);
        result.Prompt.Should().NotContain(ApiKey);
        result.ParseResult.Success.Should().BeTrue();
        result.ParseResult.Metrics.Should().NotBeNull();
        result.ParseResult.Metrics!.Faithfulness.Score.Should().Be(0.8);
        result.ParseResult.Metrics.AnswerRelevance.Score.Should().Be(0.9);
        result.ParseResult.Metrics.ContextRecall.Score.Should().Be(0.7);
        result.ParseResult.Metrics.ContextPrecision.Score.Should().Be(0.6);
    }

    [Fact]
    public async Task EvaluateAsync_WhenBaseUrlIsBlank_UsesDefaultOpenAiEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(CreateJudgeResponse());
        using var httpClient = new HttpClient(handler);
        var evaluator = CreateEvaluator(httpClient, new RagasEvaluationOptions
        {
            ApiKey = ApiKey,
            BaseUrl = " ",
            EvaluatorModel = "judge-model"
        });

        await evaluator.EvaluateAsync(CreateInput(), CancellationToken.None);

        handler.Request.Should().NotBeNull();
        handler.Request!.RequestUri.Should().Be("https://api.openai.com/v1/chat/completions");
    }

    private static OpenAiCompatibleRagasEvaluator CreateEvaluator(
        HttpClient httpClient,
        RagasEvaluationOptions options)
    {
        return new OpenAiCompatibleRagasEvaluator(
            httpClient,
            Options.Create(options),
            new RagasJudgeResponseParser());
    }

    private static RagasEvaluationCaseInput CreateInput()
    {
        return new RagasEvaluationCaseInput(
            "case-1",
            "What does LightRAGNet evaluate?",
            "It evaluates RAG answers against retrieved contexts.",
            [
                new RagasRetrievedContext(
                    "LightRAGNet evaluates generated answers with retrieved evidence.",
                    "chunk-1",
                    "docs/eval.md",
                    "ref-1")
            ],
            "It evaluates RAG answer quality.");
    }

    private static string CreateJudgeResponse()
    {
        return JsonSerializer.Serialize(
            new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = """
                                      {"faithfulness":{"score":0.8,"reason":"supported"},"answer_relevance":{"score":0.9,"reason":"direct"},"context_recall":{"score":0.7,"reason":"facts"},"context_precision":{"score":0.6,"reason":"focused"}}
                                      """
                        }
                    }
                }
            },
            LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);
    }

    private sealed class CapturingHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            };
        }
    }
}
