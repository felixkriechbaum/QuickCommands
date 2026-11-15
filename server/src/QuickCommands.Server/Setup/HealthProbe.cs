namespace QuickCommands.Server.Setup;

/// <summary>
/// "dotnet QuickCommands.Server.dll --healthcheck": the chiseled image has no shell and no curl, so the Docker
/// HEALTHCHECK runs the app itself, which calls /api/health and exits with 0 (healthy) or 1.
/// </summary>
internal static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([';', ','])[0].Trim();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync($"http://localhost:{port}/api/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
