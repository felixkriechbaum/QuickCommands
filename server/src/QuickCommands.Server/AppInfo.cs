using System.Reflection;

namespace QuickCommands.Server;

public static class AppInfo
{
    /// <summary>YYYY.M.build from CI (-p:Version=…), "0.0.0" for local builds.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>True while "dotnet build" generates openapi/v1.json – the app is started without serving requests.</summary>
    public static bool IsGeneratingOpenApiDocument { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
