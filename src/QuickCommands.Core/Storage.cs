using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickCommands.Core;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keeps umlauts and & readable in commands.json (local file, never embedded in HTML).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

internal static class FileUtil
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    public static readonly Encoding Utf8Bom = new UTF8Encoding(true);

    /// <summary>Writes via a temp file so a crash never leaves a half-written file behind.</summary>
    public static void WriteAtomic(string path, string content, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, encoding);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Reads a text file and reports the encoding it was stored in (by BOM), so it can be written back unchanged.</summary>
    public static (string Text, Encoding Encoding) ReadPreservingEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return (Utf8NoBom.GetString(bytes, 3, bytes.Length - 3), Utf8Bom);
        if (bytes is [0xFF, 0xFE, ..])
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes is [0xFE, 0xFF, ..])
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
        return (Utf8NoBom.GetString(bytes), Utf8NoBom);
    }
}

public sealed class CommandRepository(string dataDirectory)
{
    public const string FileName = "commands.json";

    public string DataDirectory { get; } = dataDirectory;
    public string FilePath => Path.Combine(DataDirectory, FileName);

    public CommandCollection Load()
    {
        if (!File.Exists(FilePath))
            return new CommandCollection();
        var json = File.ReadAllText(FilePath);
        var collection = JsonSerializer.Deserialize<CommandCollection>(json, Json.Options) ?? new CommandCollection();
        collection.Commands ??= [];
        return collection;
    }

    public HistoryStore History => new(DataDirectory);

    /// <summary>Writes commands.json and records a history snapshot (the state before the first recorded save, too).</summary>
    public void Save(CommandCollection collection)
    {
        var history = History;
        string? previous = null;
        var previousTime = DateTime.MinValue;
        try
        {
            if (File.Exists(FilePath) && history.List().Count == 0)
            {
                previous = File.ReadAllText(FilePath);
                previousTime = File.GetLastWriteTimeUtc(FilePath);
            }
        }
        catch (IOException)
        {
        }

        var json = JsonSerializer.Serialize(collection, Json.Options);
        FileUtil.WriteAtomic(FilePath, json, FileUtil.Utf8NoBom);

        try
        {
            if (previous is not null)
                history.Record(previous, previousTime < DateTime.UtcNow ? previousTime : DateTime.UtcNow.AddSeconds(-1));
            history.Record(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The history is a safety net – a failing snapshot must not fail the save itself.
        }
    }
}

public static class SettingsStore
{
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file must not prevent the app from starting; setup runs again.
        }
        return new AppSettings();
    }

    public static void Save(string path, AppSettings settings) =>
        FileUtil.WriteAtomic(path, JsonSerializer.Serialize(settings, Json.Options), FileUtil.Utf8NoBom);
}
