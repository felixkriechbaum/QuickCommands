using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QuickCommands.Core;

public sealed record PathDirectory(string Path, bool IsSystem);

/// <summary>Reads the program search path and edits the persistent user PATH (Windows only).</summary>
public interface IPathEnvironment
{
    bool SupportsUserPathEditing { get; }

    /// <summary>Directories in lookup order: system PATH first, then user PATH.</summary>
    IReadOnlyList<PathDirectory> GetSearchDirectories();

    bool UserPathContains(string directory);

    /// <summary>Prepends the directory, so commands in it win over everything else in the user PATH.</summary>
    void AddToUserPath(string directory);

    void RemoveFromUserPath(string directory);
}

public static class PathEnvironment
{
    public static IPathEnvironment CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsPathEnvironment() : new ProcessPathEnvironment();

    internal static IEnumerable<string> Split(string raw) =>
        raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Unix: only the process PATH is read; editing is not needed (cmd does not exist there).</summary>
public sealed class ProcessPathEnvironment : IPathEnvironment
{
    public bool SupportsUserPathEditing => false;

    public IReadOnlyList<PathDirectory> GetSearchDirectories() =>
        PathEnvironment.Split(Environment.GetEnvironmentVariable("PATH") ?? "")
            .Select(d => new PathDirectory(d, IsSystem: false))
            .ToList();

    public bool UserPathContains(string directory) => false;
    public void AddToUserPath(string directory) => throw new PlatformNotSupportedException();
    public void RemoveFromUserPath(string directory) => throw new PlatformNotSupportedException();
}

/// <summary>
/// Edits HKCU\Environment\Path directly instead of Environment.SetEnvironmentVariable, because the latter
/// would rewrite the value as REG_SZ with all %VARIABLES% expanded.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPathEnvironment : IPathEnvironment
{
    private const string UserKey = "Environment";
    private const string SystemKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string ValueName = "Path";

    public bool SupportsUserPathEditing => true;

    public IReadOnlyList<PathDirectory> GetSearchDirectories()
    {
        var system = PathEnvironment.Split(ReadRaw(Registry.LocalMachine, SystemKey))
            .Select(d => new PathDirectory(Environment.ExpandEnvironmentVariables(d), IsSystem: true));
        var user = PathEnvironment.Split(ReadRaw(Registry.CurrentUser, UserKey))
            .Select(d => new PathDirectory(Environment.ExpandEnvironmentVariables(d), IsSystem: false));
        return system.Concat(user).ToList();
    }

    public bool UserPathContains(string directory) =>
        PathEnvironment.Split(ReadRaw(Registry.CurrentUser, UserKey)).Any(e => PathUtil.AreEqual(e, directory));

    public void AddToUserPath(string directory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UserKey, writable: true);
        var raw = key.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        if (PathEnvironment.Split(raw).Any(e => PathUtil.AreEqual(e, directory)))
            return;

        var kind = ValueKind(key);
        var updated = raw.Length == 0 ? directory : directory + ";" + raw;
        key.SetValue(ValueName, updated, kind);
        BroadcastEnvironmentChange();
    }

    public void RemoveFromUserPath(string directory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UserKey, writable: true);
        var raw = key.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = raw.Split(';');
        var kept = parts.Where(e => e.Trim().Length == 0 || !PathUtil.AreEqual(e, directory)).ToArray();
        if (kept.Length == parts.Length)
            return;

        key.SetValue(ValueName, string.Join(';', kept), ValueKind(key));
        BroadcastEnvironmentChange();
    }

    private static RegistryValueKind ValueKind(RegistryKey key) =>
        key.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) && key.GetValueKind(ValueName) == RegistryValueKind.String
            ? RegistryValueKind.String
            : RegistryValueKind.ExpandString;

    private static string ReadRaw(RegistryKey root, string subKey)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
    }

    /// <summary>Tells Explorer (and with it newly started terminals) that the environment changed.</summary>
    private static void BroadcastEnvironmentChange()
    {
        const int HwndBroadcast = 0xFFFF;
        const uint WmSettingChange = 0x001A;
        const uint SmtoAbortIfHung = 0x0002;
        SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
