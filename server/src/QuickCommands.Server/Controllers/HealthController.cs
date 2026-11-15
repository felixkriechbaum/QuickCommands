using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommands.Server.Data;
using QuickCommands.Server.Dtos.Responses;

namespace QuickCommands.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController(AppDbContext db, ILogger<HealthController> logger) : ControllerBase
{
    /// <summary>Liveness and database check for Docker, nginx and monitoring.</summary>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        bool databaseOk;
        try
        {
            databaseOk = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            databaseOk = false;
        }

        if (databaseOk)
            return Ok(new HealthResponse("ok", AppInfo.Version, "ok"));

        logger.LogError("Health check failed: the database is not reachable");
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("degraded", AppInfo.Version, "unreachable"));
    }
}
