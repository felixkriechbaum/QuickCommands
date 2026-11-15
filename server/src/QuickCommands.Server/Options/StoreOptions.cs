namespace QuickCommands.Server.Options;

public sealed class StoreOptions
{
    public const string Section = "Store";

    /// <summary>Firebase UIDs with admin rights, comma separated (easy to set as a single environment variable).</summary>
    public string AdminUids { get; set; } = "";

    /// <summary>Open reports from different users after which a package is hidden automatically.</summary>
    public int AutoHideReportThreshold { get; set; } = 3;

    public IReadOnlySet<string> AdminUidSet =>
        AdminUids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
}
