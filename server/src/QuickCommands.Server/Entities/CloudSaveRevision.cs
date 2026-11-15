namespace QuickCommands.Server.Entities;

/// <summary>One stored version of a user's cloud save. The newest revision is the current document.</summary>
public sealed class CloudSaveRevision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Increases by one per save, starting at 1. Unique per user – guards against concurrent writes.</summary>
    public long Revision { get; set; }

    public int SchemaVersion { get; set; }
    public int SizeBytes { get; set; }

    /// <summary>The document exactly as the client sent it (text, not the MySQL JSON type, which would reformat it).</summary>
    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}
