using System.Diagnostics;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationRunCoordinatorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> disposables = [];

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
    public async Task CreateAsync_WhenBackgroundRuns_ResolvesRunnerFromServiceScopeAndDisposesScope()
    {
        var probe = new ScopedProbe();
        var evaluator = new BlockingRagasEvaluator();
        var coordinator = CreateCoordinator(evaluator: evaluator, scopedProbe: probe);
        var created = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);
        await evaluator.WaitUntilCalledAsync();

        await coordinator.CancelAsync(created.Value!.RunId, CancellationToken.None);
        await evaluator.WaitUntilCancelledAsync();
        await probe.WaitUntilDisposedAsync();

        probe.WasResolved.Should().BeTrue();
        probe.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WhenBackgroundRunnerCannotBeResolved_MarksRunFailedAndAllowsNextCreate()
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store, scopeFactory: new ThrowingServiceScopeFactory());
        var first = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        await WaitUntilAsync(async () =>
        {
            var run = await store.GetAsync(first.Value!.RunId, CancellationToken.None);
            return run?.Status == RagasEvaluationRunStatus.Failed;
        });

        var evaluator = new BlockingRagasEvaluator();
        var secondCoordinator = CreateCoordinator(store: store, evaluator: evaluator);
        var second = await secondCoordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        second.Success.Should().BeTrue();
        second.Value!.RunId.Should().NotBe(first.Value!.RunId);
        await evaluator.WaitUntilCalledAsync();
        await secondCoordinator.CancelAsync(second.Value.RunId, CancellationToken.None);
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
    public async Task ListAsync_ReturnsLightweightRuns()
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

        var result = await coordinator.ListAsync(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Runs.Should().ContainSingle();
        var item = result.Value.Runs[0];
        item.RunId.Should().Be("ragas-a");
        item.Status.Should().Be(RagasEvaluationRunStatus.Completed.ToString());
        item.Total.Should().Be(1);
        item.Succeeded.Should().Be(1);
        item.Failed.Should().Be(0);
        item.Cancelled.Should().Be(0);
        item.RagasScore.Should().Be(0.75);
        item.DurationSeconds.Should().Be(5);
    }

    [Fact]
    public async Task ExportAsync_UnknownRun_ReturnsNotFound()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.ExportAsync("missing", "json", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("run_not_found");
        result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Theory]
    [InlineData(null, "application/json; charset=utf-8", ".json")]
    [InlineData("", "application/json; charset=utf-8", ".json")]
    [InlineData("json", "application/json; charset=utf-8", ".json")]
    [InlineData("csv", "text/csv; charset=utf-8", ".csv")]
    public async Task ExportAsync_WhenFormatIsSupported_ReturnsExport(
        string? format,
        string expectedContentType,
        string expectedExtension)
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

        var result = await coordinator.ExportAsync("ragas-a", format, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.ContentType.Should().Be(expectedContentType);
        result.Value.FileName.Should().Be("ragas-a" + expectedExtension);
        result.Value.Content.Should().Contain("ragas-a");
    }

    [Fact]
    public async Task ExportAsync_WhenFormatIsUnsupported_ReturnsBadRequest()
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

        var result = await coordinator.ExportAsync("ragas-a", "xml", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unsupported_export_format");
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CompareAsync_SameRun_ReturnsBadRequest()
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-a"), CancellationToken.None);

        var result = await coordinator.CompareAsync("ragas-a", "ragas-a", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("same_run_compare");
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("missing-current", "ragas-baseline")]
    [InlineData("ragas-current", "missing-baseline")]
    public async Task CompareAsync_WhenEitherRunIsMissing_ReturnsNotFound(
        string runId,
        string baselineRunId)
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-current"), CancellationToken.None);
        await store.UpsertAsync(CreateCompletedRun("ragas-baseline"), CancellationToken.None);

        var result = await coordinator.CompareAsync(runId, baselineRunId, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("run_not_found");
        result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task CompareAsync_WhenRunsExist_ReturnsComparison()
    {
        var store = CreateStore();
        var coordinator = CreateCoordinator(store: store);
        await store.UpsertAsync(CreateCompletedRun("ragas-current", ragasScore: 0.8), CancellationToken.None);
        await store.UpsertAsync(CreateCompletedRun("ragas-baseline", ragasScore: 0.7), CancellationToken.None);

        var result = await coordinator.CompareAsync("ragas-current", "ragas-baseline", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.RunId.Should().Be("ragas-current");
        result.Value.BaselineRunId.Should().Be("ragas-baseline");
        result.Value.Metrics["ragasScore"].Direction.Should().Be("Improved");
        result.Value.Metrics["ragasScore"].Delta.Should().BeApproximately(0.1, 0.0001);
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

    [Fact]
    public async Task CreateAsync_WhenEvaluatorApiKeyComesFromDeepSeekEnvironment_QueuesRun()
    {
        var options = CreateOptions();
        options.ApiKey = string.Empty;
        var store = CreateStore();
        var secretProvider = new RagasEvaluationSecretProvider(
            Options.Create(options),
            name => name == "DEEPSEEK_API_KEY" ? "environment-key" : null);
        var coordinator = CreateCoordinator(options: options, store: store, secretProvider: secretProvider);

        var result = await coordinator.CreateAsync(CreateRequest(maxCases: 1), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Status.Should().Be(RagasEvaluationRunStatus.Queued.ToString());
        await WaitUntilAsync(async () =>
        {
            var run = await store.GetAsync(result.Value.RunId, CancellationToken.None);
            return run?.Status == RagasEvaluationRunStatus.Completed;
        });
    }

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private RagasEvaluationRunCoordinator CreateCoordinator(
        RagasEvaluationOptions? options = null,
        RagasEvaluationRunStore? store = null,
        IRagasEvaluator? evaluator = null,
        ScopedProbe? scopedProbe = null,
        IServiceScopeFactory? scopeFactory = null,
        RagasEvaluationSecretProvider? secretProvider = null)
    {
        options ??= CreateOptions();
        store ??= CreateStore();
        evaluator ??= new SuccessfulRagasEvaluator();

        var optionsMonitor = Options.Create(options);
        var snapshotter = new RagasEvaluationTextSnapshotter(optionsMonitor);
        scopeFactory ??= CreateScopeFactory(store, evaluator, snapshotter, optionsMonitor, scopedProbe);

        return new RagasEvaluationRunCoordinator(
            optionsMonitor,
            new RagasEvaluationDataLoader(optionsMonitor),
            store,
            new RagasEvaluationExportService(),
            new RagasEvaluationComparisonService(),
            scopeFactory,
            snapshotter,
            secretProvider ?? new RagasEvaluationSecretProvider(optionsMonitor, _ => null),
            NullLogger<RagasEvaluationRunCoordinator>.Instance);
    }

    private IServiceScopeFactory CreateScopeFactory(
        RagasEvaluationRunStore store,
        IRagasEvaluator evaluator,
        RagasEvaluationTextSnapshotter snapshotter,
        IOptions<RagasEvaluationOptions> options,
        ScopedProbe? scopedProbe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(options);
        services.AddSingleton(snapshotter);
        services.AddSingleton<RagasEvaluationSecretProvider>();
        services.AddScoped<IRagasRagQueryClient, SuccessfulRagasRagQueryClient>();
        services.AddScoped(_ => scopedProbe ?? new ScopedProbe());
        services.AddScoped<IRagasEvaluator>(serviceProvider =>
        {
            serviceProvider.GetRequiredService<ScopedProbe>().MarkResolved();
            return evaluator;
        });
        services.AddScoped<RagasEvaluationRunner>();
        services.AddSingleton<ILogger<RagasEvaluationRunner>>(NullLogger<RagasEvaluationRunner>.Instance);

        var provider = services.BuildServiceProvider();
        disposables.Add(provider);

        return provider.GetRequiredService<IServiceScopeFactory>();
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

    private static RagasEvaluationRunRecord CreateCompletedRun(string runId, double ragasScore = 0.75) =>
        new()
        {
            RunId = runId,
            Status = RagasEvaluationRunStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 5, 28, 8, 0, 5, TimeSpan.Zero),
            Summary = new RagasEvaluationSummaryDto
            {
                Total = 1,
                Succeeded = 1,
                AverageMetrics = new RagasEvaluationMetricsDto
                {
                    Faithfulness = 0.9,
                    AnswerRelevance = 0.8,
                    ContextRecall = 0.7,
                    ContextPrecision = 0.6,
                    RagasScore = ragasScore
                },
                ElapsedTimeSeconds = 5
            },
            Cases =
            [
                new RagasEvaluationCaseResultDto
                {
                    CaseName = "case-a",
                    Status = RagasEvaluationCaseStatus.Succeeded.ToString()
                }
            ]
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

    private sealed class ScopedProbe : IDisposable
    {
        private readonly TaskCompletionSource disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasResolved { get; private set; }

        public bool WasDisposed { get; private set; }

        public void MarkResolved()
        {
            WasResolved = true;
        }

        public void Dispose()
        {
            WasDisposed = true;
            disposed.TrySetResult();
        }

        public async Task WaitUntilDisposedAsync()
        {
            var completed = await Task.WhenAny(disposed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().Be(disposed.Task);
        }
    }

    private sealed class ThrowingServiceScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            throw new InvalidOperationException("Could not resolve RAGAS runner scope.");
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }
}
