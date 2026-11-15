using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace QuickCommands.Core;

public sealed record ExecutableInfo(string Path, bool InSystemPath);

/// <summary>Snapshot of the commands that already exist on this machine, used for conflict checks.</summary>
public sealed class CommandInventory(
    IReadOnlyDictionary<string, ExecutableInfo> executables,
    IReadOnlyDictionary<string, string> powerShellCommands)
{
    public static readonly CommandInventory Empty = new(
        new Dictionary<string, ExecutableInfo>(), new Dictionary<string, string>());

    public int ExecutableCount => executables.Count;
    public int PowerShellCommandCount => powerShellCommands.Count;

    public ExecutableInfo? FindExecutable(string name) => executables.GetValueOrDefault(name);

    /// <returns>The PowerShell command type (Alias, Function, Cmdlet, …) or null.</returns>
    public string? FindPowerShellCommand(string name) => powerShellCommands.GetValueOrDefault(name);
}

public static class InventoryLoader
{
    private const string PowerShellQuery =
        "Get-Command -CommandType Alias,Function,Filter,Cmdlet -ErrorAction SilentlyContinue | " +
        "ForEach-Object { '{0}|{1}' -f $_.Name, $_.CommandType }";

    public static async Task<CommandInventory> LoadAsync(IPathEnvironment environment, string ownBinDirectory, CancellationToken ct = default)
    {
        var executablesTask = Task.Run(() => ScanExecutables(environment.GetSearchDirectories(), ownBinDirectory), ct);
        var powerShellTask = LoadPowerShellCommandsAsync(ct);
        return new CommandInventory(await executablesTask, await powerShellTask);
    }

    private static StringComparer NameComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Maps program name → first match in lookup order, the way the shell would resolve it.</summary>
    internal static Dictionary<string, ExecutableInfo> ScanExecutables(IEnumerable<PathDirectory> directories, string ownBinDirectory)
    {
        var result = new Dictionary<string, ExecutableInfo>(NameComparer);
        var extensions = OperatingSystem.IsWindows()
            ? new HashSet<string>(
                (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var dir in directories)
        {
            if (PathUtil.AreEqual(dir.Path, ownBinDirectory) || !Directory.Exists(dir.Path))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir.Path).ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                string name;
                if (extensions is not null)
                {
                    if (!extensions.Contains(Path.GetExtension(file)))
                        continue;
                    name = Path.GetFileNameWithoutExtension(file);
                }
                else
                {
                    if (!IsUnixExecutable(file))
                        continue;
                    name = Path.GetFileName(file);
                }
                result.TryAdd(name, new ExecutableInfo(file, dir.IsSystem));
            }
        }
        return result;
    }

    private static bool IsUnixExecutable(string file)
    {
        if (OperatingSystem.IsWindows())
            return false;
        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(file) & anyExecute) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadPowerShellCommandsAsync(CancellationToken ct)
    {
        string[] candidates = OperatingSystem.IsWindows() ? ["pwsh", "powershell"] : ["pwsh"];
        foreach (var exe in candidates)
        {
            try
            {
                var run = await ProcessRunner.RunAsync(exe, ["-NoProfile", "-NonInteractive", "-Command", PowerShellQuery],
                    TimeSpan.FromSeconds(45), ct);
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in run.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var sep = line.LastIndexOf('|');
                    if (sep > 0)
                        map.TryAdd(line[..sep], line[(sep + 1)..]);
                }
                return map;
            }
            catch (Exception e) when (e is Win32Exception or TimeoutException)
            {
                // Not installed or hanging: try the next one / no PowerShell inventory.
            }
        }
        return new Dictionary<string, string>();
    }
}

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = Process.Start(psi) ?? throw new Win32Exception($"Could not start {fileName}");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within {timeout.TotalSeconds:0}s.");
        }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
