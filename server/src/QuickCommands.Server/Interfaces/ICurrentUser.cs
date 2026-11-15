namespace QuickCommands.Server.Interfaces;

/// <summary>The user of the current request, filled in by the CurrentUserMiddleware.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Database ID. Throws for anonymous requests – only use behind [Authorize].</summary>
    Guid Id { get; }

    Guid? IdOrNull { get; }
    string? FirebaseUid { get; }
    string? Handle { get; }
    bool IsAdmin { get; }
}
