namespace QuickCommands.Server.Options;

public sealed class ForwardedHeadersSettings
{
    public const string Section = "ForwardedHeaders";

    /// <summary>IP addresses of the reverse proxy, comma separated. Empty = loopback and private networks (nginx on the Docker host).</summary>
    public string KnownProxies { get; set; } = "";
}
