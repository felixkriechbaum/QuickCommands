namespace QuickCommands.Server.Middlewares;

/// <summary>Endpoint stays usable for banned users (reading their profile and deleting their data).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowBannedAttribute : Attribute;
