using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/evaluation/ragas/runs")]
public sealed class RagasEvaluationController(
    IOptions<RagasEvaluationOptions> options,
    RagasEvaluationRunCoordinator coordinator) : ControllerBase
{
    private const string TokenHeaderName = "X-Evaluation-Token";

    [HttpPost]
    public async Task<ActionResult<CreateRagasEvaluationRunResponse>> CreateAsync(
        [FromBody] CreateRagasEvaluationRunRequest? request,
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.CreateAsync(request ?? new CreateRagasEvaluationRunRequest(), cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RagasEvaluationRunResponse>> GetAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.GetAsync(runId, cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet]
    public async Task<ActionResult<RagasEvaluationRunListResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.ListAsync(cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{runId}/export")]
    public async Task<ActionResult> ExportAsync(
        string runId,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.ExportAsync(runId, format, cancellationToken);
        if (!result.Success)
        {
            return ToErrorResult(result);
        }

        var export = result.Value!;
        return File(
            Encoding.UTF8.GetBytes(export.Content),
            export.ContentType,
            export.FileName);
    }

    [HttpGet("{runId}/compare/{baselineRunId}")]
    public async Task<ActionResult<RagasEvaluationComparisonResponse>> CompareAsync(
        string runId,
        string baselineRunId,
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.CompareAsync(runId, baselineRunId, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{runId}/cancel")]
    public async Task<ActionResult<RagasEvaluationRunResponse>> CancelAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (ValidateRequestAccess() is { } failure)
        {
            return failure;
        }

        var result = await coordinator.CancelAsync(runId, cancellationToken);

        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(RagasEvaluationOperationResult<T> result)
    {
        if (result.Success)
        {
            return StatusCode(result.StatusCode, result.Value);
        }

        return ToErrorResult(result);
    }

    private ObjectResult ToErrorResult<T>(RagasEvaluationOperationResult<T> result)
    {
        return StatusCode(result.StatusCode, new
        {
            code = result.ErrorCode,
            message = result.ErrorMessage
        });
    }

    private ActionResult? ValidateRequestAccess()
    {
        var expectedToken = options.Value.AdminToken;
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "missing_admin_token",
                message = "RAGAS evaluation requires Evaluation:Ragas:AdminToken."
            });
        }

        if (!Request.Headers.TryGetValue(TokenHeaderName, out var headerValues))
        {
            return Unauthorized();
        }

        var actualToken = headerValues.Count == 1 ? headerValues[0] : null;
        return TokenEquals(actualToken, expectedToken)
            ? null
            : Unauthorized();
    }

    private static bool TokenEquals(string? actualToken, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(actualToken))
        {
            return false;
        }

        var actualBytes = Encoding.UTF8.GetBytes(actualToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
