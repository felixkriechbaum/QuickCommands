using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickCommands.Server.Data;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Exceptions;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Mapping;
using QuickCommands.Server.Options;

namespace QuickCommands.Server.Services;

public sealed class ModerationService(
    AppDbContext db,
    ICurrentUser currentUser,
    IOptions<StoreOptions> options,
    TimeProvider clock,
    ILogger<ModerationService> logger) : IModerationService
{
    public async Task ReportAsync(Guid packageId, ReportPackageRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var package = await StoreService.LockPackageAsync(db, packageId, ct, track: true);
        if (package is null || package.Status is not (PackageStatus.Active or PackageStatus.Hidden))
            throw StoreService.PackageNotFound();
        if (package.OwnerId == currentUser.Id)
            throw new ConflictException("cannot_report_own", "You cannot report your own package.");

        var report = new StoreReport
        {
            PackageId = packageId,
            ReporterId = currentUser.Id,
            Reason = request.Reason,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.Reports.Add(report);
        package.OpenReportCount++;
        var hideNow = package.Status == PackageStatus.Active && package.OpenReportCount >= options.Value.AutoHideReportThreshold;
        if (hideNow)
            package.Status = PackageStatus.Hidden;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsDuplicateKey())
        {
            throw new ConflictException("already_reported", "You have already reported this package.");
        }
        await transaction.CommitAsync(ct);

        // Warnings trigger the loggerui alert – this is how the admin learns about reports.
        logger.LogWarning(
            "Package {PackageId} \"{PackageTitle}\" reported as {Reason} by {Reporter} ({OpenReportCount} open reports): {Comment}",
            package.Id, package.Title, request.Reason, currentUser.Handle ?? currentUser.FirebaseUid, package.OpenReportCount, report.Comment ?? "");
        if (hideNow)
            logger.LogWarning("Package {PackageId} \"{PackageTitle}\" hidden automatically after {OpenReportCount} reports",
                package.Id, package.Title, package.OpenReportCount);
    }

    public async Task<PagedResponse<ReportResponse>> GetReportsAsync(ReportListQuery query, CancellationToken ct)
    {
        var reports = db.Reports.AsNoTracking().Where(r => query.Open ? r.ResolvedAt == null : r.ResolvedAt != null);
        var total = await reports.CountAsync(ct);
        var page = await reports
            .Include(r => r.Package!).ThenInclude(p => p.Owner)
            .Include(r => r.Reporter)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return new PagedResponse<ReportResponse>(page.Select(r => r.ToResponse()).ToList(), query.Page, query.PageSize, total);
    }

    public async Task RemovePackageAsync(Guid packageId, RemovePackageRequest request, CancellationToken ct)
    {
        var package = await FindPackageAsync(packageId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        package.Status = PackageStatus.Removed;
        package.RemovalReason = request.Reason;
        package.RemovedAt = now;
        package.OpenReportCount = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await ResolveOpenReportsAsync(packageId, ReportResolution.Upheld, now, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Package {PackageId} removed by an admin as {Reason}", packageId, request.Reason);
    }

    public async Task RestorePackageAsync(Guid packageId, CancellationToken ct)
    {
        var package = await FindPackageAsync(packageId, ct);
        if (package.OwnerId is null)
            throw new ConflictException("package_deleted", "The author deleted this package; only the tombstone is left.");
        if (package.Status == PackageStatus.Active)
            return;

        var now = clock.GetUtcNow().UtcDateTime;
        package.Status = PackageStatus.Active;
        package.RemovalReason = null;
        package.RemovedAt = null;
        package.OpenReportCount = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await ResolveOpenReportsAsync(packageId, ReportResolution.Dismissed, now, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Package {PackageId} restored by an admin", packageId);
    }

    public async Task BanUserAsync(Guid userId, BanUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("user_not_found", "The user does not exist.");
        var now = clock.GetUtcNow().UtcDateTime;
        user.IsBanned = true;
        user.BannedAt = now;
        user.BanReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (request.RemovePackagesWithReason is { } reason)
        {
            var packages = db.Packages.Where(p => p.OwnerId == userId && (p.Status == PackageStatus.Active || p.Status == PackageStatus.Hidden));
            var ids = await packages.Select(p => p.Id).ToListAsync(ct);
            await packages.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PackageStatus.Removed)
                .SetProperty(p => p.RemovalReason, reason)
                .SetProperty(p => p.RemovedAt, now)
                .SetProperty(p => p.OpenReportCount, 0), ct);
            foreach (var id in ids)
                await ResolveOpenReportsAsync(id, ReportResolution.Upheld, now, ct);
        }
        await transaction.CommitAsync(ct);
        logger.LogInformation("User {UserId} banned by an admin", userId);
    }

    public async Task UnbanUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("user_not_found", "The user does not exist.");
        user.IsBanned = false;
        user.BannedAt = null;
        user.BanReason = null;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} unbanned by an admin", userId);
    }

    private async Task<StorePackage> FindPackageAsync(Guid packageId, CancellationToken ct) =>
        await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId && p.Status != PackageStatus.Deleted, ct)
        ?? throw StoreService.PackageNotFound();

    private Task ResolveOpenReportsAsync(Guid packageId, ReportResolution resolution, DateTime now, CancellationToken ct) =>
        db.Reports.Where(r => r.PackageId == packageId && r.ResolvedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResolvedAt, now).SetProperty(r => r.Resolution, resolution), ct);
}
