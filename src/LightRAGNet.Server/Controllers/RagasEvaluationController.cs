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
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        var result = await coordinator.CreateAsync(request ?? new CreateRagasEvaluationRunRequest(), cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RagasEvaluationRunResponse>> GetAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        var result = await coordinator.GetAsync(runId, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{runId}/cancel")]
    public async Task<ActionResult<RagasEvaluationRunResponse>> CancelAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
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

        return StatusCode(result.StatusCode, new
        {
            code = result.ErrorCode,
            message = result.ErrorMessage
        });
    }

    private bool IsAuthorized()
    {
        var expectedToken = options.Value.AdminToken;
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(TokenHeaderName, out var headerValues))
        {
            return false;
        }

        var actualToken = headerValues.Count == 1 ? headerValues[0] : null;
        return TokenEquals(actualToken, expectedToken);
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
