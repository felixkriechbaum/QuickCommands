using System.Text.RegularExpressions;

namespace QuickCommands.Server.Validators;

/// <summary>Normalisation and rules shared by validators and services.</summary>
public static partial class StoreRules
{
    public const int MaxCommandsPerPackage = 20;
    public const int MaxTagsPerPackage = 5;
    public const int MaxScriptBytes = 64 * 1024;
    public const int MaxParametersBytes = 16 * 1024;
    public const int MaxCommandNameLength = 64;

    /// <summary>The desktop app generates a "qc" command itself.</summary>
    public const string ReservedCommandName = "qc";

    private static readonly HashSet<string> ReservedHandles = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "anonymous", "api", "deleted", "mod", "moderator", "null", "qc",
        "quickcommands", "root", "staff", "store", "support", "system",
    };

    // Same rule as CommandNameRules in the desktop app.
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex CommandNamePattern();

    [GeneratedRegex("^[a-z0-9_-]{3,20}$")]
    private static partial Regex HandlePattern();

    [GeneratedRegex("^[a-z0-9-]{2,24}$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex TagSeparators();

    public static string NormalizeCommandName(string? name) => (name ?? "").Trim().ToLowerInvariant();

    public static bool IsValidCommandName(string normalized) =>
        normalized.Length is > 0 and <= MaxCommandNameLength && normalized != ReservedCommandName && CommandNamePattern().IsMatch(normalized);

    public static string NormalizeHandle(string? handle) => (handle ?? "").Trim().ToLowerInvariant();

    public static bool IsValidHandle(string normalized) => HandlePattern().IsMatch(normalized);

    public static bool IsReservedHandle(string normalized) => ReservedHandles.Contains(normalized);

    /// <summary>"Dev Tools" → "dev-tools". The result may still be invalid – check with <see cref="IsValidTag"/>.</summary>
    public static string NormalizeTag(string? tag) =>
        TagSeparators().Replace((tag ?? "").Trim().ToLowerInvariant(), "-").Trim('-');

    public static bool IsValidTag(string normalized) => TagPattern().IsMatch(normalized);

    public static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? []).Select(NormalizeTag).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}
