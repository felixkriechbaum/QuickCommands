using System.Text;
using System.Text.RegularExpressions;

namespace QuickCommands.Core;

/// <summary>A function or alias found in a shell profile that can become a quickcommand.</summary>
public sealed record ImportCandidate(
    string SourceFile,
    bool IsAlias,
    string OriginalName,
    ShellLanguage Language,
    string Script,
    int StartLine,
    int EndLine,
    bool CanCommentOut)
{
    public string Name => CommandNameRules.Normalize(OriginalName);
}

/// <summary>
/// Finds functions and aliases in the user's shell profiles and comments out the originals after import
/// (otherwise the profile definition, loaded later, would win over the quickcommand).
/// </summary>
public sealed partial class ProfileImporter(QcPaths paths)
{
    public const string CommentPrefix = "# [moved to QuickCommands] ";

    /// <summary>Profile functions that belong to the shell itself and must stay where they are.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "tabexpansion", "tabexpansion2", "precmd", "preexec", "chpwd", "command_not_found_handle",
        "command_not_found_handler", "qc",
    };

    public IEnumerable<string> ProfileFiles(ShellLanguage language)
    {
        IEnumerable<string> files = language switch
        {
            ShellLanguage.PowerShell => new[]
            {
                paths.ProfilePath(ShellTarget.Pwsh),
                Path.Combine(Path.GetDirectoryName(paths.ProfilePath(ShellTarget.Pwsh))!, "profile.ps1"),
                OperatingSystem.IsWindows() ? paths.ProfilePath(ShellTarget.WindowsPowerShell) : "",
            },
            ShellLanguage.Sh => new[]
            {
                Path.Combine(paths.Home, ".bashrc"),
                Path.Combine(paths.Home, ".bash_aliases"),
                Path.Combine(paths.Home, ".bash_profile"),
                Path.Combine(paths.Home, ".zshrc"),
                Path.Combine(paths.Home, ".aliases"),
            },
            _ => [],
        };
        return files.Where(f => f.Length > 0 && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ImportCandidate>> ScanAsync(PowerShellWorker? powerShell, CancellationToken ct = default)
    {
        var result = new List<ImportCandidate>();
        if (powerShell is not null)
        {
            foreach (var file in ProfileFiles(ShellLanguage.PowerShell))
            {
                var text = FileUtil.ReadPreservingEncoding(file).Text;
                var definitions = await powerShell.FindDefinitionsAsync(text, ct);
                if (definitions is null)
                    break;
                var lines = SplitLines(text);
                var block = BlockLines(lines);
                foreach (var d in definitions)
                {
                    if (Ignored.Contains(d.Name) || Overlaps(block, d.StartLine, d.EndLine))
                        continue;
                    var canComment = IsWholeLines(lines, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn);
                    result.Add(new(file, d.IsAlias, d.Name, ShellLanguage.PowerShell, d.Script.Trim('\r', '\n'), d.StartLine, d.EndLine, canComment));
                }
            }
        }
        foreach (var file in ProfileFiles(ShellLanguage.Sh))
            result.AddRange(ScanSh(file, FileUtil.ReadPreservingEncoding(file).Text));
        return result;
    }

    /// <summary>Heuristic parser for `alias x='…'`, `x() { … }` and `function x { … }` – good enough for profiles.</summary>
    public static IReadOnlyList<ImportCandidate> ScanSh(string file, string text)
    {
        var result = new List<ImportCandidate>();
        var lines = SplitLines(text);
        var block = BlockLines(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineNo = i + 1;
            if (block.Contains(lineNo))
                continue;
            var line = lines[i];

            var alias = ShAlias().Match(line);
            if (alias.Success)
            {
                foreach (Match m in ShAliasPair().Matches(alias.Groups["defs"].Value))
                {
                    var value = m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["dq"].Success ? m.Groups["dq"].Value : m.Groups["bare"].Value;
                    if (!Ignored.Contains(m.Groups["name"].Value))
                        result.Add(new(file, true, m.Groups["name"].Value, ShellLanguage.Sh, value + " \"$@\"", lineNo, lineNo, true));
                }
                continue;
            }

            var fn = ShFunction().Match(line);
            if (!fn.Success || Ignored.Contains(fn.Groups["name"].Value))
                continue;

            var rest = fn.Groups["rest"].Value;
            // One-liner: name() { cmd; }
            if (rest.TrimEnd().EndsWith('}') && Balance(rest) < 0)
            {
                var inner = rest.Trim();
                inner = inner[..^1].Trim().TrimEnd(';').Trim();
                result.Add(new(file, false, fn.Groups["name"].Value, ShellLanguage.Sh, inner, lineNo, lineNo, true));
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var depth = 1 + Balance(rest);
            var body = new StringBuilder();
            if (rest.Trim().Length > 0)
                body.Append(rest.Trim()).Append('\n');
            var end = -1;
            for (var j = i + 1; j < lines.Count; j++)
            {
                depth += Balance(lines[j]);
                var trimmed = lines[j].Trim();
                if (depth <= 0 || (trimmed == "}" && lines[j].Length - lines[j].TrimStart().Length == indent))
                {
                    end = j;
                    break;
                }
                body.Append(lines[j]).Append('\n');
            }
            if (end < 0)
                continue;
            result.Add(new(file, false, fn.Groups["name"].Value, ShellLanguage.Sh, Dedent(body.ToString()).TrimEnd('\n'), lineNo, end + 1, true));
            i = end;
        }
        return result;
    }

    /// <summary>Comments out the given definitions (whole lines only). Each touched profile is backed up first.</summary>
    /// <returns>The backup files that were written.</returns>
    public IReadOnlyList<string> CommentOut(IEnumerable<ImportCandidate> imported)
    {
        var backups = new List<string>();
        foreach (var group in imported.Where(c => c.CanCommentOut).GroupBy(c => c.SourceFile, StringComparer.OrdinalIgnoreCase))
        {
            var (text, encoding) = FileUtil.ReadPreservingEncoding(group.Key);
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = SplitLines(text);
            var touched = new HashSet<int>();
            foreach (var c in group)
            {
                for (var n = c.StartLine; n <= c.EndLine && n <= lines.Count; n++)
                {
                    if (touched.Add(n))
                        lines[n - 1] = CommentPrefix + lines[n - 1];
                }
            }

            var backup = group.Key + ".qc-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            File.Copy(group.Key, backup, overwrite: true);
            backups.Add(backup);
            var updated = string.Join(newline, lines);
            if (text.EndsWith('\n'))
                updated += newline;
            FileUtil.WriteAtomic(group.Key, updated, encoding);
        }
        return backups;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>1-based line numbers of the QuickCommands block – never imported or touched.</summary>
    private static HashSet<int> BlockLines(List<string> lines)
    {
        var result = new HashSet<int>();
        var inside = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == ProfileBlock.Begin)
                inside = true;
            if (inside)
                result.Add(i + 1);
            if (lines[i].Trim() == ProfileBlock.End)
                inside = false;
        }
        return result;
    }

    private static bool Overlaps(HashSet<int> block, int start, int end) => Enumerable.Range(start, end - start + 1).Any(block.Contains);

    /// <summary>True if nothing else shares the first or last line with the definition.</summary>
    private static bool IsWholeLines(List<string> lines, int startLine, int startColumn, int endLine, int endColumn)
    {
        if (startLine < 1 || endLine > lines.Count)
            return false;
        var before = lines[startLine - 1][..Math.Min(startColumn - 1, lines[startLine - 1].Length)];
        var lastLine = lines[endLine - 1];
        var after = endColumn - 1 < lastLine.Length ? lastLine[(endColumn - 1)..] : "";
        var afterTrimmed = after.Trim();
        return before.Trim().Length == 0 && (afterTrimmed.Length == 0 || afterTrimmed.StartsWith('#'));
    }

    /// <summary>{ minus } outside of quotes and comments.</summary>
    private static int Balance(string line)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"')
                    i++;
                else if (ch == quote)
                    quote = '\0';
                continue;
            }
            switch (ch)
            {
                case '\\':
                    i++;
                    break;
                case '\'' or '"':
                    quote = ch;
                    break;
                case '#' when i == 0 || char.IsWhiteSpace(line[i - 1]):
                    return depth;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
            }
        }
        return depth;
    }

    private static string Dedent(string text)
    {
        var lines = text.Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    [GeneratedRegex(@"^\s*alias\s+(?<defs>[^#].*)$")]
    private static partial Regex ShAlias();

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_.-]+)=(?:'(?<sq>[^']*)'|""(?<dq>(?:[^""\\]|\\.)*)""|(?<bare>[^\s'""]+))")]
    private static partial Regex ShAliasPair();

    [GeneratedRegex(@"^\s*(?:function\s+(?<name>[A-Za-z0-9_.-]+)\s*(?:\(\s*\))?|(?<name>[A-Za-z0-9_.-]+)\s*\(\s*\))\s*\{(?<rest>.*)$")]
    private static partial Regex ShFunction();
}
