using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Setup;

namespace QuickCommands.Server.Tests.Infrastructure;

public static class ApiHelpers
{
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ServiceRegistration.ConfigureJson(options);
        return options;
    }

    public static string NewUid() => "uid-" + Guid.NewGuid().ToString("N");

    public static string NewTag() => "t" + Guid.NewGuid().ToString("N")[..12];

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static async Task ExpectAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
    }

    /// <summary>Asserts a ProblemDetails response with the given status and machine-readable code.</summary>
    public static async Task<JsonElement> ExpectProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _), "ProblemDetails without traceId");
        return problem;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);

    /// <summary>A signed-in user with a handle – ready to publish.</summary>
    public static async Task<(HttpClient Client, string Uid, MeResponse Me)> CreatePublisherAsync(this ServerFixture server)
    {
        var uid = NewUid();
        var client = server.As(uid);
        var me = await (await client.PutJsonAsync("/api/v1/me", new UpdateMeRequest("h" + Guid.NewGuid().ToString("N")[..12])))
            .ReadAsync<MeResponse>();
        return (client, uid, me);
    }

    public static PackageRequest SamplePackage(string? tag = null, string title = "Git helpers", params PackageCommandRequest[] commands) => new(
        title,
        "Shortcuts for everyday git work",
        "git",
        tag is null ? ["git"] : ["git", tag],
        commands.Length > 0
            ? commands
            :
            [
                new PackageCommandRequest("gs", ShellLanguage.Sh, "git status", "git status \"$@\"", null),
                new PackageCommandRequest("gs", ShellLanguage.PowerShell, "git status", "git status @args",
                    JsonDocument.Parse("""[{"name":"short","kind":"switch"}]""").RootElement),
            ]);

    public static async Task<PackageDetailResponse> PublishAsync(this HttpClient client, PackageRequest request) =>
        await (await client.PostJsonAsync("/api/v1/store/packages", request)).ReadAsync<PackageDetailResponse>(HttpStatusCode.Created);

    public static async Task<PackageVersionResponse?> VersionOfAsync(this ServerFixture server, Guid id)
    {
        var versions = await (await server.Anonymous().GetAsync($"/api/v1/store/packages/versions?ids={id}"))
            .ReadAsync<List<PackageVersionResponse>>();
        return versions.SingleOrDefault();
    }
}
