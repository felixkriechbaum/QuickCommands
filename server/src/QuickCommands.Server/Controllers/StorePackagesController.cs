using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Setup;

namespace QuickCommands.Server.Controllers;

[ApiController]
[Route("api/v1/store/packages")]
public sealed class StorePackagesController(IStoreService store, IModerationService moderation) : ControllerBase
{
    /// <summary>Searches active packages.</summary>
    [HttpGet]
    [AllowAnonymous]
    public Task<PagedResponse<PackageSummaryResponse>> Search([FromQuery] SearchPackagesQuery query, CancellationToken ct) =>
        store.SearchAsync(query, ct);

    /// <summary>Update check for installed packages: status and version per known ID (unknown IDs are left out).</summary>
    [HttpGet("versions")]
    [AllowAnonymous]
    public Task<IReadOnlyList<PackageVersionResponse>> GetVersions([FromQuery] Guid[] ids, CancellationToken ct) =>
        store.GetVersionsAsync(ids, ct);

    /// <summary>Package details including all scripts.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PackageDetailResponse> Get(Guid id, CancellationToken ct) => store.GetAsync(id, ct);

    /// <summary>Publishes a new package (a handle is required).</summary>
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PackageDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PackageDetailResponse>> Publish(PackageRequest request, CancellationToken ct)
    {
        var package = await store.PublishAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = package.Id }, package);
    }

    /// <summary>Replaces title, description, category, tags and commands; increases the version.</summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PackageDetailResponse> Update(Guid id, PackageRequest request, CancellationToken ct) => store.UpdateAsync(id, request, ct);

    /// <summary>Deletes the package (author or admin). A tombstone stays for the update check.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await store.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Stars the package (idempotent).</summary>
    [HttpPut("{id:guid}/star")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Star(Guid id, CancellationToken ct)
    {
        await store.SetStarAsync(id, starred: true, ct);
        return NoContent();
    }

    /// <summary>Removes the star (idempotent).</summary>
    [HttpDelete("{id:guid}/star")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unstar(Guid id, CancellationToken ct)
    {
        await store.SetStarAsync(id, starred: false, ct);
        return NoContent();
    }

    /// <summary>Counts an installation – once per user and package.</summary>
    [HttpPost("{id:guid}/install")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Install(Guid id, CancellationToken ct)
    {
        await store.RecordInstallAsync(id, ct);
        return NoContent();
    }

    /// <summary>Reports the package. From 3 open reports it is hidden until an admin decides.</summary>
    [HttpPost("{id:guid}/reports")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(Guid id, ReportPackageRequest request, CancellationToken ct)
    {
        await moderation.ReportAsync(id, request, ct);
        return NoContent();
    }
}
