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

public sealed class StoreService(AppDbContext db, ICurrentUser currentUser, TimeProvider clock, ILogger<StoreService> logger)
    : IStoreService
{
    public const int MaxVersionIds = 100;

    public async Task<PagedResponse<PackageSummaryResponse>> SearchAsync(SearchPackagesQuery query, CancellationToken ct)
    {
        var packages = db.Packages.AsNoTracking().Where(p => p.Status == PackageStatus.Active);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var slug = query.Category.Trim().ToLowerInvariant();
            packages = packages.Where(p => p.Category!.Slug == slug);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = StoreRules.NormalizeTag(query.Tag);
            packages = packages.Where(p => p.Tags.Any(t => t.Tag == tag));
        }
        if (query.Language is { } language)
            packages = packages.Where(p => p.Commands.Any(c => c.Language == language));
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            // The default MySQL collation (utf8mb4_0900_ai_ci) makes this case-insensitive.
            var text = query.Q.Trim();
            packages = packages.Where(p =>
                p.Title.Contains(text) ||
                p.Description.Contains(text) ||
                p.Tags.Any(t => t.Tag.Contains(text)) ||
                p.Commands.Any(c => c.Name.Contains(text)));
        }

        var total = await packages.CountAsync(ct);
        var rows = await Sort(packages, query.Sort)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(PackageMapping.ToSummaryRow)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResponse<PackageSummaryResponse>(rows.Select(r => r.ToResponse()).ToList(), query.Page, query.PageSize, total);
    }

    private IQueryable<StorePackage> Sort(IQueryable<StorePackage> packages, PackageSort sort)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return sort switch
        {
            PackageSort.Stars => packages.OrderByDescending(p => p.StarCount).ThenByDescending(p => p.CreatedAt),
            PackageSort.Installs => packages.OrderByDescending(p => p.InstallCount).ThenByDescending(p => p.CreatedAt),
            PackageSort.New => packages.OrderByDescending(p => p.CreatedAt),
            PackageSort.Updated => packages.OrderByDescending(p => p.UpdatedAt),
            // Hacker-News-style: popularity damped by age, so new packages get a chance. Computed in SQL.
            _ => packages
                .OrderByDescending(p => (p.StarCount * 3 + p.InstallCount + 1) /
                                        Math.Pow(EF.Functions.DateDiffHour(p.CreatedAt, now) + 2, 1.5))
                .ThenByDescending(p => p.CreatedAt),
        };
    }

    public async Task<PackageDetailResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var package = await db.Packages.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Owner)
            .Include(p => p.Tags)
            .Include(p => p.Commands)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        var isOwner = package?.OwnerId is { } ownerId && ownerId == currentUser.IdOrNull;
        if (package is null || package.Status == PackageStatus.Deleted ||
            (package.Status != PackageStatus.Active && !isOwner && !currentUser.IsAdmin))
            throw PackageNotFound();

        bool? starred = null, installed = null;
        if (currentUser.IdOrNull is { } me)
        {
            starred = await db.Stars.AnyAsync(s => s.PackageId == id && s.UserId == me, ct);
            installed = await db.Installs.AnyAsync(i => i.PackageId == id && i.UserId == me, ct);
        }
        return package.ToDetailResponse(starred, installed, isOwner);
    }

    public async Task<IReadOnlyList<PackageVersionResponse>> GetVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count > MaxVersionIds)
            throw new BadRequestException("validation_failed", $"At most {MaxVersionIds} IDs can be checked at once.");
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
            return [];

        var packages = await db.Packages.AsNoTracking().Where(p => distinct.Contains(p.Id)).ToListAsync(ct);
        return packages.Select(p => p.ToVersionResponse()).ToList();
    }

    public async Task<PackageDetailResponse> PublishAsync(PackageRequest request, CancellationToken ct)
    {
        var owner = await db.Users.SingleAsync(u => u.Id == currentUser.Id, ct);
        if (owner.Handle is null)
            throw new ConflictException("handle_required", "Choose a handle (PUT /api/v1/me) before publishing.");

        var now = clock.GetUtcNow().UtcDateTime;
        var package = new StorePackage
        {
            OwnerId = owner.Id,
            Owner = owner,
            Status = PackageStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await ApplyAsync(package, request, ct);
        db.Packages.Add(package);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Package {PackageId} published by {Handle} with {CommandCount} commands", package.Id, owner.Handle, package.Commands.Count);
        return package.ToDetailResponse(starredByMe: false, installedByMe: false, isOwner: true);
    }

    public async Task<PackageDetailResponse> UpdateAsync(Guid id, PackageRequest request, CancellationToken ct)
    {
        var package = await db.Packages
            .Include(p => p.Owner)
            .Include(p => p.Tags)
            .Include(p => p.Commands)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null || package.Status == PackageStatus.Deleted)
            throw PackageNotFound();
        if (package.OwnerId != currentUser.Id)
            throw new ForbiddenException(message: "Only the author can edit this package.");
        if (package.Status != PackageStatus.Active)
            throw new ConflictException("package_locked", "The package is hidden or removed and cannot be edited.");

        await ApplyAsync(package, request, ct);
        package.Version++;
        package.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        var starred = await db.Stars.AnyAsync(s => s.PackageId == id && s.UserId == currentUser.Id, ct);
        var installed = await db.Installs.AnyAsync(i => i.PackageId == id && i.UserId == currentUser.Id, ct);
        return package.ToDetailResponse(starred, installed, isOwner: true);
    }

    /// <summary>Copies the request onto the package. Commands are replaced, tags are diffed (they are keyed by their value).</summary>
    private async Task ApplyAsync(StorePackage package, PackageRequest request, CancellationToken ct)
    {
        var slug = request.Category.Trim().ToLowerInvariant();
        package.Category = await db.Categories.FirstOrDefaultAsync(c => c.Slug == slug, ct)
            ?? throw new BadRequestException("unknown_category", $"The category '{slug}' does not exist. See GET /api/v1/categories.");
        package.CategoryId = package.Category.Id;
        package.Title = request.Title.Trim();
        package.Description = request.Description?.Trim() ?? "";

        // New rows are added to the context explicitly: their keys are set up front, so EF would otherwise
        // take them for existing rows and send UPDATEs.
        var tags = StoreRules.NormalizeTags(request.Tags);
        package.Tags.RemoveAll(t => !tags.Contains(t.Tag));
        foreach (var tag in tags.Where(t => package.Tags.All(existing => existing.Tag != t)))
        {
            var row = new StorePackageTag { PackageId = package.Id, Tag = tag };
            db.PackageTags.Add(row);
            if (!package.Tags.Contains(row)) // EF fixes up the collection itself when the package is tracked.
                package.Tags.Add(row);
        }

        package.Commands.Clear();
        var commands = request.Commands.Select((c, index) => new StoreCommand
        {
            PackageId = package.Id,
            SortOrder = index,
            Name = StoreRules.NormalizeCommandName(c.Name),
            Language = c.Language,
            Description = c.Description?.Trim() ?? "",
            Script = c.Script,
            ParametersJson = c.Parameters is { ValueKind: not System.Text.Json.JsonValueKind.Null } p ? p.GetRawText() : null,
        }).ToList();
        db.Commands.AddRange(commands);
        package.Commands.AddRange(commands.Where(c => !package.Commands.Contains(c)));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null || package.Status == PackageStatus.Deleted || package.OwnerId is null)
            throw PackageNotFound();
        if (package.OwnerId != currentUser.Id && !currentUser.IsAdmin)
            throw new ForbiddenException(message: "Only the author can delete this package.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PackageTombstones.CreateAsync(db, db.Packages.Where(p => p.Id == id), clock.GetUtcNow().UtcDateTime, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Package {PackageId} deleted", id);
    }

    public async Task SetStarAsync(Guid id, bool starred, CancellationToken ct)
    {
        var me = currentUser.Id;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var package = await LockPackageAsync(db, id, ct);
        if (starred)
        {
            if (package?.Status != PackageStatus.Active)
                throw PackageNotFound();
            if (!await TryInsertAsync(new StoreStar { PackageId = id, UserId = me, CreatedAt = clock.GetUtcNow().UtcDateTime }, ct))
                return;
            await db.Packages.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.StarCount, p => p.StarCount + 1), ct);
        }
        else
        {
            if (package is null)
                throw PackageNotFound();
            var removed = await db.Stars.Where(s => s.PackageId == id && s.UserId == me).ExecuteDeleteAsync(ct);
            if (removed == 0)
                return;
            await db.Packages.Where(p => p.Id == id && p.StarCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StarCount, p => p.StarCount - 1), ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task RecordInstallAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if ((await LockPackageAsync(db, id, ct))?.Status != PackageStatus.Active)
            throw PackageNotFound();
        if (!await TryInsertAsync(new StoreInstall { PackageId = id, UserId = currentUser.Id, CreatedAt = clock.GetUtcNow().UtcDateTime }, ct))
            return;
        await db.Packages.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.InstallCount, p => p.InstallCount + 1), ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Locks the package row for the rest of the transaction (SELECT … FOR UPDATE). Every counter change on a package
    /// takes this lock first, so concurrent stars/installs/reports queue up instead of deadlocking on the foreign-key locks.
    /// </summary>
    internal static async Task<StorePackage?> LockPackageAsync(AppDbContext db, Guid id, CancellationToken ct, bool track = false)
    {
        var query = db.Packages.FromSql($"SELECT * FROM store_packages WHERE id = {id} FOR UPDATE");
        // Not composed (no FirstOrDefault in SQL), so MySQL sees the FOR UPDATE as written.
        var rows = await (track ? query : query.AsNoTracking()).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    /// <returns>False if the row already exists (star / install is idempotent).</returns>
    private async Task<bool> TryInsertAsync<T>(T row, CancellationToken ct) where T : class
    {
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e) when (e.IsDuplicateKey())
        {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    internal static NotFoundException PackageNotFound() => new("package_not_found", "The package does not exist or is not visible.");
}
