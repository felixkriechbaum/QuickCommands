using Microsoft.EntityFrameworkCore;
using QuickCommands.Server.Data;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Services;

/// <summary>
/// Deleting a package keeps only a tombstone (ID, status, removal date and reason), so installed copies can still
/// learn that the package is gone. Everything else – content, tags, stars, installs, reports, link to the author – is deleted.
/// A package that was already removed by an admin keeps status and reason (the "malicious" warning must survive).
/// </summary>
internal static class PackageTombstones
{
    /// <summary>Call inside a transaction.</summary>
    public static async Task CreateAsync(AppDbContext db, IQueryable<StorePackage> packages, DateTime now, CancellationToken ct)
    {
        var ids = await packages.Where(p => p.Status != PackageStatus.Deleted && p.OwnerId != null).Select(p => p.Id).ToListAsync(ct);
        if (ids.Count == 0)
            return;

        await db.Commands.Where(c => ids.Contains(c.PackageId)).ExecuteDeleteAsync(ct);
        await db.PackageTags.Where(t => ids.Contains(t.PackageId)).ExecuteDeleteAsync(ct);
        await db.Stars.Where(s => ids.Contains(s.PackageId)).ExecuteDeleteAsync(ct);
        await db.Installs.Where(i => ids.Contains(i.PackageId)).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => ids.Contains(r.PackageId)).ExecuteDeleteAsync(ct);

        var targets = db.Packages.Where(p => ids.Contains(p.Id));
        await targets.Where(p => p.Status != PackageStatus.Removed)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PackageStatus.Deleted).SetProperty(p => p.RemovalReason, (ReportReason?)null), ct);
        await targets.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.OwnerId, (Guid?)null)
            .SetProperty(p => p.Title, "")
            .SetProperty(p => p.Description, "")
            .SetProperty(p => p.CategoryId, (int?)null)
            .SetProperty(p => p.StarCount, 0)
            .SetProperty(p => p.InstallCount, 0)
            .SetProperty(p => p.OpenReportCount, 0)
            .SetProperty(p => p.UpdatedAt, now)
            .SetProperty(p => p.RemovedAt, p => p.RemovedAt ?? now), ct);
    }
}
