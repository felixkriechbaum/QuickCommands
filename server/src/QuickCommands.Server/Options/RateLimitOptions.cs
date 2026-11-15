namespace QuickCommands.Server.Options;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>All requests per user (or IP when anonymous) and minute.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Writing requests (publish, star, report, cloud save, …) per user and minute.</summary>
    public int WritePerMinute { get; set; } = 30;
}
