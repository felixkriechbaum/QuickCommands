using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using QuickCommands.Server.Middlewares;
using QuickCommands.Server.Options;

namespace QuickCommands.Server.Setup;

/// <summary>Admin = Firebase UID listed in Store:AdminUids (no Firebase custom claims, so no Admin SDK needed).</summary>
public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminAuthorizationHandler(IOptionsMonitor<StoreOptions> options) : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var uid = context.User.FindFirst(CurrentUserMiddleware.UidClaim)?.Value;
        if (uid is not null && options.CurrentValue.AdminUidSet.Contains(uid))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
