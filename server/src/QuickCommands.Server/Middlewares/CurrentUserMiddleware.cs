using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickCommands.Server.Data;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Exceptions;
using QuickCommands.Server.Options;
using QuickCommands.Server.Services;
using Serilog;
using Serilog.Context;

namespace QuickCommands.Server.Middlewares;

/// <summary>
/// Runs after authentication: creates the user row on the first request of a Firebase UID, keeps LastSeenAt roughly
/// up to date, fills ICurrentUser and rejects banned users.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public const string UidClaim = "sub";
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromHours(1);

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        CurrentUser currentUser,
        IOptions<StoreOptions> storeOptions,
        TimeProvider clock,
        IDiagnosticContext diagnosticContext)
    {
        var uid = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(UidClaim)?.Value : null;
        if (string.IsNullOrEmpty(uid))
        {
            await next(context);
            return;
        }

        var user = await GetOrCreateAsync(db, uid, clock.GetUtcNow().UtcDateTime, context.RequestAborted);
        currentUser.Set(user, storeOptions.Value.AdminUidSet.Contains(uid));
        diagnosticContext.Set("uid", uid);

        if (user.IsBanned && context.GetEndpoint()?.Metadata.GetMetadata<AllowBannedAttribute>() is null)
            throw new ForbiddenException("user_banned", "This account is banned.");

        using (LogContext.PushProperty("uid", uid))
            await next(context);
    }

    private static async Task<User> GetOrCreateAsync(AppDbContext db, string uid, DateTime now, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.FirebaseUid == uid, ct);
        if (user is not null)
        {
            if (now - user.LastSeenAt > LastSeenResolution)
            {
                user.LastSeenAt = now;
                await db.SaveChangesAsync(ct);
            }
            return user;
        }

        user = new User { FirebaseUid = uid, CreatedAt = now, LastSeenAt = now };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException e) when (e.IsDuplicateKey())
        {
            // Two first requests of the same user at once – the other one created the row.
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.SingleAsync(u => u.FirebaseUid == uid, ct);
        }
    }
}
