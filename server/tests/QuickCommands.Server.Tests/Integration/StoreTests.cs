using System.Net;
using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;
using QuickCommands.Server.Tests.Infrastructure;

namespace QuickCommands.Server.Tests.Integration;

public sealed class StoreTests(ServerFixture server)
{
    private async Task<PagedResponse<PackageSummaryResponse>> SearchAsync(string query) =>
        await (await server.Anonymous().GetAsync("/api/v1/store/packages?" + query)).ReadAsync<PagedResponse<PackageSummaryResponse>>();

    [Fact]
    public async Task Publishing_requires_a_handle()
    {
        await (await server.As(ApiHelpers.NewUid()).PostJsonAsync("/api/v1/store/packages", ApiHelpers.SamplePackage()))
            .ExpectProblemAsync(HttpStatusCode.Conflict, "handle_required");
    }

    [Fact]
    public async Task Publishing_requires_login()
    {
        await (await server.Anonymous().PostJsonAsync("/api/v1/store/packages", ApiHelpers.SamplePackage()))
            .ExpectProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Publish_and_read_details()
    {
        var (client, _, me) = await server.CreatePublisherAsync();
        var request = ApiHelpers.SamplePackage(" Dev Tools ") with { Tags = ["Git", " Dev Tools ", "git"] };
        var response = await client.PostJsonAsync("/api/v1/store/packages", request);
        var created = await response.ReadAsync<PackageDetailResponse>(HttpStatusCode.Created);
        Assert.EndsWith($"/api/v1/store/packages/{created.Id}", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);

        var detail = await (await server.Anonymous().GetAsync($"/api/v1/store/packages/{created.Id}")).ReadAsync<PackageDetailResponse>();
        Assert.Equal("Git helpers", detail.Title);
        Assert.Equal("git", detail.Category);
        Assert.Equal(["dev-tools", "git"], detail.Tags);
        Assert.Equal(me.Handle, detail.Author);
        Assert.Equal(1, detail.Version);
        Assert.Equal(PackageStatus.Active, detail.Status);
        Assert.Null(detail.StarredByMe);
        Assert.False(detail.IsOwner);
        Assert.Equal(2, detail.Commands.Count);
        Assert.Equal(ShellLanguage.Sh, detail.Commands[0].Language);
        Assert.Equal("git status \"$@\"", detail.Commands[0].Script);
        Assert.Null(detail.Commands[0].Parameters);
        Assert.Equal("""[{"name":"short","kind":"switch"}]""", detail.Commands[1].Parameters?.GetRawText());
    }

    [Fact]
    public async Task Unknown_category_is_rejected()
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        await (await client.PostJsonAsync("/api/v1/store/packages", ApiHelpers.SamplePackage() with { Category = "cooking" }))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "unknown_category");
    }

    [Theory]
    [InlineData("qc")]
    [InlineData("-dash")]
    [InlineData("has space")]
    public async Task Invalid_command_names_are_rejected(string name)
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var request = ApiHelpers.SamplePackage(commands: new PackageCommandRequest(name, ShellLanguage.Sh, null, "echo", null));
        var problem = await (await client.PostJsonAsync("/api/v1/store/packages", request))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("commands[0].name", out _), problem.ToString());
    }

    [Fact]
    public async Task Duplicate_name_in_the_same_language_is_rejected()
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var request = ApiHelpers.SamplePackage(commands:
        [
            new PackageCommandRequest("gs", ShellLanguage.Sh, null, "git status", null),
            new PackageCommandRequest("GS", ShellLanguage.Sh, null, "git status -s", null),
        ]);
        await (await client.PostJsonAsync("/api/v1/store/packages", request)).ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Too_many_tags_and_empty_commands_are_rejected()
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var problem = await (await client.PostJsonAsync("/api/v1/store/packages",
                ApiHelpers.SamplePackage() with { Tags = ["a1", "b2", "c3", "d4", "e5", "f6"], Commands = [] }))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("tags", out _));
        Assert.True(errors.TryGetProperty("commands", out _));
    }

    [Fact]
    public async Task Search_filters_by_tag_text_category_and_language()
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var tag = ApiHelpers.NewTag();
        var git = await client.PublishAsync(ApiHelpers.SamplePackage(tag));
        var docker = await client.PublishAsync(new PackageRequest("Docker cleanup", "Prune everything", "docker", [tag],
            [new PackageCommandRequest("dprune", ShellLanguage.PowerShell, null, "docker system prune -f", null)]));

        var byTag = await SearchAsync($"tag={tag}");
        Assert.Equal(2, byTag.TotalCount);

        var byCommandName = await SearchAsync($"tag={tag}&q=DPRU");
        Assert.Equal(docker.Id, Assert.Single(byCommandName.Items).Id);

        var byCategory = await SearchAsync($"tag={tag}&category=git");
        Assert.Equal(git.Id, Assert.Single(byCategory.Items).Id);

        var byLanguage = await SearchAsync($"tag={tag}&language=sh");
        var item = Assert.Single(byLanguage.Items);
        Assert.Equal(git.Id, item.Id);
        Assert.Equal([ShellLanguage.PowerShell, ShellLanguage.Sh], item.Languages);
        Assert.Equal(2, item.CommandCount);
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("stars")]
    [InlineData("installs")]
    [InlineData("new")]
    [InlineData("updated")]
    public async Task Every_sort_order_works(string sort)
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var tag = ApiHelpers.NewTag();
        var older = await client.PublishAsync(ApiHelpers.SamplePackage(tag, "Older package"));
        var newer = await client.PublishAsync(ApiHelpers.SamplePackage(tag, "Newer package"));

        // Give the older one a star: wins for popular and stars, loses for new.
        await (await server.As(ApiHelpers.NewUid()).PutAsync($"/api/v1/store/packages/{older.Id}/star", null)).ExpectAsync(HttpStatusCode.NoContent);

        var result = await SearchAsync($"tag={tag}&sort={sort}&pageSize=10");
        Assert.Equal(2, result.TotalCount);
        var expectedFirst = sort is "popular" or "stars" ? older.Id : newer.Id;
        Assert.Equal(expectedFirst, result.Items[0].Id);
    }

    [Fact]
    public async Task Paging_and_page_size_limit()
    {
        var (client, _, _) = await server.CreatePublisherAsync();
        var tag = ApiHelpers.NewTag();
        for (var i = 0; i < 3; i++)
            await client.PublishAsync(ApiHelpers.SamplePackage(tag, $"Package {i}"));

        var page2 = await SearchAsync($"tag={tag}&sort=new&page=2&pageSize=2");
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal("Package 0", Assert.Single(page2.Items).Title);

        await (await server.Anonymous().GetAsync("/api/v1/store/packages?pageSize=51"))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Only_the_author_can_update_and_updates_bump_the_version()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());

        var (other, _, _) = await server.CreatePublisherAsync();
        await (await other.PutJsonAsync($"/api/v1/store/packages/{package.Id}", ApiHelpers.SamplePackage()))
            .ExpectProblemAsync(HttpStatusCode.Forbidden, "forbidden");

        var changed = ApiHelpers.SamplePackage(commands: new PackageCommandRequest("gl", ShellLanguage.Cmd, "log", "git log %*", null))
            with { Title = "Renamed", Tags = ["git", "log"] };
        var updated = await (await author.PutJsonAsync($"/api/v1/store/packages/{package.Id}", changed)).ReadAsync<PackageDetailResponse>();
        Assert.Equal(2, updated.Version);
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(["git", "log"], updated.Tags);
        Assert.Equal("gl", Assert.Single(updated.Commands).Name);

        var version = await server.VersionOfAsync(package.Id);
        Assert.Equal(PackageStatus.Active, version!.Status);
        Assert.Equal(2, version.Version);
    }

    [Fact]
    public async Task Stars_and_installs_count_once_per_user()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());
        var fan = server.As(ApiHelpers.NewUid());
        var url = $"/api/v1/store/packages/{package.Id}";

        for (var i = 0; i < 2; i++)
        {
            await (await fan.PutAsync($"{url}/star", null)).ExpectAsync(HttpStatusCode.NoContent);
            await (await fan.PostAsync($"{url}/install", null)).ExpectAsync(HttpStatusCode.NoContent);
        }
        var detail = await (await fan.GetAsync(url)).ReadAsync<PackageDetailResponse>();
        Assert.Equal(1, detail.StarCount);
        Assert.Equal(1, detail.InstallCount);
        Assert.True(detail.StarredByMe);
        Assert.True(detail.InstalledByMe);

        await (await fan.DeleteAsync($"{url}/star")).ExpectAsync(HttpStatusCode.NoContent);
        await (await fan.DeleteAsync($"{url}/star")).ExpectAsync(HttpStatusCode.NoContent);
        detail = await (await fan.GetAsync(url)).ReadAsync<PackageDetailResponse>();
        Assert.Equal(0, detail.StarCount);
        Assert.False(detail.StarredByMe);
    }

    [Fact]
    public async Task Concurrent_stars_of_one_user_count_once()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());
        var fan = server.As(ApiHelpers.NewUid());
        await (await fan.GetAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.OK);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => fan.PutAsync($"/api/v1/store/packages/{package.Id}/star", null)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        var detail = await (await server.Anonymous().GetAsync($"/api/v1/store/packages/{package.Id}")).ReadAsync<PackageDetailResponse>();
        Assert.Equal(1, detail.StarCount);
    }

    [Fact]
    public async Task Deleting_leaves_a_tombstone()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var tag = ApiHelpers.NewTag();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage(tag));

        await (await server.As(ApiHelpers.NewUid()).DeleteAsync($"/api/v1/store/packages/{package.Id}"))
            .ExpectProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await author.DeleteAsync($"/api/v1/store/packages/{package.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        await (await server.Anonymous().GetAsync($"/api/v1/store/packages/{package.Id}"))
            .ExpectProblemAsync(HttpStatusCode.NotFound, "package_not_found");
        Assert.Equal(0, (await SearchAsync($"tag={tag}")).TotalCount);
        var version = await server.VersionOfAsync(package.Id);
        Assert.Equal(PackageStatus.Deleted, version!.Status);
        Assert.Null(version.Version);

        await (await author.DeleteAsync($"/api/v1/store/packages/{package.Id}"))
            .ExpectProblemAsync(HttpStatusCode.NotFound, "package_not_found");
    }

    [Fact]
    public async Task Version_check_ignores_unknown_ids_and_limits_the_count()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var package = await author.PublishAsync(ApiHelpers.SamplePackage());
        var versions = await (await server.Anonymous().GetAsync($"/api/v1/store/packages/versions?ids={package.Id}&ids={Guid.NewGuid()}"))
            .ReadAsync<List<PackageVersionResponse>>();
        Assert.Equal(package.Id, Assert.Single(versions).Id);

        var tooMany = string.Join('&', Enumerable.Range(0, 101).Select(_ => $"ids={Guid.NewGuid()}"));
        await (await server.Anonymous().GetAsync($"/api/v1/store/packages/versions?{tooMany}"))
            .ExpectProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task My_packages_lists_own_packages()
    {
        var (author, _, _) = await server.CreatePublisherAsync();
        var kept = await author.PublishAsync(ApiHelpers.SamplePackage(title: "Kept"));
        var deleted = await author.PublishAsync(ApiHelpers.SamplePackage(title: "Deleted"));
        await (await author.DeleteAsync($"/api/v1/store/packages/{deleted.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        var mine = await (await author.GetAsync("/api/v1/me/packages")).ReadAsync<List<PackageSummaryResponse>>();
        Assert.Equal(kept.Id, Assert.Single(mine).Id);
    }
}
