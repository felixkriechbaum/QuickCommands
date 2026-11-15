namespace QuickCommands.Server.Entities;

public sealed class StoreStar
{
    public Guid PackageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
