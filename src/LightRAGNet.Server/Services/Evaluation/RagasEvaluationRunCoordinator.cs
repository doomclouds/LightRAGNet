using System.Collections.Concurrent;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

public sealed class RagasEvaluationRunCoordinator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeRuns = new();
    private readonly SemaphoreSlim createGate = new(1, 1);
    private readonly IOptions<RagasEvaluationOptions> options;
    private readonly RagasEvaluationDataLoader dataLoader;
    private readonly RagasEvaluationRunStore store;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly RagasEvaluationTextSnapshotter snapshotter;
    private readonly RagasEvaluationSecretProvider secretProvider;
    private readonly ILogger<RagasEvaluationRunCoordinator> logger;

    internal RagasEvaluationRunCoordinator(
        IOptions<RagasEvaluationOptions> options,
        RagasEvaluationDataLoader dataLoader,
        RagasEvaluationRunStore store,
        IServiceScopeFactory scopeFactory,
        RagasEvaluationTextSnapshotter snapshotter,
        RagasEvaluationSecretProvider secretProvider,
        ILogger<RagasEvaluationRunCoordinator> logger)
    {
        this.options = options;
        this.dataLoader = dataLoader;
        this.store = store;
        this.scopeFactory = scopeFactory;
        this.snapshotter = snapshotter;
        this.secretProvider = secretProvider;
        this.logger = logger;
    }

    internal async Task<RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>> CreateAsync(
        CreateRagasEvaluationRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var availability = ValidateAvailability();
        if (!availability.Success)
        {
            return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                availability.ErrorCode!,
                availability.ErrorMessage!,
                availability.StatusCode);
        }

        var fullTextValidation = snapshotter.ValidateFullTextRequest(request.IncludeFullText);
        if (!fullTextValidation.Success)
        {
            return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                fullTextValidation.ErrorCode!,
                fullTextValidation.ErrorMessage!,
                fullTextValidation.StatusCode);
        }

        var casesResult = await dataLoader.LoadCasesAsync(
            request.CaseNames ?? [],
            request.MaxCases,
            cancellationToken);
        if (!casesResult.Success)
        {
            return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                casesResult.ErrorCode!,
                casesResult.ErrorMessage!,
                casesResult.StatusCode);
        }

        var cases = casesResult.Value ?? [];
        RagasEvaluationRunRecord run;
        await createGate.WaitAsync(cancellationToken);
        try
        {
            if (!activeRuns.IsEmpty || await store.GetActiveAsync(cancellationToken) is not null)
            {
                return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                    "active_run_exists",
                    "Another RAGAS evaluation run is already queued or running.",
                    StatusCodes.Status409Conflict);
            }

            run = CreateRunRecord(request, cases.Count);
            await store.UpsertAsync(run, cancellationToken);
        }
        finally
        {
            createGate.Release();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        try
        {
            if (!activeRuns.TryAdd(run.RunId, cts))
            {
                cts.Dispose();
                await MarkRunFailedAsync(
                    run,
                    new InvalidOperationException("Could not register the RAGAS evaluation run as active."));
                return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                    "active_run_exists",
                    "Another RAGAS evaluation run is already queued or running.",
                    StatusCodes.Status409Conflict);
            }

            StartBackgroundRun(run, cases, cts);
        }
        catch
        {
            activeRuns.TryRemove(run.RunId, out _);
            cts.Dispose();
            throw;
        }

        return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Ok(
            new CreateRagasEvaluationRunResponse
            {
                RunId = run.RunId,
                Status = run.Status.ToString(),
                CreatedAt = run.CreatedAt,
                Message = "RAGAS evaluation run queued."
            });
    }

    internal async Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> GetAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return RagasEvaluationOperationResult<RagasEvaluationRunResponse>.Fail(
                "run_not_found",
                $"RAGAS evaluation run '{runId}' was not found.",
                StatusCodes.Status404NotFound);
        }

        return RagasEvaluationOperationResult<RagasEvaluationRunResponse>.Ok(ToResponse(run));
    }

    internal async Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> CancelAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return RagasEvaluationOperationResult<RagasEvaluationRunResponse>.Fail(
                "run_not_found",
                $"RAGAS evaluation run '{runId}' was not found.",
                StatusCodes.Status404NotFound);
        }

        if (activeRuns.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
        }

        return RagasEvaluationOperationResult<RagasEvaluationRunResponse>.Ok(ToResponse(run));
    }

    private RagasEvaluationOperationResult<object> ValidateAvailability()
    {
        var value = options.Value;
        if (!value.Enabled)
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "ragas_evaluation_disabled",
                "RAGAS evaluation is disabled by Evaluation:Ragas:Enabled.",
                StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(value.AdminToken))
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "missing_admin_token",
                "RAGAS evaluation requires Evaluation:Ragas:AdminToken.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(secretProvider.GetEvaluatorApiKey()))
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "missing_evaluator_api_key",
                "RAGAS evaluation requires Evaluation:Ragas:ApiKey or DEEPSEEK_API_KEY.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return RagasEvaluationOperationResult<object>.Ok(new object());
    }

    private RagasEvaluationRunRecord CreateRunRecord(
        CreateRagasEvaluationRunRequest request,
        int loadedCaseCount)
    {
        var value = options.Value;

        return new RagasEvaluationRunRecord
        {
            RunId = $"ragas-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29],
            Status = RagasEvaluationRunStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            Request = new RagasEvaluationRequestSnapshot
            {
                CaseNames = request.CaseNames?.ToList() ?? [],
                MaxCases = request.MaxCases ?? loadedCaseCount,
                IncludeFullText = request.IncludeFullText,
                PreviewMaxChars = value.PreviewMaxChars,
                Query = request.Query
            }
        };
    }

    private void StartBackgroundRun(
        RagasEvaluationRunRecord run,
        IReadOnlyList<RagasDatasetCase> cases,
        CancellationTokenSource cts)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<RagasEvaluationRunner>();
                await runner.ExecuteAsync(run, cases, cts.Token);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RAGAS evaluation run {RunId} background task failed.", run.RunId);
                await MarkRunFailedAsync(run, exception);
            }
            finally
            {
                activeRuns.TryRemove(run.RunId, out _);
                cts.Dispose();
            }
        });
    }

    private async Task MarkRunFailedAsync(RagasEvaluationRunRecord run, Exception exception)
    {
        run.Status = RagasEvaluationRunStatus.Failed;
        run.Error = exception.Message;
        run.CompletedAt = DateTimeOffset.UtcNow;

        try
        {
            await store.UpsertAsync(run, CancellationToken.None);
        }
        catch (Exception storeException)
        {
            logger.LogError(
                storeException,
                "Could not persist failed RAGAS evaluation run {RunId}.",
                run.RunId);
        }
    }

    private static RagasEvaluationRunResponse ToResponse(RagasEvaluationRunRecord run) =>
        new()
        {
            RunId = run.RunId,
            Status = run.Status.ToString(),
            CreatedAt = run.CreatedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            Request = run.Request,
            Summary = run.Summary,
            Cases = run.Cases,
            Diagnostics = run.Diagnostics,
            Error = run.Error
        };
}
