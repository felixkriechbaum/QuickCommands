using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Exceptions;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Setup;

namespace QuickCommands.Server.Controllers;

/// <summary>
/// One JSON document per user. Concurrency: GET returns ETag "&lt;revision&gt;", PUT needs If-Match with that ETag
/// (or If-None-Match: * for the very first save) and answers 412 if another device saved in between.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/cloud-save")]
public sealed class CloudSaveController(ICloudSaveService cloudSave) : ControllerBase
{
    /// <summary>Hard limit for the body; the configured document limit (1 MB) is checked by the service.</summary>
    private const long MaxRequestBytes = 2 * 1024 * 1024;

    /// <summary>The current document exactly as it was saved.</summary>
    [HttpGet]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var document = await cloudSave.GetCurrentAsync(ct);
        return Document(document, withETag: true);
    }

    /// <summary>Saves a new revision. Requires If-Match: "&lt;revision&gt;" or, for the first save, If-None-Match: *.</summary>
    [HttpPut]
    [RequestSizeLimit(MaxRequestBytes)]
    [Consumes("application/json")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<CloudSaveRevisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<CloudSaveRevisionResponse> Put([FromBody] JsonElement document, CancellationToken ct)
    {
        var saved = await cloudSave.SaveAsync(document.GetRawText(), ReadExpectedRevision(), ct);
        Response.Headers.ETag = ToETag(saved.Revision);
        return saved;
    }

    /// <summary>Metadata of the stored revisions, newest first (at most 10).</summary>
    [HttpGet("revisions")]
    public Task<IReadOnlyList<CloudSaveRevisionResponse>> GetRevisions(CancellationToken ct) => cloudSave.GetRevisionsAsync(ct);

    /// <summary>The content of an older revision. To restore it, PUT it again as a new revision.</summary>
    [HttpGet("revisions/{revision:long}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRevision(long revision, CancellationToken ct) =>
        Document(await cloudSave.GetRevisionAsync(revision, ct), withETag: false);

    /// <summary>Turns cloud save off: all revisions are deleted.</summary>
    [HttpDelete]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await cloudSave.DeleteAsync(ct);
        return NoContent();
    }

    private ContentResult Document(CloudSaveDocument document, bool withETag)
    {
        if (withETag)
            Response.Headers.ETag = ToETag(document.Revision);
        Response.Headers.LastModified = document.CreatedAt.ToString("R", CultureInfo.InvariantCulture);
        return Content(document.Json, "application/json");
    }

    private static string ToETag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    /// <returns>The revision from If-Match, or null for If-None-Match: * (create only).</returns>
    private long? ReadExpectedRevision()
    {
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (ifMatch.Length > 0)
        {
            if (EntityTagHeaderValue.TryParse(ifMatch, out var tag) && !tag.Equals(EntityTagHeaderValue.Any) &&
                long.TryParse(tag.Tag.AsSpan().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
                return revision;
            throw new PreconditionRequiredException("If-Match must contain the ETag of the current revision, e.g. \"3\".");
        }

        if (Request.Headers.IfNoneMatch.ToString().Trim() == "*")
            return null;

        throw new PreconditionRequiredException("Send If-Match with the current ETag, or If-None-Match: * for the first save.");
    }
}
