using LightRAGNet.Server.Services.SystemHealth;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemHealthController(SystemHealthService healthService) : ControllerBase
{
    [HttpGet("health")]
    public async Task<ActionResult<SystemHealthResponse>> GetHealth(CancellationToken cancellationToken)
    {
        var result = await healthService.GetHealthAsync(cancellationToken);
        return Ok(result);
    }
}
