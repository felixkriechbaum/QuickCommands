using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickCommands.Core;

public sealed record SyntaxError(int Line, int Column, string Message);

/// <summary>Result of a syntax check. <see cref="HasParamBlock"/> is only known for PowerShell.</summary>
public sealed record SyntaxCheckResult(IReadOnlyList<SyntaxError> Errors, bool HasParamBlock = false)
{
    public static SyntaxCheckResult Ok { get; } = new([]);
}

/// <summary>
/// Checks scripts without running them: PowerShell via its own parser (in a long-running pwsh process),
/// sh via `bash -n` / `zsh -n`. cmd has no parser that could be asked, so it is not checked.
/// </summary>
public sealed partial class SyntaxChecker(bool preferZsh = false) : IDisposable
{
    private readonly PowerShellWorker _powerShell = new();

    public PowerShellWorker PowerShell => _powerShell;

    /// <returns>null if the language cannot be checked on this computer.</returns>
    public async Task<SyntaxCheckResult?> CheckAsync(ShellLanguage language, string script, CancellationToken ct = default)
    {
        switch (language)
        {
            case ShellLanguage.PowerShell:
                return await _powerShell.ParseAsync(script, ct);
            case ShellLanguage.Sh:
                return await CheckShAsync(script, ct);
            default:
                return null;
        }
    }

    private async Task<SyntaxCheckResult?> CheckShAsync(string script, CancellationToken ct)
    {
        var shell = FindSh();
        if (shell is null)
            return null;
        try
        {
            var run = await ProcessRunner.RunAsync(shell, ["-n"], TimeSpan.FromSeconds(10), ct, stdin: script.ReplaceLineEndings("\n"));
            if (run.ExitCode == 0)
                return SyntaxCheckResult.Ok;
            var errors = new List<SyntaxError>();
            foreach (var line in run.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // bash: "bash: line 3: syntax error near unexpected token `}'"   zsh: "zsh: parse error near `}'" / "zsh:3: …"
                var m = ShError().Match(line);
                if (m.Success)
                    errors.Add(new SyntaxError(int.Parse(m.Groups["line"].Value), 1, m.Groups["msg"].Value.Trim()));
                else if (errors.Count == 0)
                    errors.Add(new SyntaxError(1, 1, StripShellPrefix(line)));
            }
            return new SyntaxCheckResult(errors.Count > 0 ? errors : [new SyntaxError(1, 1, "syntax error")]);
        }
        catch (Exception e) when (e is Win32Exception or TimeoutException)
        {
            return null;
        }
    }

    private string? FindSh()
    {
        if (OperatingSystem.IsWindows())
            return ShellDetector.FindGitBash();
        var zsh = ShellDetector.FindExecutable("zsh");
        var bash = ShellDetector.FindExecutable("bash");
        return preferZsh ? zsh ?? bash : bash ?? zsh;
    }

    private static string StripShellPrefix(string line)
    {
        var i = line.IndexOf(": ", StringComparison.Ordinal);
        return i > 0 && i < 12 ? line[(i + 2)..] : line;
    }

    [GeneratedRegex(@"(?:line |:)(?<line>\d+):\s*(?<msg>.*)$")]
    private static partial Regex ShError();

    public void Dispose() => _powerShell.Dispose();
}

