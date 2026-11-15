using QuickCommands.Server.Entities;
using QuickCommands.Server.Interfaces;

namespace QuickCommands.Server.Services;

/// <summary>Scoped; the CurrentUserMiddleware calls <see cref="Set"/> once per authenticated request.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private User? _user;

    public bool IsAuthenticated => _user is not null;
    public Guid Id => _user?.Id ?? throw new InvalidOperationException("The request is not authenticated.");
    public Guid? IdOrNull => _user?.Id;
    public string? FirebaseUid => _user?.FirebaseUid;
    public string? Handle => _user?.Handle;
    public bool IsAdmin { get; private set; }

    internal void Set(User user, bool isAdmin)
    {
        _user = user;
        IsAdmin = isAdmin;
    }
}
