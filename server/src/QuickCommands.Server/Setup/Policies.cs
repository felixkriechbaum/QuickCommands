namespace QuickCommands.Server.Setup;

public static class AuthPolicies
{
    public const string Admin = "admin";
}

public static class RateLimitPolicies
{
    /// <summary>Writing endpoints: publish, edit, delete, star, install, report, cloud save.</summary>
    public const string Write = "write";
}
