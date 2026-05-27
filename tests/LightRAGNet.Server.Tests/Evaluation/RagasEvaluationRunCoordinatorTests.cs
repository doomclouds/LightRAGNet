using System.Diagnostics;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationRunCoordinatorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateAsync_WhenEnabledAndConfigured_ReturnsQueuedRunAndStoresIt()
    {
        var store = CreateStore();
        var evaluator = new BlockingRagasEvaluator();
        var coordinator = CreateCoordinator(store: store, evaluator: evaluator);

        var result = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.RunId.Should().StartWith("ragas-");
        result.Value.RunId.Should().HaveLength(29);
        result.Value.Status.Should().Be(RagasEvaluationRunStatus.Queued.ToString());
        result.Value.Message.Should().Be("RAGAS evaluation run queued.");

        var stored = await store.GetAsync(result.Value.RunId, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Request.MaxCases.Should().Be(1);
        stored.Request.CaseNames.Should().BeEmpty();
        stored.Request.IncludeFullText.Should().BeFalse();
        stored.Request.PreviewMaxChars.Should().Be(64);
        stored.Request.Query.Mode.Should().Be(QueryMode.Local);

        await evaluator.WaitUntilCalledAsync();
        await coordinator.CancelAsync(result.Value.RunId, CancellationToken.None);
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task CreateAsync_WhenActiveRunExists_ReturnsConflict()
    {
        var evaluator = new BlockingRagasEvaluator();
        var coordinator = CreateCoordinator(evaluator: evaluator);
        var first = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);
        await evaluator.WaitUntilCalledAsync();

        var second = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        second.Success.Should().BeFalse();
        second.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        second.ErrorCode.Should().Be("active_run_exists");

        await coordinator.CancelAsync(first.Value!.RunId, CancellationToken.None);
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task CreateAsync_WhenCalledConcurrently_AllowsOnlyOneRun()
    {
        var evaluator = new BlockingRagasEvaluator();
        var coordinator = CreateCoordinator(evaluator: evaluator);

        var results = await Task.WhenAll(
            coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None),
            coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None));

        results.Should().ContainSingle(result => result.Success);
        results.Should().ContainSingle(result => result.StatusCode == StatusCodes.Status409Conflict);

        var created = results.Single(result => result.Success).Value!;
        await coordinator.CancelAsync(created.RunId, CancellationToken.None);
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task CancelAsync_WhenRunIsActive_CancelsTokenSourceAndReturnsCurrentRecord()
    {
        var evaluator = new BlockingRagasEvaluator();
        var coordinator = CreateCoordinator(evaluator: evaluator);
        var created = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);
        await evaluator.WaitUntilCalledAsync();

        var result = await coordinator.CancelAsync(created.Value!.RunId, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.RunId.Should().Be(created.Value.RunId);
        result.Value.Status.Should().BeOneOf(
            RagasEvaluationRunStatus.Queued.ToString(),
            RagasEvaluationRunStatus.Running.ToString());
        await evaluator.WaitUntilCancelledAsync();
    }

    [Fact]
    public async Task GetAsync_WhenRunIsMissing_ReturnsNotFound()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.GetAsync("missing-run", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        result.ErrorCode.Should().Be("run_not_found");
    }

    [Fact]
    public async Task CreateAsync_WhenFullTextPersistenceIsDisabled_PropagatesSnapshotterFailure()
    {
        var options = CreateOptions();
        options.AllowPersistFullText = false;
        var coordinator = CreateCoordinator(options: options);

        var result = await coordinator.CreateAsync(
            CreateRequest(maxCases: 1, includeFullText: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.ErrorCode.Should().Be("full_text_disabled");
    }

    [Fact]
    public async Task CreateAsync_WhenLoaderFails_PropagatesLoaderFailure()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.CreateAsync(
            CreateRequest(maxCases: 1, caseNames: ["missing-case"]),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.ErrorCode.Should().Be("unknown_case");
    }

    [Fact]
    public async Task CreateAsync_WhenEvaluationIsDisabled_ReturnsForbidden()
    {
        var coordinator = CreateCoordinator(options: CreateOptions(enabled: false));

        var result = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.ErrorCode.Should().Be("ragas_evaluation_disabled");
    }

    [Theory]
    [InlineData("", "test-api-key", "missing_admin_token")]
    [InlineData("test-admin-token", "", "missing_evaluator_api_key")]
    public async Task CreateAsync_WhenRequiredSecretIsMissing_ReturnsServiceUnavailable(
        string adminToken,
        string apiKey,
        string expectedCode)
    {
        var options = CreateOptions();
        options.AdminToken = adminToken;
        options.ApiKey = apiKey;
        var coordinator = CreateCoordinator(options: options);

        var result = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        result.ErrorCode.Should().Be(expectedCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private RagasEvaluationRunCoordinator CreateCoordinator(
        RagasEvaluationOptions? options = null,
        RagasEvaluationRunStore? store = null,
        IRagasEvaluator? evaluator = null)
    {
        options ??= CreateOptions();
        store ??= CreateStore();
        evaluator ??= new SuccessfulRagasEvaluator();

        var optionsMonitor = Options.Create(options);
        var queryClient = new SuccessfulRagasRagQueryClient();
        var snapshotter = new RagasEvaluationTextSnapshotter(optionsMonitor);
        var runner = new RagasEvaluationRunner(
            store,
            queryClient,
            evaluator,
            snapshotter,
            optionsMonitor,
            NullLogger<RagasEvaluationRunner>.Instance);

        return new RagasEvaluationRunCoordinator(
            optionsMonitor,
            new RagasEvaluationDataLoader(optionsMonitor),
            store,
            runner,
            snapshotter,
            NullLogger<RagasEvaluationRunCoordinator>.Instance);
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

    private static RagasEvaluationOptions CreateOptions(bool enabled = true) =>
        new()
        {
            Enabled = enabled,
            AdminToken = "test-admin-token",
            ApiKey = "test-api-key",
            BaseUrl = "https://evaluator.example",
            AllowPersistFullText = true,
            MaxCasesPerRun = 5,
            PreviewMaxChars = 64,
            PersistJudgePrompts = false,
            PersistJudgeResponses = false
        };

    private static CreateRagasEvaluationRunRequest CreateRequest(
        int? maxCases,
        bool includeFullText = false,
        List<string>? caseNames = null) =>
        new()
        {
            CaseNames = caseNames ?? [],
            MaxCases = maxCases,
            IncludeFullText = includeFullText,
            Query = new RagasEvaluationQueryOptions
            {
                Mode = QueryMode.Local,
                TopK = 3,
                ChunkTopK = 2,
                EnableRerank = false
            }
        };

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

            throw new UnreachableException();
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
