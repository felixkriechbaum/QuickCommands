using System.Text;

namespace QuickCommands.Core;

/// <summary>The one marked block the tool owns inside a shell profile file.</summary>
public static class ProfileBlock
{
    public const string Begin = "# >>> QuickCommands >>>";
    public const string End = "# <<< QuickCommands <<<";

    public static bool Contains(string content) => FindRange(content) is not null;

    /// <summary>Replaces an existing block or appends a new one (separated by a blank line). Keeps the file's line endings.</summary>
    public static string Upsert(string content, string body, string defaultNewline)
    {
        var nl = DetectNewline(content) ?? defaultNewline;
        var block = Begin + nl + body.ReplaceLineEndings(nl) + nl + End;

        if (FindRange(content) is var (start, length))
            return content[..start] + block + content[(start + length)..];

        if (content.Length == 0)
            return block + nl;
        var separator = content.EndsWith('\n') ? nl : nl + nl;
        return content + separator + block + nl;
    }

    /// <summary>Removes the block, its line break and the blank line Upsert put in front of it.</summary>
    public static string Remove(string content)
    {
        if (FindRange(content) is not var (start, length))
            return content;

        var end = start + length;
        if (end < content.Length && content[end] == '\r') end++;
        if (end < content.Length && content[end] == '\n') end++;

        // Drop the blank separator line in front of the block.
        if (start >= 2 && content[start - 1] == '\n')
        {
            var prev = content[start - 2] == '\r' ? start - 3 : start - 2;
            if (prev >= 0 && content[prev] == '\n')
                start = content[start - 2] == '\r' ? start - 2 : start - 1;
        }
        return content[..start] + content[end..];
    }

    private static (int Start, int Length)? FindRange(string content)
    {
        var start = content.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0)
            return null;
        var end = content.IndexOf(End, start, StringComparison.Ordinal);
        return end < 0 ? null : (start, end + End.Length - start);
    }

    private static string? DetectNewline(string content)
    {
        var i = content.IndexOf('\n');
        if (i < 0)
            return null;
        return i > 0 && content[i - 1] == '\r' ? "\r\n" : "\n";
    }
}

/// <summary>Installs/removes the hook that makes a shell load the generated commands.</summary>
public sealed class IntegrationService(QcPaths paths, IPathEnvironment pathEnvironment)
{
    public const string BackupSuffix = ".qc-backup";

    public QcPaths Paths => paths;

    public bool IsInstalled(ShellTarget target)
    {
        if (target == ShellTarget.Cmd)
            return pathEnvironment.SupportsUserPathEditing && pathEnvironment.UserPathContains(paths.BinDirectory);

        var profile = paths.ProfilePath(target);
        return File.Exists(profile) && ProfileBlock.Contains(FileUtil.ReadPreservingEncoding(profile).Text);
    }

    /// <summary>The file (or PATH) that installing the target changes – shown to the user before installing.</summary>
    public string ChangeLocation(ShellTarget target) =>
        target == ShellTarget.Cmd ? paths.BinDirectory : paths.ProfilePath(target);

    public void Install(ShellTarget target)
    {
        if (target == ShellTarget.Cmd)
        {
            Directory.CreateDirectory(paths.BinDirectory);
            pathEnvironment.AddToUserPath(paths.BinDirectory);
            return;
        }

        var profile = paths.ProfilePath(target);
        var isPowerShell = target.Language() == ShellLanguage.PowerShell;
        string content;
        Encoding encoding;
        if (File.Exists(profile))
        {
            (content, encoding) = FileUtil.ReadPreservingEncoding(profile);
            var backup = profile + BackupSuffix;
            if (!File.Exists(backup))
                File.Copy(profile, backup);
        }
        else
        {
            content = "";
            encoding = isPowerShell ? FileUtil.Utf8Bom : FileUtil.Utf8NoBom;
        }

        // sh profiles always use LF: a CRLF .bashrc breaks Git Bash.
        var newline = isPowerShell && OperatingSystem.IsWindows() ? "\r\n" : "\n";
        var updated = ProfileBlock.Upsert(content, LoaderLine(target), newline);
        if (updated != content)
            FileUtil.WriteAtomic(profile, updated, encoding);
    }

    public void Uninstall(ShellTarget target)
    {
        if (target == ShellTarget.Cmd)
        {
            if (pathEnvironment.SupportsUserPathEditing)
                pathEnvironment.RemoveFromUserPath(paths.BinDirectory);
            return;
        }

        var profile = paths.ProfilePath(target);
        if (!File.Exists(profile))
            return;
        var (content, encoding) = FileUtil.ReadPreservingEncoding(profile);
        var updated = ProfileBlock.Remove(content);
        if (updated != content)
            FileUtil.WriteAtomic(profile, updated, encoding);
    }

    public string LoaderLine(ShellTarget target) => target.Language() switch
    {
        ShellLanguage.PowerShell =>
            $"if (Test-Path -LiteralPath {PowerShellGenerator.Quote(paths.PowerShellScript)}) {{ . {PowerShellGenerator.Quote(paths.PowerShellScript)} }}",
        ShellLanguage.Sh => $"[ -f {paths.ShScriptReference} ] && . {paths.ShScriptReference}",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
