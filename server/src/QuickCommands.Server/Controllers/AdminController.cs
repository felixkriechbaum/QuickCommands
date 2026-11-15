using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Setup;

namespace QuickCommands.Server.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminController(IModerationService moderation) : ControllerBase
{
    /// <summary>Reports, newest first (open ones by default).</summary>
    [HttpGet("reports")]
    public Task<PagedResponse<ReportResponse>> GetReports([FromQuery] ReportListQuery query, CancellationToken ct) =>
        moderation.GetReportsAsync(query, ct);

    /// <summary>Removes a package; its open reports count as upheld.</summary>
    [HttpPost("packages/{id:guid}/remove")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemovePackage(Guid id, RemovePackageRequest request, CancellationToken ct)
    {
        await moderation.RemovePackageAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Makes a hidden or removed package active again; its open reports are dismissed.</summary>
    [HttpPost("packages/{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RestorePackage(Guid id, CancellationToken ct)
    {
        await moderation.RestorePackageAsync(id, ct);
        return NoContent();
    }

    /// <summary>Bans a user; optionally removes all their packages.</summary>
    [HttpPost("users/{id:guid}/ban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> BanUser(Guid id, BanUserRequest request, CancellationToken ct)
    {
        await moderation.BanUserAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("users/{id:guid}/unban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnbanUser(Guid id, CancellationToken ct)
    {
        await moderation.UnbanUserAsync(id, ct);
        return NoContent();
    }
}
