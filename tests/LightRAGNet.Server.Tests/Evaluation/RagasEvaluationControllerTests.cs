using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationControllerTests
{
    private const string Endpoint = "/api/evaluation/ragas/runs";
    private const string AdminToken = "test-admin-token";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CreateAsync_WhenTokenHeaderIsMissing_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAsync_WhenTokenHeaderIsWrong_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Evaluation-Token", "wrong-token");

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAsync_WhenTokenIsValid_ReachesCoordinatorAndReturnsQueuedRun()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateRagasEvaluationRunResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.RunId.Should().StartWith("ragas-");
        body.Status.Should().Be("Queued");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(AdminToken);
        raw.Should().NotContain("test-api-key");
    }

    [Fact]
    public async Task CreateGetAndCancelAsync_WhenRunExists_ReturnExpectedCodes()
    {
        var evaluator = new BlockingRagasEvaluator();
        using var factory = CreateFactory(evaluator: evaluator);
        using var client = CreateAuthorizedClient(factory);

        var createResponse = await client.PostAsJsonAsync(Endpoint, CreateRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRagasEvaluationRunResponse>(JsonOptions);
        created.Should().NotBeNull();
        await evaluator.WaitUntilCalledAsync();

        var getResponse = await client.GetAsync($"{Endpoint}/{created!.RunId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var run = await getResponse.Content.ReadFromJsonAsync<RagasEvaluationRunResponse>(JsonOptions);
        run.Should().NotBeNull();
        run!.RunId.Should().Be(created.RunId);

        var cancelResponse = await client.PostAsync($"{Endpoint}/{created.RunId}/cancel", content: null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task CreateAsync_WhenActiveRunExists_ReturnsConflict()
    {
        var evaluator = new BlockingRagasEvaluator();
        using var factory = CreateFactory(evaluator: evaluator);
        using var client = CreateAuthorizedClient(factory);

        var firstResponse = await client.PostAsJsonAsync(Endpoint, CreateRequest());
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<CreateRagasEvaluationRunResponse>(JsonOptions);
        await evaluator.WaitUntilCalledAsync();

        var secondResponse = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await secondResponse.Content.ReadAsStringAsync();
        error.Should().Contain("active_run_exists");
        await client.PostAsync($"{Endpoint}/{first!.RunId}/cancel", content: null);
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task CreateAsync_WhenEvaluationIsDisabled_ReturnsForbidden()
    {
        using var factory = CreateFactory(configurationOverrides: new Dictionary<string, string?>
        {
            ["Evaluation:Ragas:Enabled"] = "false"
        });
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAsync_WhenEvaluatorApiKeyIsMissing_ReturnsServiceUnavailable()
    {
        using var factory = CreateFactory(configurationOverrides: new Dictionary<string, string?>
        {
            ["Evaluation:Ragas:ApiKey"] = ""
        });
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("missing_evaluator_api_key");
        body.Should().NotContain(AdminToken);
    }

    [Fact]
    public async Task CreateAsync_WhenAdminTokenIsNotConfigured_ReturnsServiceUnavailable()
    {
        using var factory = CreateFactory(configurationOverrides: new Dictionary<string, string?>
        {
            ["Evaluation:Ragas:AdminToken"] = ""
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Evaluation-Token", AdminToken);

        var response = await client.PostAsJsonAsync(Endpoint, CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("missing_admin_token");
        body.Should().NotContain(AdminToken);
    }

    private static HttpClient CreateAuthorizedClient(LightRagServerFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Evaluation-Token", AdminToken);
        return client;
    }

    private static LightRagServerFactory CreateFactory(
        IRagasEvaluator? evaluator = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Evaluation:Ragas:Enabled"] = "true",
            ["Evaluation:Ragas:AdminToken"] = AdminToken,
            ["Evaluation:Ragas:ApiKey"] = "test-api-key",
            ["Evaluation:Ragas:BaseUrl"] = "https://evaluator.example",
            ["Evaluation:Ragas:MaxCasesPerRun"] = "5",
            ["Evaluation:Ragas:PreviewMaxChars"] = "64"
        };

        if (configurationOverrides is not null)
        {
            foreach (var (key, value) in configurationOverrides)
            {
                overrides[key] = value;
            }
        }

        return new LightRagServerFactory(
            services =>
            {
                services.RemoveAll<IRagasRagQueryClient>();
                services.RemoveAll<IRagasEvaluator>();
                services.RemoveAll<RagasEvaluationSecretProvider>();
                services.AddScoped<IRagasRagQueryClient, SuccessfulRagasRagQueryClient>();
                services.AddScoped<IRagasEvaluator>(_ => evaluator ?? new SuccessfulRagasEvaluator());
                services.AddSingleton(sp => new RagasEvaluationSecretProvider(
                    sp.GetRequiredService<IOptions<RagasEvaluationOptions>>(),
                    _ => null));
            },
            overrides);
    }

    private static CreateRagasEvaluationRunRequest CreateRequest() =>
        new()
        {
            MaxCases = 1,
            Query = new RagasEvaluationQueryOptions
            {
                Mode = QueryMode.Local,
                TopK = 3,
                ChunkTopK = 2,
                EnableRerank = false
            }
        };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class SuccessfulRagasRagQueryClient : IRagasRagQueryClient
    {
        public Task<RagasQueryExecutionResult> QueryAsync(
            RagasDatasetCase dataSetCase,
            RagasEvaluationQueryOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RagasQueryExecutionResult(
                "answer",
                [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
                options.Mode));
    }

    private sealed class SuccessfulRagasEvaluator : IRagasEvaluator
    {
        public Task<RagasEvaluatorResult> EvaluateAsync(
            RagasEvaluationCaseInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RagasEvaluatorResult(
                "{}",
                RagasJudgeParseResult.Succeeded(new RagasMetricSet(
                    new RagasMetricScore(1, "faithfulness"),
                    new RagasMetricScore(1, "answer relevance"),
                    new RagasMetricScore(1, "context recall"),
                    new RagasMetricScore(1, "context precision"))),
                "prompt"));
    }

    private sealed class BlockingRagasEvaluator : IRagasEvaluator
    {
        private readonly TaskCompletionSource called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RagasEvaluatorResult> EvaluateAsync(
            RagasEvaluationCaseInput input,
            CancellationToken cancellationToken)
        {
            called.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("Blocking evaluator should only finish by cancellation.");
        }

        public async Task WaitUntilCalledAsync()
        {
            var completed = await Task.WhenAny(called.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().Be(called.Task);
        }

        public async Task WaitUntilCancelledAsync()
        {
            var completed = await Task.WhenAny(cancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().Be(cancelled.Task);
        }
    }
}