/// <summary>
/// A pwsh (or Windows PowerShell) process that stays alive and answers parse requests – starting PowerShell
/// for every keystroke would take far too long. One request at a time; one JSON line per answer.
/// </summary>
public sealed class PowerShellWorker : IDisposable
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        while ($true) {
            $line = [Console]::In.ReadLine()
            if ($null -eq $line) { break }
            $parts = $line.Split(':', 2)
            $code = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[1]))
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
            if ($parts[0] -eq 'parse') {
                $result = @{
                    errors = @($errors | ForEach-Object { @{ line = $_.Extent.StartLineNumber; column = $_.Extent.StartColumnNumber; message = $_.Message } })
                    paramBlock = $null -ne $ast.ParamBlock
                }
            } else {
                $items = [System.Collections.Generic.List[object]]::new()
                foreach ($s in $ast.EndBlock.Statements) {
                    if ($s -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                        $body = $s.Body.Extent.Text
                        $body = $body.Substring(1, $body.Length - 2)
                        if ($s.Parameters) { $body = 'param(' + (($s.Parameters | ForEach-Object { $_.Extent.Text }) -join ', ') + ")`n" + $body.Trim("`r", "`n") }
                        $items.Add(@{ kind = 'function'; name = $s.Name; script = $body; startLine = $s.Extent.StartLineNumber; endLine = $s.Extent.EndLineNumber; startColumn = $s.Extent.StartColumnNumber; endColumn = $s.Extent.EndColumnNumber })
                    } elseif ($s -is [System.Management.Automation.Language.PipelineAst] -and $s.PipelineElements.Count -eq 1 -and $s.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst]) {
                        $c = $s.PipelineElements[0]
                        if ($c.GetCommandName() -in 'Set-Alias', 'New-Alias', 'sal', 'nal') {
                            $bound = [System.Management.Automation.Language.StaticParameterBinder]::BindCommand($c, $true)
                            $n = $bound.BoundParameters['Name']; $v = $bound.BoundParameters['Value']
                            if ($n -and $v -and $n.ConstantValue -and $v.ConstantValue) {
                                $items.Add(@{ kind = 'alias'; name = [string]$n.ConstantValue; script = [string]$v.ConstantValue + ' @args'; startLine = $s.Extent.StartLineNumber; endLine = $s.Extent.EndLineNumber; startColumn = $s.Extent.StartColumnNumber; endColumn = $s.Extent.EndColumnNumber })
                            }
                        }
                    }
                }
                $result = @{ items = $items.ToArray() }
            }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Compress -Depth 5))
            [Console]::Out.Flush()
        }
        """;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _process;
    private bool _unavailable;

    public async Task<SyntaxCheckResult?> ParseAsync(string script, CancellationToken ct = default)
    {
        using var doc = await RequestAsync("parse", script, ct);
        if (doc is null)
            return null;
        var root = doc.RootElement;
        var errors = root.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => new SyntaxError(
                e.GetProperty("line").GetInt32(), e.GetProperty("column").GetInt32(), e.GetProperty("message").GetString() ?? "")).ToList()
            : [];
        return new SyntaxCheckResult(errors, root.TryGetProperty("paramBlock", out var p) && p.ValueKind == JsonValueKind.True);
    }

    /// <summary>Top-level functions and Set-Alias calls of a PowerShell file (for the profile import).</summary>
    public async Task<IReadOnlyList<PowerShellDefinition>?> FindDefinitionsAsync(string script, CancellationToken ct = default)
    {
        using var doc = await RequestAsync("definitions", script, ct);
        if (doc is null)
            return null;
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];
        return items.EnumerateArray().Select(e => new PowerShellDefinition(
            e.GetProperty("kind").GetString() == "alias",
            e.GetProperty("name").GetString() ?? "",
            e.GetProperty("script").GetString() ?? "",
            e.GetProperty("startLine").GetInt32(),
            e.GetProperty("endLine").GetInt32(),
            e.GetProperty("startColumn").GetInt32(),
            e.GetProperty("endColumn").GetInt32())).ToList();
    }

    private async Task<JsonDocument?> RequestAsync(string op, string script, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var process = EnsureStarted();
                if (process is null)
                    return null;
                try
                {
                    await process.StandardInput.WriteLineAsync(op + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(script)));
                    await process.StandardInput.FlushAsync(ct);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                    if (line is not null)
                        return JsonDocument.Parse(line);
                }
                catch (Exception e) when (e is IOException or JsonException or InvalidOperationException
                                              || (e is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    // The worker died or hangs – restart it once.
                }
                Kill();
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private Process? EnsureStarted()
    {
        if (_process is { HasExited: false })
            return _process;
        if (_unavailable)
            return null;

        string[] candidates = OperatingSystem.IsWindows() ? ["pwsh", "powershell"] : ["pwsh"];
        foreach (var exe in candidates)
        {
            var path = ShellDetector.FindExecutable(exe);
            if (path is null)
                continue;
            try
            {
                var psi = new ProcessStartInfo(path)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (var arg in new[] { "-NoProfile", "-NoLogo", "-NonInteractive", "-Command", Script })
                    psi.ArgumentList.Add(arg);
                var process = Process.Start(psi);
                if (process is null)
                    continue;
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                _process = process;
                return process;
            }
            catch (Win32Exception)
            {
                // Try the next candidate.
            }
        }
        _unavailable = true;
        return null;
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        try
        {
            _process?.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        Kill();
    }
}

public sealed record PowerShellDefinition(bool IsAlias, string Name, string Script, int StartLine, int EndLine, int StartColumn, int EndColumn);
