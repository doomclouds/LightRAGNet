using System.Collections.Concurrent;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationRunCoordinator(
    IOptions<RagasEvaluationOptions> options,
    RagasEvaluationDataLoader dataLoader,
    RagasEvaluationRunStore store,
    RagasEvaluationRunner runner,
    RagasEvaluationTextSnapshotter snapshotter,
    ILogger<RagasEvaluationRunCoordinator> logger)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeRuns = new();
    private readonly SemaphoreSlim createGate = new(1, 1);

    public async Task<RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>> CreateAsync(
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
        await createGate.WaitAsync(cancellationToken);
        try
        {
            var activeRun = await store.GetActiveAsync(cancellationToken);
            if (activeRun is not null)
            {
                return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Fail(
                    "active_run_exists",
                    "Another RAGAS evaluation run is already queued or running.",
                    StatusCodes.Status409Conflict);
            }

            var run = CreateRunRecord(request, cases.Count);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            activeRuns[run.RunId] = cts;

            await store.UpsertAsync(run, cancellationToken);
            StartBackgroundRun(run, cases, cts);

            return RagasEvaluationOperationResult<CreateRagasEvaluationRunResponse>.Ok(
                new CreateRagasEvaluationRunResponse
                {
                    RunId = run.RunId,
                    Status = run.Status.ToString(),
                    CreatedAt = run.CreatedAt,
                    Message = "RAGAS evaluation run queued."
                });
        }
        finally
        {
            createGate.Release();
        }
    }

    public async Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> GetAsync(
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

    public async Task<RagasEvaluationOperationResult<RagasEvaluationRunResponse>> CancelAsync(
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

        if (string.IsNullOrWhiteSpace(value.ApiKey))
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "missing_evaluator_api_key",
                "RAGAS evaluation requires Evaluation:Ragas:ApiKey.",
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
                await runner.ExecuteAsync(run, cases, cts.Token);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RAGAS evaluation run {RunId} background task failed.", run.RunId);
            }
            finally
            {
                activeRuns.TryRemove(run.RunId, out _);
                cts.Dispose();
            }
        });
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
