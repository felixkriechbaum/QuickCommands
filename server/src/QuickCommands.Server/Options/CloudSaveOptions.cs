namespace QuickCommands.Server.Options;

public sealed class CloudSaveOptions
{
    public const string Section = "CloudSave";

    public int MaxDocumentBytes { get; set; } = 1024 * 1024;

    /// <summary>How many revisions are kept per user (including the current one).</summary>
    public int KeepRevisions { get; set; } = 10;
}
