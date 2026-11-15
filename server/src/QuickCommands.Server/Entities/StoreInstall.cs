namespace QuickCommands.Server.Entities;

/// <summary>Counted once per user and package, so the ranking cannot be pushed by reinstalling.</summary>
public sealed class StoreInstall
{
    public Guid PackageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
