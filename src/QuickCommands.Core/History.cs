using System.Globalization;
using System.Text.Json;

namespace QuickCommands.Core;

public sealed record HistoryEntry(string FilePath, DateTime TimestampUtc);

/// <summary>
/// Snapshots of commands.json, one per save, in &lt;data folder&gt;/history. Lives next to commands.json, so a
/// synced data folder syncs its history too.
/// </summary>
public sealed class HistoryStore(string dataDirectory)
{
    public const string FolderName = "history";
    public const int MaxEntries = 50;
    private const string Prefix = "commands-";
    private const string TimeFormat = "yyyyMMdd-HHmmss-fff";

    public string Folder => Path.Combine(dataDirectory, FolderName);

    /// <summary>Stores a snapshot unless it equals the newest one. Old snapshots beyond <see cref="MaxEntries"/> are deleted.</summary>
    public void Record(string json, DateTime? timestampUtc = null)
    {
        var latest = List().FirstOrDefault();
        if (latest is not null && File.ReadAllText(latest.FilePath) == json)
            return;

        Directory.CreateDirectory(Folder);
        var now = timestampUtc ?? DateTime.UtcNow;
        var file = Path.Combine(Folder, $"{Prefix}{now.ToString(TimeFormat, CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..4]}.json");
        FileUtil.WriteAtomic(file, json, FileUtil.Utf8NoBom);

        foreach (var old in List().Skip(MaxEntries))
            File.Delete(old.FilePath);
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(Folder))
            return [];
        var entries = new List<HistoryEntry>();
        foreach (var file in Directory.EnumerateFiles(Folder, Prefix + "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length >= Prefix.Length + TimeFormat.Length &&
                DateTime.TryParseExact(name.AsSpan(Prefix.Length, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
                entries.Add(new HistoryEntry(file, time));
        }
        return entries.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.FilePath, StringComparer.Ordinal).ToList();
    }

    public CommandCollection Load(HistoryEntry entry)
    {
        var collection = JsonSerializer.Deserialize<CommandCollection>(File.ReadAllText(entry.FilePath), Json.Options) ?? new CommandCollection();
        collection.Commands ??= [];
        return collection;
    }
}

public enum ChangeKind
{
    Added,
    Removed,
    Changed,
    Unchanged,
}

public sealed record CommandChange(ChangeKind Kind, QuickCommand Command);

public static class HistoryDiff
{
    /// <summary>What happened between two states, matched by command id. Unchanged commands are included.</summary>
    public static IReadOnlyList<CommandChange> Compare(CommandCollection? before, CommandCollection after)
    {
        var old = (before?.Commands ?? []).ToDictionary(c => c.Id);
        var changes = new List<CommandChange>();
        foreach (var c in after.Commands)
        {
            if (!old.Remove(c.Id, out var previous))
                changes.Add(new(ChangeKind.Added, c));
            else
                changes.Add(new(AreEqual(previous, c) ? ChangeKind.Unchanged : ChangeKind.Changed, c));
        }
        changes.AddRange(old.Values.Select(c => new CommandChange(ChangeKind.Removed, c)));
        return changes.OrderBy(c => c.Command.Name, StringComparer.Ordinal).ThenBy(c => c.Command.Language).ToList();
    }

    public static bool AreEqual(QuickCommand a, QuickCommand b) =>
        JsonSerializer.Serialize(a, Json.Options) == JsonSerializer.Serialize(b, Json.Options);
}
