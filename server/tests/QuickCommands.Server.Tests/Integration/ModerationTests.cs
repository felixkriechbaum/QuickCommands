using System.Net;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Tests.Infrastructure;

namespace QuickCommands.Server.Tests.Integration;

public sealed class ModerationTests(ServerFixture server)
{
    private static string ReportUrl(Guid id) => $"/api/v1/store/packages/{id}/reports";

    private async Task ReportAsync(Guid packageId, ReportReason reason = ReportReason.Spam) =>
        await (await server.As(ApiHelpers.NewUid()).PostJsonAsync(ReportUrl(packageId), new ReportPackageRequest(reason, "looks bad")))
            .ExpectAsync(HttpStatusCode.NoContent);

    [Fact]
    public async Task Own_package_and_double_reports_are_rejected()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());
        await (await author.PostJsonAsync(ReportUrl(package.Id), new ReportPackageRequest(ReportReason.Spam, null)))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "cannot_report_own");

        var reporter = server.As(ApiHelpers.NewUid());
        await (await reporter.PostJsonAsync(ReportUrl(package.Id), new ReportPackageRequest(ReportReason.Broken, null))).ExpectAsync(HttpStatusCode.NoContent);
        await (await reporter.PostJsonAsync(ReportUrl(package.Id), new ReportPackageRequest(ReportReason.Broken, null)))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "already_reported");
    }

    [Fact]
    public async Task Three_reports_hide_the_package()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var tag = ApiHelpers.NewTag();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage(tag));

        await ReportAsync(package.Id);
        await ReportAsync(package.Id);
        Assert.Equal(PackageStatus.Active, (await server.VersionOfAsync(package.Id))!.Status);
        await ReportAsync(package.Id, ReportReason.Malicious);

        Assert.Equal(PackageStatus.Hidden, (await server.VersionOfAsync(package.Id))!.Status);
        var search = await (await server.Anonymous().GetAsync($"/api/v1/store/packages?tag={tag}")).ReadAsync<PagedResponse<PackageSummaryResponse>>();
        Assert.Equal(0, search.TotalCount);
        await (await server.Anonymous().GetAsync($"/api/v1/store/packages/{package.Id}"))
            .ExpectProblemAsync(HttpStatusCode.NotFound, "package_not_found");

        // The author still sees it, but cannot edit it while it is being reviewed.
        var own = await (await author.GetAsync($"/api/v1/store/packages/{package.Id}")).ReadAsync<PackageDetailResponse>();
        Assert.Equal(PackageStatus.Hidden, own.Status);
        await (await author.PutJsonAsync($"/api/v1/store/packages/{package.Id}", ApiHelpers.SamplePackage()))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "package_locked");

        // Restoring dismisses the reports and starts the count from zero.
        await (await server.Admin().PostAsync($"/api/v1/admin/packages/{package.Id}/restore", null)).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal(PackageStatus.Active, (await server.VersionOfAsync(package.Id))!.Status);
        await ReportAsync(package.Id);
        Assert.Equal(PackageStatus.Active, (await server.VersionOfAsync(package.Id))!.Status);
    }

    [Fact]
    public async Task Admin_endpoints_need_admin()
    {
        await (await server.As(ApiHelpers.NewUid()).GetAsync("/api/v1/admin/reports")).ExpectProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await server.Anonymous().GetAsync("/api/v1/admin/reports")).ExpectProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Admin_sees_reports_and_removal_survives_deletion()
    {
        var (author, _, me) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage(title: "Totally safe"));
        await ReportAsync(package.Id, ReportReason.Malicious);

        var admin = server.Admin();
        var open = await (await admin.GetAsync("/api/v1/admin/reports?open=true&pageSize=50")).ReadAsync<PagedResponse<ReportResponse>>();
        var report = Assert.Single(open.Items, r => r.PackageId == package.Id);
        Assert.Equal(ReportReason.Malicious, report.Reason);
        Assert.Equal("Totally safe", report.PackageTitle);
        Assert.Equal(me.Handle, report.Author);
        Assert.Equal("looks bad", report.Comment);

        await (await admin.PostJsonAsync($"/api/v1/admin/packages/{package.Id}/remove", new RemovePackageRequest(ReportReason.Malicious)))
            .ExpectAsync(HttpStatusCode.NoContent);
        var version = await server.VersionOfAsync(package.Id);
        Assert.Equal(PackageStatus.Removed, version!.Status);
        Assert.Equal(ReportReason.Malicious, version.RemovalReason);

        var resolved = await (await admin.GetAsync("/api/v1/admin/reports?open=false&pageSize=50")).ReadAsync<PagedResponse<ReportResponse>>();
        Assert.Equal(ReportResolution.Upheld, Assert.Single(resolved.Items, r => r.PackageId == package.Id).Resolution);

        // The author deleting it must not turn "malicious" into a harmless "deleted".
        await (await author.DeleteAsync($"/api/v1/store/packages/{package.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        version = await server.VersionOfAsync(package.Id);
        Assert.Equal(PackageStatus.Removed, version!.Status);
        Assert.Equal(ReportReason.Malicious, version.RemovalReason);
        await (await admin.PostAsync($"/api/v1/admin/packages/{package.Id}/restore", null))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "package_deleted");
    }

    [Fact]
    public async Task Banned_users_can_only_read_and_delete_their_profile()
    {
        var (author, _, me) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());

        await (await server.Admin().PostJsonAsync($"/api/v1/admin/users/{me.Id}/ban", new BanUserRequest("spam", ReportReason.Spam)))
            .ExpectAsync(HttpStatusCode.NoContent);

        var version = await server.VersionOfAsync(package.Id);
        Assert.Equal(PackageStatus.Removed, version!.Status);
        Assert.Equal(ReportReason.Spam, version.RemovalReason);

        await (await author.PostJsonAsync("/api/v1/store/packages", ApiHelpers.SamplePackage()))
            .ExpectProblemAsync(HttpStatusCode.Forbidden, "user_banned");
        var profile = await (await author.GetAsync("/api/v1/me")).ReadAsync<MeResponse>();
        Assert.True(profile.IsBanned);

        await (await server.Admin().PostAsync($"/api/v1/admin/users/{me.Id}/unban", null)).ExpectAsync(HttpStatusCode.NoContent);
        await (await author.GetAsync("/api/v1/me/packages")).ExpectAsync(HttpStatusCode.OK);
    }
}
