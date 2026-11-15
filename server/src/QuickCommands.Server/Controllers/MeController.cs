using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Middlewares;
using QuickCommands.Server.Setup;

namespace QuickCommands.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(IUserService users) : ControllerBase
{
    /// <summary>The profile of the signed-in user.</summary>
    [HttpGet]
    [AllowBanned]
    public Task<MeResponse> Get(CancellationToken ct) => users.GetMeAsync(ct);

    /// <summary>Sets or changes the public handle (required before publishing).</summary>
    [HttpPut]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<MeResponse> Update(UpdateMeRequest request, CancellationToken ct) => users.UpdateMeAsync(request, ct);

    /// <summary>Own packages in every state except deleted.</summary>
    [HttpGet("packages")]
    public Task<IReadOnlyList<PackageSummaryResponse>> GetPackages(CancellationToken ct) => users.GetMyPackagesAsync(ct);

    /// <summary>Deletes cloud save, stars, installs and reports; own packages become tombstones.</summary>
    [HttpDelete]
    [AllowBanned]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await users.DeleteMeAsync(ct);
        return NoContent();
    }
}
