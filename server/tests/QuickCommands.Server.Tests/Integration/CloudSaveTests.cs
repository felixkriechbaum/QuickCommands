using System.Net;
using System.Text;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Tests.Infrastructure;

namespace QuickCommands.Server.Tests.Integration;

public sealed class CloudSaveTests(ServerFixture server)
{
    private static HttpRequestMessage Put(string json, string? ifMatch = null, bool create = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cloud-save")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (create)
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return request;
    }

    private static string Doc(int n) => $$"""{"schemaVersion":1,"commands":[{"id":"{{n}}"}]}""";

    [Fact]
    public async Task Requires_login()
    {
        await (await server.Anonymous().GetAsync("/api/v1/cloud-save")).ExpectProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Full_flow_with_optimistic_concurrency()
    {
        var client = server.As(ApiHelpers.NewUid());
        await (await client.GetAsync("/api/v1/cloud-save")).ExpectProblemAsync(HttpStatusCode.NotFound, "cloud_save_not_found");

        // No precondition at all → 428.
        await (await client.SendAsync(Put(Doc(1)))).ExpectProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");

        var first = await client.SendAsync(Put(Doc(1), create: true));
        var saved = await first.ReadAsync<CloudSaveRevisionResponse>();
        Assert.Equal(1, saved.Revision);
        Assert.Equal("\"1\"", first.Headers.ETag?.ToString());

        // A second "first save" (other device) loses.
        await (await client.SendAsync(Put(Doc(9), create: true))).ExpectProblemAsync(HttpStatusCode.PreconditionFailed, "revision_mismatch");

        var second = await (await client.SendAsync(Put(Doc(2), ifMatch: "\"1\""))).ReadAsync<CloudSaveRevisionResponse>();
        Assert.Equal(2, second.Revision);

        // Stale ETag → 412.
        await (await client.SendAsync(Put(Doc(3), ifMatch: "\"1\""))).ExpectProblemAsync(HttpStatusCode.PreconditionFailed, "revision_mismatch");

        var current = await client.GetAsync("/api/v1/cloud-save");
        await current.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("\"2\"", current.Headers.ETag?.ToString());
        Assert.Equal(Doc(2), await current.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_is_returned_byte_for_byte()
    {
        var client = server.As(ApiHelpers.NewUid());
        const string json = """
            {
              "schemaVersion": 2,
              "z": "Ümlaut & <tag>",
              "a": [1, 2, 3]
            }
            """;
        await (await client.SendAsync(Put(json, create: true))).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal(json, await (await client.GetAsync("/api/v1/cloud-save")).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("""{"commands":[]}""")]
    [InlineData("""{"schemaVersion":"1"}""")]
    [InlineData("""{"schemaVersion":0}""")]
    public async Task Invalid_documents_are_rejected(string json)
    {
        var client = server.As(ApiHelpers.NewUid());
        await (await client.SendAsync(Put(json, create: true))).ExpectProblemAsync(HttpStatusCode.BadRequest, "invalid_document");
    }

    [Fact]
    public async Task Too_large_document_is_413()
    {
        // The fixture limits documents to 4096 bytes.
        var json = $$"""{"schemaVersion":1,"data":"{{new string('x', 5000)}}"}""";
        await (await server.As(ApiHelpers.NewUid()).SendAsync(Put(json, create: true)))
            .ExpectProblemAsync(HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
    }

    [Fact]
    public async Task Keeps_the_last_ten_revisions_and_can_read_old_ones()
    {
        var client = server.As(ApiHelpers.NewUid());
        await (await client.SendAsync(Put(Doc(1), create: true))).ExpectAsync(HttpStatusCode.OK);
        for (var i = 2; i <= 12; i++)
            await (await client.SendAsync(Put(Doc(i), ifMatch: $"\"{i - 1}\""))).ExpectAsync(HttpStatusCode.OK);

        var revisions = await (await client.GetAsync("/api/v1/cloud-save/revisions")).ReadAsync<List<CloudSaveRevisionResponse>>();
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(i => (long)i), revisions.Select(r => r.Revision));

        Assert.Equal(Doc(5), await (await client.GetAsync("/api/v1/cloud-save/revisions/5")).Content.ReadAsStringAsync());
        await (await client.GetAsync("/api/v1/cloud-save/revisions/2")).ExpectProblemAsync(HttpStatusCode.NotFound, "revision_not_found");
    }

    [Fact]
    public async Task Delete_removes_everything()
    {
        var client = server.As(ApiHelpers.NewUid());
        await (await client.SendAsync(Put(Doc(1), create: true))).ExpectAsync(HttpStatusCode.OK);
        await (await client.DeleteAsync("/api/v1/cloud-save")).ExpectAsync(HttpStatusCode.NoContent);
        await (await client.GetAsync("/api/v1/cloud-save")).ExpectProblemAsync(HttpStatusCode.NotFound, "cloud_save_not_found");

        // Starting over works with If-None-Match again.
        var saved = await (await client.SendAsync(Put(Doc(2), create: true))).ReadAsync<CloudSaveRevisionResponse>();
        Assert.Equal(1, saved.Revision);
    }

    [Fact]
    public async Task Users_do_not_see_each_others_saves()
    {
        var alice = server.As(ApiHelpers.NewUid());
        await (await alice.SendAsync(Put(Doc(1), create: true))).ExpectAsync(HttpStatusCode.OK);
        await (await server.As(ApiHelpers.NewUid()).GetAsync("/api/v1/cloud-save"))
            .ExpectProblemAsync(HttpStatusCode.NotFound, "cloud_save_not_found");
    }
}
