namespace QuickCommands.Server.Options;

public sealed class FirebaseOptions
{
    public const string Section = "Firebase";

    /// <summary>ID of the Firebase project whose ID tokens are accepted (issuer and audience).</summary>
    public string ProjectId { get; set; } = "";

    public string Issuer => $"https://securetoken.google.com/{ProjectId}";
}
