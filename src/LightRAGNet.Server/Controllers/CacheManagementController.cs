using LightRAGNet.Server.Services.CacheManagement;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/cache-management")]
public sealed class CacheManagementController(CacheManagementService service) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<CacheOverviewResponse>> GetOverviewAsync(
        [FromQuery] string? workspace,
        [FromQuery] string? window,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetOverviewAsync(workspace, window, cancellationToken));
    }

    [HttpPost("clear")]
    public async Task<ActionResult<CacheClearResponse>> ClearAsync(
        [FromBody] CacheClearRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.ClearAsync(request, cancellationToken);
        return response.Succeeded ? Ok(response) : BadRequest(response);
    }
}
