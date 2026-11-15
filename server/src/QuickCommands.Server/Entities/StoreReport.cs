namespace QuickCommands.Server.Entities;

public enum ReportReason
{
    Malicious,
    Spam,
    Broken,
    Inappropriate,
    Other,
}

public enum ReportResolution
{
    /// <summary>The package was removed.</summary>
    Upheld,

    /// <summary>The package was restored – the report was unfounded.</summary>
    Dismissed,
}

public sealed class StoreReport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PackageId { get; set; }
    public StorePackage? Package { get; set; }
    public Guid ReporterId { get; set; }
    public User? Reporter { get; set; }

    public ReportReason Reason { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public ReportResolution? Resolution { get; set; }
}
