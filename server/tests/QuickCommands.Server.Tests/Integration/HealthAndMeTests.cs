using System.Net;
using System.Net.Http.Headers;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Tests.Infrastructure;

namespace QuickCommands.Server.Tests.Integration;

public sealed class HealthAndMeTests(ServerFixture server)
{
    [Fact]
    public async Task Health_reports_database_ok()
    {
        var health = await (await server.Anonymous().GetAsync("/api/health")).ReadAsync<HealthResponse>();
        Assert.Equal("ok", health.Status);
        Assert.Equal("ok", health.Database);
    }

    [Fact]
    public async Task Categories_are_seeded()
    {
        var categories = await (await server.Anonymous().GetAsync("/api/v1/categories")).ReadAsync<List<CategoryResponse>>();
        Assert.Contains(categories, c => c.Slug == "git" && c.NameDe == "Git");
        Assert.Equal("other", categories[^1].Slug);
    }

    [Fact]
    public async Task Request_id_is_echoed_or_created()
    {
        var client = server.Anonymous();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Request-Id", "my-id-123");
        var echoed = await client.SendAsync(request);
        Assert.Equal("my-id-123", echoed.Headers.GetValues("X-Request-Id").Single());

        var created = await client.GetAsync("/api/health");
        Assert.Equal(32, created.Headers.GetValues("X-Request-Id").Single().Length);
    }

    [Fact]
    public async Task Me_without_token_is_401_problem()
    {
        var response = await server.Anonymous().GetAsync("/api/v1/me");
        await response.ExpectProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    public async Task Invalid_tokens_are_rejected(string variant)
    {
        var uid = ApiHelpers.NewUid();
        var token = variant switch
        {
            "wrong-issuer" => ServerFixture.CreateToken(uid, issuer: "https://securetoken.google.com/other-project"),
            "wrong-audience" => ServerFixture.CreateToken(uid, audience: "other-project"),
            _ => ServerFixture.CreateToken(uid, expires: DateTime.UtcNow.AddMinutes(-6)),
        };
        var client = server.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await (await client.GetAsync("/api/v1/me")).ExpectProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task First_request_creates_the_user_without_handle()
    {
        var me = await (await server.As(ApiHelpers.NewUid()).GetAsync("/api/v1/me")).ReadAsync<MeResponse>();
        Assert.Null(me.Handle);
        Assert.False(me.IsAdmin);
        Assert.False(me.IsBanned);
    }

    [Fact]
    public async Task Admin_flag_comes_from_configuration()
    {
        var me = await (await server.Admin().GetAsync("/api/v1/me")).ReadAsync<MeResponse>();
        Assert.True(me.IsAdmin);
    }

    [Fact]
    public async Task Handle_is_normalized_and_unique()
    {
        var handle = "Felix_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var first = server.As(ApiHelpers.NewUid());
        var me = await (await first.PutJsonAsync("/api/v1/me", new UpdateMeRequest($"  {handle} "))).ReadAsync<MeResponse>();
        Assert.Equal(handle.ToLowerInvariant(), me.Handle);

        // Setting the same handle again is fine, another user gets 409.
        await (await first.PutJsonAsync("/api/v1/me", new UpdateMeRequest(handle))).ExpectAsync(HttpStatusCode.OK);
        var second = server.As(ApiHelpers.NewUid());
        await (await second.PutJsonAsync("/api/v1/me", new UpdateMeRequest(handle.ToLowerInvariant())))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "handle_taken");
    }

    [Fact]
    public async Task Reserved_handle_is_rejected()
    {
        await (await server.As(ApiHelpers.NewUid()).PutJsonAsync("/api/v1/me", new UpdateMeRequest("Admin")))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "handle_taken");
    }

    [Fact]
    public async Task Invalid_handle_is_a_validation_error()
    {
        var problem = await (await server.As(ApiHelpers.NewUid()).PutJsonAsync("/api/v1/me", new UpdateMeRequest("a!")))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("handle", out _));
    }

    [Fact]
    public async Task Malformed_json_is_a_validation_error()
    {
        var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        await (await server.As(ApiHelpers.NewUid()).PutAsync("/api/v1/me", content))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }
}
