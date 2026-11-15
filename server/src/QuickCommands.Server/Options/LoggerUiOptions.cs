namespace QuickCommands.Server.Options;

public sealed class LoggerUiOptions
{
    public const string Section = "LoggerUi";

    /// <summary>Base URL of the loggerui, e.g. https://logging.example.com. Empty = console logging only.</summary>
    public string Url { get; set; } = "";

    /// <summary>Upload token of the project (Bearer).</summary>
    public string Token { get; set; } = "";

    public string Project { get; set; } = "quickcommands";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token);
}
