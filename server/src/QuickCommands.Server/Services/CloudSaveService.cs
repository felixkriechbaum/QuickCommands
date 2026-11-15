using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickCommands.Server.Data;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Exceptions;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Mapping;
using QuickCommands.Server.Options;

namespace QuickCommands.Server.Services;

/// <summary>One opaque JSON document per user with a revision counter (optimistic concurrency via ETag/If-Match).</summary>
public sealed class CloudSaveService(AppDbContext db, ICurrentUser currentUser, IOptions<CloudSaveOptions> options, TimeProvider clock)
    : ICloudSaveService
{
    private const string MismatchMessage = "The cloud save was changed on another device. Load it again, merge and retry.";

    private IQueryable<CloudSaveRevision> MyRevisions => db.CloudSaveRevisions.Where(r => r.UserId == currentUser.Id);

    public async Task<CloudSaveDocument> GetCurrentAsync(CancellationToken ct)
    {
        var current = await MyRevisions.AsNoTracking().OrderByDescending(r => r.Revision).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("cloud_save_not_found", "There is no cloud save yet.");
        return new CloudSaveDocument(current.Revision, current.Content, current.CreatedAt);
    }

    public async Task<CloudSaveRevisionResponse> SaveAsync(string json, long? expectedRevision, CancellationToken ct)
    {
        var sizeBytes = Encoding.UTF8.GetByteCount(json);
        if (sizeBytes > options.Value.MaxDocumentBytes)
            throw new PayloadTooLargeException($"The cloud save must not exceed {options.Value.MaxDocumentBytes} bytes.");
        var schemaVersion = ReadSchemaVersion(json);

        var currentRevision = await MyRevisions.MaxAsync(r => (long?)r.Revision, ct);
        if (currentRevision != expectedRevision)
            throw new PreconditionFailedException("revision_mismatch", MismatchMessage);

        var revision = new CloudSaveRevision
        {
            UserId = currentUser.Id,
            Revision = (currentRevision ?? 0) + 1,
            SchemaVersion = schemaVersion,
            SizeBytes = sizeBytes,
            Content = json,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.CloudSaveRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsDuplicateKey())
        {
            // Another device saved the same revision number a moment earlier.
            throw new PreconditionFailedException("revision_mismatch", MismatchMessage);
        }

        var oldest = revision.Revision - options.Value.KeepRevisions;
        await MyRevisions.Where(r => r.Revision <= oldest).ExecuteDeleteAsync(ct);
        return revision.ToResponse();
    }

    public async Task<IReadOnlyList<CloudSaveRevisionResponse>> GetRevisionsAsync(CancellationToken ct)
    {
        var revisions = await MyRevisions.AsNoTracking()
            .OrderByDescending(r => r.Revision)
            .Select(r => new CloudSaveRevisionResponse(r.Revision, r.SchemaVersion, r.SizeBytes, r.CreatedAt))
            .ToListAsync(ct);
        return revisions;
    }

    public async Task<CloudSaveDocument> GetRevisionAsync(long revision, CancellationToken ct)
    {
        var found = await MyRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Revision == revision, ct)
            ?? throw new NotFoundException("revision_not_found", $"Revision {revision} does not exist (any more).");
        return new CloudSaveDocument(found.Revision, found.Content, found.CreatedAt);
    }

    public Task DeleteAsync(CancellationToken ct) => MyRevisions.ExecuteDeleteAsync(ct);

    /// <summary>The server ignores the content, but it must be a JSON object with an integer schemaVersion ≥ 1.</summary>
    private static int ReadSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("schemaVersion", out var version) &&
                version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value >= 1)
                return value;
        }
        catch (JsonException)
        {
            // Falls through to the error below.
        }
        throw new BadRequestException("invalid_document", "The cloud save must be a JSON object with an integer \"schemaVersion\" ≥ 1.");
    }
}
