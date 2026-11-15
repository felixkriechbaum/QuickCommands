using System.Net;
using System.Text;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Tests.Infrastructure;

namespace QuickCommands.Server.Tests.Integration;

public sealed class AccountDeletionTests(ServerFixture server)
{
    [Fact]
    public async Task Deleting_an_account_removes_its_data_and_updates_counters()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var othersPackage = await author.PublishAsync(ApiHelpers.SamplePackage());

        var (leaving, leavingUid, _) = await server.CreatePublisherAsync();
        var ownPackage = await leaving.PublishAsync(ApiHelpers.SamplePackage());
        await (await leaving.PutAsync($"/api/v1/store/packages/{othersPackage.Id}/star", null)).ExpectAsync(HttpStatusCode.NoContent);
        await (await leaving.PostAsync($"/api/v1/store/packages/{othersPackage.Id}/install", null)).ExpectAsync(HttpStatusCode.NoContent);
        await (await leaving.PostJsonAsync($"/api/v1/store/packages/{othersPackage.Id}/reports", new ReportPackageRequest(ReportReason.Broken, null)))
            .ExpectAsync(HttpStatusCode.NoContent);
        var save = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cloud-save")
        {
            Content = new StringContent("""{"schemaVersion":1}""", Encoding.UTF8, "application/json"),
        };
        save.Headers.TryAddWithoutValidation("If-None-Match", "*");
        await (await leaving.SendAsync(save)).ExpectAsync(HttpStatusCode.OK);

        await (await leaving.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.NoContent);

        // Their package is a tombstone, their star is gone, the install count stays (it counts downloads).
        Assert.Equal(PackageStatus.Deleted, (await server.VersionOfAsync(ownPackage.Id))!.Status);
        var other = await (await server.Anonymous().GetAsync($"/api/v1/store/packages/{othersPackage.Id}")).ReadAsync<PackageDetailResponse>();
        Assert.Equal(0, other.StarCount);
        Assert.Equal(1, other.InstallCount);
        Assert.DoesNotContain(
            (await (await server.Admin().GetAsync("/api/v1/admin/reports?pageSize=50")).ReadAsync<PagedResponse<ReportResponse>>()).Items,
            r => r.PackageId == othersPackage.Id);

        // Signing in again starts from scratch.
        var again = server.As(leavingUid);
        var me = await (await again.GetAsync("/api/v1/me")).ReadAsync<MeResponse>();
        Assert.Null(me.Handle);
        await (await again.GetAsync("/api/v1/cloud-save")).ExpectProblemAsync(HttpStatusCode.NotFound, "cloud_save_not_found");
    }

    [Fact]
    public async Task A_banned_account_stays_banned_after_deletion()
    {
        var (client, uid, me) = await server.CreatePublisherAsync();
        await (await server.Admin().PostJsonAsync($"/api/v1/admin/users/{me.Id}/ban", new BanUserRequest(null, null)))
            .ExpectAsync(HttpStatusCode.NoContent);

        await (await client.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.NoContent);

        var again = server.As(uid);
        var profile = await (await again.GetAsync("/api/v1/me")).ReadAsync<MeResponse>();
        Assert.True(profile.IsBanned);
        Assert.Null(profile.Handle);
        await (await again.PutJsonAsync("/api/v1/me", new UpdateMeRequest("newname" + Guid.NewGuid().ToString("N")[..6])))
            .ExpectProblemAsync(HttpStatusCode.Forbidden, "user_banned");
    }
}
