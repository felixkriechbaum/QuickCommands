namespace QuickCommands.Server.Entities;

public sealed class StorePackageTag
{
    public Guid PackageId { get; set; }
    public string Tag { get; set; } = "";
}
