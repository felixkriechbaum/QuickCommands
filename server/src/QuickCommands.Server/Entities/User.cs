namespace QuickCommands.Server.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Firebase UID ("sub" of the ID token).</summary>
    public string FirebaseUid { get; set; } = "";

    /// <summary>Public name in the store (lowercase). Null until the user picks one.</summary>
    public string? Handle { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BanReason { get; set; }
}
