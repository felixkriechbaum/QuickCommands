namespace QuickCommands.Server.Entities;

public enum PackageStatus
{
    Active,

    /// <summary>Hidden automatically after enough reports, waiting for an admin.</summary>
    Hidden,

    /// <summary>Removed by an admin (see <see cref="StorePackage.RemovalReason"/>).</summary>
    Removed,

    /// <summary>Deleted by the author or with the account – only a tombstone is left.</summary>
    Deleted,
}

public sealed class StorePackage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Null for tombstones (the link to the author is removed on deletion).</summary>
    public Guid? OwnerId { get; set; }
    public User? Owner { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Increases with every edit, so clients can offer updates.</summary>
    public int Version { get; set; } = 1;

    public PackageStatus Status { get; set; }
    public ReportReason? RemovalReason { get; set; }

    public int StarCount { get; set; }
    public int InstallCount { get; set; }

    /// <summary>Unresolved reports – drives the automatic hiding.</summary>
    public int OpenReportCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    public List<StoreCommand> Commands { get; set; } = [];
    public List<StorePackageTag> Tags { get; set; } = [];
}
