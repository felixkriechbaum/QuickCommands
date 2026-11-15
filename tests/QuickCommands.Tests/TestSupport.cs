using QuickCommands.Core;

namespace QuickCommands.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qc-tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public QcPaths CreatePaths() => new(
        System.IO.Path.Combine(Path, "home"),
        System.IO.Path.Combine(Path, "docs"),
        System.IO.Path.Combine(Path, "config"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class FakePathEnvironment : IPathEnvironment
{
    public List<string> UserPath { get; } = [];
    public List<PathDirectory> Search { get; } = [];

    public bool SupportsUserPathEditing => true;
    public IReadOnlyList<PathDirectory> GetSearchDirectories() => Search;
    public bool UserPathContains(string directory) => UserPath.Any(p => PathUtil.AreEqual(p, directory));

    public void AddToUserPath(string directory)
    {
        if (!UserPathContains(directory))
            UserPath.Insert(0, directory);
    }

    public void RemoveFromUserPath(string directory) => UserPath.RemoveAll(p => PathUtil.AreEqual(p, directory));
}

internal static class Make
{
    public static QuickCommand Command(string name, ShellLanguage language, string script, string description = "") =>
        new() { Name = name, Language = language, Script = script, Description = description };
}
