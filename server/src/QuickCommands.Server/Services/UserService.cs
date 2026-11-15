using Microsoft.EntityFrameworkCore;
using QuickCommands.Server.Data;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Exceptions;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Mapping;
using QuickCommands.Server.Validators;

namespace QuickCommands.Server.Services;

public sealed class UserService(AppDbContext db, ICurrentUser currentUser, TimeProvider clock, ILogger<UserService> logger) : IUserService
{
    private Task<User> LoadMeAsync(CancellationToken ct) => db.Users.SingleAsync(u => u.Id == currentUser.Id, ct);

    public async Task<MeResponse> GetMeAsync(CancellationToken ct) => (await LoadMeAsync(ct)).ToMeResponse(currentUser.IsAdmin);

    public async Task<MeResponse> UpdateMeAsync(UpdateMeRequest request, CancellationToken ct)
    {
        var handle = StoreRules.NormalizeHandle(request.Handle);
        var user = await LoadMeAsync(ct);
        if (user.Handle == handle)
            return user.ToMeResponse(currentUser.IsAdmin);
        if (StoreRules.IsReservedHandle(handle) || await db.Users.AnyAsync(u => u.Handle == handle && u.Id != user.Id, ct))
            throw HandleTaken();

        user.Handle = handle;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsDuplicateKey())
        {
            throw HandleTaken();
        }
        return user.ToMeResponse(currentUser.IsAdmin);
    }

    public async Task<IReadOnlyList<PackageSummaryResponse>> GetMyPackagesAsync(CancellationToken ct)
    {
        var rows = await db.Packages.AsNoTracking()
            .Where(p => p.OwnerId == currentUser.Id && p.Status != PackageStatus.Deleted)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(PackageMapping.ToSummaryRow)
            .AsSplitQuery()
            .ToListAsync(ct);
        return rows.Select(r => r.ToResponse()).ToList();
    }

    public async Task DeleteMeAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Own packages become tombstones – whoever installed them keeps the command and sees "deleted".
        await PackageTombstones.CreateAsync(db, db.Packages.Where(p => p.OwnerId == me), now, ct);

        // Stars and open reports on other packages: lower their counters first, then delete the rows.
        await db.Packages.Where(p => db.Stars.Any(s => s.PackageId == p.Id && s.UserId == me) && p.StarCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StarCount, p => p.StarCount - 1), ct);
        await db.Packages.Where(p => db.Reports.Any(r => r.PackageId == p.Id && r.ReporterId == me && r.ResolvedAt == null) && p.OpenReportCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.OpenReportCount, p => p.OpenReportCount - 1), ct);
        await db.Stars.Where(s => s.UserId == me).ExecuteDeleteAsync(ct);
        await db.Installs.Where(i => i.UserId == me).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.ReporterId == me).ExecuteDeleteAsync(ct);
        await db.CloudSaveRevisions.Where(r => r.UserId == me).ExecuteDeleteAsync(ct);

        // A banned UID stays as a ban entry, so signing in again does not lift the ban.
        var user = await LoadMeAsync(ct);
        if (user.IsBanned)
            await db.Users.Where(u => u.Id == me).ExecuteUpdateAsync(s => s.SetProperty(u => u.Handle, (string?)null), ct);
        else
            await db.Users.Where(u => u.Id == me).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
        logger.LogInformation("Account {UserId} deleted (banned: {IsBanned})", me, user.IsBanned);
    }

    private static ConflictException HandleTaken() => new("handle_taken", "This handle is already taken or reserved.");
}
