using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationRunnerTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_WhenTwoCasesSucceed_AggregatesAverageMetricsAndSnapshotsText()
    {
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer one",
            [new RagasRetrievedContext("context one", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer two",
            [new RagasRetrievedContext("context two", "chunk-2", "docs/two.md", "ref-2")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.EnqueueSuccess(CreateMetrics(0.8, 0.6, 0.4, 0.2));
        evaluator.EnqueueSuccess(CreateMetrics(1.0, 0.8, 0.6, 0.4));
        var store = CreateStore();
        var run = CreateRun(includeFullText: true);
        var runner = CreateRunner(store, queryClient, evaluator);

        await runner.ExecuteAsync(run, CreateCases(2), CancellationToken.None);

        run.Status.Should().Be(RagasEvaluationRunStatus.Completed);
        run.StartedAt.Should().NotBeNull();
        run.CompletedAt.Should().NotBeNull();
        run.Summary.Total.Should().Be(2);
        run.Summary.Succeeded.Should().Be(2);
        run.Summary.Failed.Should().Be(0);
        run.Summary.Cancelled.Should().Be(0);
        run.Summary.AverageMetrics.Faithfulness.Should().BeApproximately(0.9, 0.000001);
        run.Summary.AverageMetrics.AnswerRelevance.Should().BeApproximately(0.7, 0.000001);
        run.Summary.AverageMetrics.ContextRecall.Should().BeApproximately(0.5, 0.000001);
        run.Summary.AverageMetrics.ContextPrecision.Should().BeApproximately(0.3, 0.000001);
        run.Summary.AverageMetrics.RagasScore.Should().BeApproximately(0.6, 0.000001);
        run.Cases.Should().HaveCount(2);
        run.Cases.Should().OnlyContain(result => result.Status == RagasEvaluationCaseStatus.Succeeded.ToString());
        run.Cases[0].Metrics.RagasScore.Should().BeApproximately(0.5, 0.000001);
        run.Cases[0].Reasons.Should().ContainEquivalentOf(new RagasEvaluationMetricReasonDto
        {
            Metric = "faithfulness",
            Reason = "faithfulness reason"
        });
        run.Cases[0].AnswerPreview.Should().Be("answer one");
        run.Cases[0].AnswerText.Should().Be("answer one");
        run.Cases[0].Contexts.Should().ContainSingle();
        run.Cases[0].Contexts[0].Preview.Should().Be("context one");
        run.Cases[0].Contexts[0].Text.Should().Be("context one");
        run.Cases[0].Contexts[0].ChunkId.Should().Be("chunk-1");
        queryClient.Options.Should().OnlyContain(options => options.Mode == QueryMode.Local && options.TopK == 3);
        evaluator.Inputs.Should().HaveCount(2);

        var saved = await store.GetAsync(run.RunId, CancellationToken.None);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(RagasEvaluationRunStatus.Completed);
        saved.Cases.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoContextsAreRetrieved_MarksCaseFailedAndSkipsEvaluator()
    {
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult("answer", [], QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        var run = CreateRun();
        var runner = CreateRunner(CreateStore(), queryClient, evaluator);

        await runner.ExecuteAsync(run, CreateCases(1), CancellationToken.None);

        run.Status.Should().Be(RagasEvaluationRunStatus.Completed);
        run.Summary.Total.Should().Be(1);
        run.Summary.Succeeded.Should().Be(0);
        run.Summary.Failed.Should().Be(1);
        run.Summary.AverageMetrics.Faithfulness.Should().BeNull();
        run.Cases.Should().ContainSingle();
        run.Cases[0].Status.Should().Be(RagasEvaluationCaseStatus.Failed.ToString());
        run.Cases[0].Metrics.RagasScore.Should().BeNull();
        run.Cases[0].Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "no_contexts" &&
            diagnostic.Message == "RAG query returned no retrieved contexts.");
        evaluator.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenEvaluatorParserFails_MarksCaseFailedWithParserDiagnostic()
    {
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer",
            [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.EnqueueFailure("invalid_json", "Judge response was not valid JSON.");
        var run = CreateRun();
        var runner = CreateRunner(CreateStore(), queryClient, evaluator);

        await runner.ExecuteAsync(run, CreateCases(1), CancellationToken.None);

        run.Status.Should().Be(RagasEvaluationRunStatus.Completed);
        run.Summary.Total.Should().Be(1);
        run.Summary.Succeeded.Should().Be(0);
        run.Summary.Failed.Should().Be(1);
        run.Cases.Should().ContainSingle();
        run.Cases[0].Status.Should().Be(RagasEvaluationCaseStatus.Failed.ToString());
        run.Cases[0].Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "invalid_json" &&
            diagnostic.Message == "Judge response was not valid JSON.");
        evaluator.Inputs.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEvaluatorParserFails_PersistsSanitizedJudgePromptAndResponseSnapshots()
    {
        const string apiKey = "sk-test-secret";
        const string adminToken = "admin-test-secret";
        var prompt = $"judge prompt with {apiKey} and useful context";
        var rawResponse = $"raw judge response with {adminToken} and malformed json";
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer",
            [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.EnqueueFailure("invalid_json", "Judge response was not valid JSON.", rawResponse, prompt);
        var run = CreateRun(includeFullText: true);
        var runner = CreateRunner(
            CreateStore(),
            queryClient,
            evaluator,
            new RagasEvaluationOptions
            {
                AllowPersistFullText = true,
                PreviewMaxChars = 128,
                PersistJudgePrompts = true,
                PersistJudgeResponses = true,
                ApiKey = apiKey,
                AdminToken = adminToken
            });

        await runner.ExecuteAsync(run, CreateCases(1), CancellationToken.None);

        var promptDiagnostic = run.Cases[0].Diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Code == "judge_prompt")
            .Subject;
        var responseDiagnostic = run.Cases[0].Diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Code == "judge_response")
            .Subject;

        promptDiagnostic.Details["preview"].Should().Contain("judge prompt");
        promptDiagnostic.Details["hash"].Should().NotBeNullOrWhiteSpace();
        promptDiagnostic.Details["text"].Should().Contain("useful context");
        responseDiagnostic.Details["preview"].Should().Contain("raw judge response");
        responseDiagnostic.Details["hash"].Should().NotBeNullOrWhiteSpace();
        responseDiagnostic.Details["text"].Should().Contain("malformed json");

        var serializedDiagnostics = string.Join(
            Environment.NewLine,
            run.Cases[0].Diagnostics.SelectMany(diagnostic =>
                diagnostic.Details.Select(detail => $"{detail.Key}:{detail.Value}")));
        serializedDiagnostics.Should().NotContain(apiKey);
        serializedDiagnostics.Should().NotContain(adminToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJudgePersistenceIsDisabled_DoesNotPersistJudgeDiagnostics()
    {
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer",
            [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.EnqueueFailure(
            "invalid_json",
            "Judge response was not valid JSON.",
            "raw response",
            "judge prompt");
        var run = CreateRun(includeFullText: true);
        var runner = CreateRunner(
            CreateStore(),
            queryClient,
            evaluator,
            new RagasEvaluationOptions
            {
                AllowPersistFullText = true,
                PersistJudgePrompts = false,
                PersistJudgeResponses = false
            });

        await runner.ExecuteAsync(run, CreateCases(1), CancellationToken.None);

        run.Cases[0].Diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Code == "judge_prompt" || diagnostic.Code == "judge_response");
    }

    [Fact]
    public async Task ExecuteAsync_WhenJudgeMetricReasonsContainSecrets_RedactsReasonsBeforePersisting()
    {
        const string apiKey = "sk-test-secret";
        const string adminToken = "admin-test-secret";
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer",
            [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.EnqueueSuccess(new RagasMetricSet(
            new RagasMetricScore(1, $"uses {apiKey}"),
            new RagasMetricScore(1, $"uses {adminToken}"),
            new RagasMetricScore(1, "safe recall reason"),
            new RagasMetricScore(1, "safe precision reason")));
        var run = CreateRun();
        var store = CreateStore();
        var runner = CreateRunner(
            store,
            queryClient,
            evaluator,
            new RagasEvaluationOptions
            {
                ApiKey = apiKey,
                AdminToken = adminToken,
                PreviewMaxChars = 128
            });

        await runner.ExecuteAsync(run, CreateCases(1), CancellationToken.None);

        var serializedRun = System.Text.Json.JsonSerializer.Serialize(run);
        serializedRun.Should().NotContain(apiKey);
        serializedRun.Should().NotContain(adminToken);
        serializedRun.Should().Contain("[redacted]");
        run.Cases[0].Reasons.Should().Contain(reason =>
            reason.Metric == "faithfulness" &&
            reason.Reason == "uses [redacted]");
        run.Cases[0].Reasons.Should().Contain(reason =>
            reason.Metric == "answer_relevance" &&
            reason.Reason == "uses [redacted]");

        var saved = await store.GetAsync(run.RunId, CancellationToken.None);
        var serializedSaved = System.Text.Json.JsonSerializer.Serialize(saved);
        serializedSaved.Should().NotContain(apiKey);
        serializedSaved.Should().NotContain(adminToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationIsRequested_MarksRunCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var queryClient = new FakeRagasRagQueryClient();
        queryClient.Enqueue(new RagasQueryExecutionResult(
            "answer",
            [new RagasRetrievedContext("context", "chunk-1", "docs/one.md", "ref-1")],
            QueryMode.Mix));
        var evaluator = new FakeRagasEvaluator();
        evaluator.OnEvaluate = token =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        };
        var store = CreateStore();
        var run = CreateRun();
        var runner = CreateRunner(store, queryClient, evaluator);

        await runner.ExecuteAsync(run, CreateCases(2), cancellation.Token);

        run.Status.Should().Be(RagasEvaluationRunStatus.Cancelled);
        run.CompletedAt.Should().NotBeNull();
        run.Summary.Total.Should().Be(2);
        run.Summary.Succeeded.Should().Be(0);
        run.Summary.Failed.Should().Be(0);
        run.Summary.Cancelled.Should().Be(2);
        run.Cases.Should().BeEmpty();
        evaluator.Inputs.Should().HaveCount(1);
        var saved = await store.GetAsync(run.RunId, CancellationToken.None);
        saved!.Status.Should().Be(RagasEvaluationRunStatus.Cancelled);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private RagasEvaluationRunner CreateRunner(
        RagasEvaluationRunStore store,
        IRagasRagQueryClient queryClient,
        IRagasEvaluator evaluator,
        RagasEvaluationOptions? options = null)
    {
        options ??= new RagasEvaluationOptions
        {
            AllowPersistFullText = true,
            PreviewMaxChars = 64
        };

        return new RagasEvaluationRunner(
            store,
            queryClient,
            evaluator,
            new RagasEvaluationTextSnapshotter(Options.Create(options)),
            Options.Create(options),
            new RagasEvaluationSecretProvider(Options.Create(options)),
            NullLogger<RagasEvaluationRunner>.Instance);
    }

    private RagasEvaluationRunStore CreateStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LightRAG:WorkingDir"] = tempDirectory
            })
            .Build();

        return new RagasEvaluationRunStore(configuration);
    }

    private static RagasEvaluationRunRecord CreateRun(bool includeFullText = false)
    {
        return new RagasEvaluationRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            Status = RagasEvaluationRunStatus.Queued,
            CreatedAt = new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            Request = new RagasEvaluationRequestSnapshot
            {
                IncludeFullText = includeFullText,
                Query = new RagasEvaluationQueryOptions
                {
                    Mode = QueryMode.Local,
                    TopK = 3
                }
            }
        };
    }

    private static IReadOnlyList<RagasDatasetCase> CreateCases(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new RagasDatasetCase(
                $"case-{index}",
                $"question {index}",
                $"ground truth {index}",
                "project"))
            .ToArray();
    }

    private static RagasMetricSet CreateMetrics(
        double faithfulness,
        double answerRelevance,
        double contextRecall,
        double contextPrecision)
    {
        return new RagasMetricSet(
            new RagasMetricScore(faithfulness, "faithfulness reason"),
            new RagasMetricScore(answerRelevance, "answer relevance reason"),
            new RagasMetricScore(contextRecall, "context recall reason"),
            new RagasMetricScore(contextPrecision, "context precision reason"));
    }

    private sealed class FakeRagasRagQueryClient : IRagasRagQueryClient
    {
        private readonly Queue<RagasQueryExecutionResult> results = new();

        public List<RagasEvaluationQueryOptions> Options { get; } = [];

        public void Enqueue(RagasQueryExecutionResult result)
        {
            results.Enqueue(result);
        }

        public Task<RagasQueryExecutionResult> QueryAsync(
            RagasDatasetCase dataSetCase,
            RagasEvaluationQueryOptions options,
            CancellationToken cancellationToken)
        {
            Options.Add(options);

            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class FakeRagasEvaluator : IRagasEvaluator
    {
        private readonly Queue<RagasEvaluatorResult> results = new();

        public List<RagasEvaluationCaseInput> Inputs { get; } = [];

        public Action<CancellationToken>? OnEvaluate { get; set; }

        public void EnqueueSuccess(RagasMetricSet metrics)
        {
            results.Enqueue(new RagasEvaluatorResult(
                "{}",
                RagasJudgeParseResult.Succeeded(metrics),
                "prompt"));
        }

        public void EnqueueFailure(
            string code,
            string message,
            string rawResponse = "{}",
            string prompt = "prompt")
        {
            results.Enqueue(new RagasEvaluatorResult(
                rawResponse,
                RagasJudgeParseResult.Failed(code, message),
                prompt));
        }

        public Task<RagasEvaluatorResult> EvaluateAsync(
            RagasEvaluationCaseInput input,
            CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            OnEvaluate?.Invoke(cancellationToken);

            return Task.FromResult(results.Dequeue());
        }
    }
}
