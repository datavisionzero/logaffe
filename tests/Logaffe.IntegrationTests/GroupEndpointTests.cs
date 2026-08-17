using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's group acts over HTTP, asked of an installation that is
/// actually running.
/// </summary>
/// <remarks>
/// <para>
/// As with the project endpoints, the property worth starting a composition root
/// for is that <b>every one of these is behind the operator's session</b>.
/// </para>
/// <para>
/// The rest is what only a real database answers, and it is the half of ADR 0039
/// that no in-memory double can hold: that a project's name is unique
/// <i>within</i> its group while two projects in no group still collide — which
/// is <c>ix_project_group_id_name</c> and its <c>nulls not distinct</c> — and
/// that removing a group leaves its projects behind rather than taking them,
/// which is the <c>on delete set null</c> on
/// <c>fk_project_project_group</c>.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class GroupEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";
    private const string NoSuchGroup = "0195f0d4-0000-7000-8000-000000000000";

    private readonly string _volume = InstallationVolume.Create(nameof(GroupEndpointTests));

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres", await postgres.CreateDatabaseAsync());
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await ClaimAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Theory]
    [InlineData("GET", "/groups")]
    [InlineData("POST", "/groups")]
    [InlineData("PATCH", $"/groups/{NoSuchGroup}")]
    [InlineData("DELETE", $"/groups/{NoSuchGroup}")]
    [InlineData("PUT", $"/projects/{NoSuchGroup}/group")]
    public async Task Every_group_endpoint_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "shop" }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_group_is_made_listed_renamed_and_removed()
    {
        using var client = await SignedInAsync();

        var made = await ReadAsync<GroupBody>(await client.PostAsJsonAsync(
            "/groups", new { name = "  shop  " }, TestContext.Current.CancellationToken));

        // The name is stored as it would be, not as it was typed.
        Assert.Equal("shop", made.Name);

        // A group made before its first project is on the list all the same: it
        // is something the operator made and not a side effect of what the
        // projects say (ADR 0039).
        var listed = Assert.Single(await ReadAsync<ListedGroupBody[]>(
            await client.GetAsync("/groups", TestContext.Current.CancellationToken)));
        Assert.Equal(made.Id, listed.Id);
        Assert.Equal("shop", listed.Name);

        using var renamed = await client.PatchAsJsonAsync(
            $"/groups/{made.Id}",
            new { name = "storefront" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var removed = await client.DeleteAsync(
            $"/groups/{made.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // One already gone is a second click or another tab, and not a failure.
        using var again = await client.DeleteAsync(
            $"/groups/{made.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_second_group_by_that_name_is_refused_by_the_installation()
    {
        using var client = await SignedInAsync();

        await MakeAsync(client, "shop");

        using var second = await client.PostAsJsonAsync(
            "/groups", new { name = "shop" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var other = await MakeAsync(client, "blog");
        using var renamed = await client.PatchAsJsonAsync(
            $"/groups/{other.Id}", new { name = "shop" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_that_is_not_one_is_refused(string name)
    {
        using var client = await SignedInAsync();

        using var response = await client.PostAsJsonAsync(
            "/groups", new { name }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_projects_share_a_name_in_two_groups_and_never_in_none()
    {
        using var client = await SignedInAsync();

        var shop = await MakeAsync(client, "shop");
        var blog = await MakeAsync(client, "blog");

        var one = await CreateProjectAsync(client, "api");
        await MoveAsync(client, one.Id, shop.Id, HttpStatusCode.NoContent);

        // `api` is free among the projects in no group again, and free inside
        // blog: the index is over the group and the name together.
        var other = await CreateProjectAsync(client, "api");
        await MoveAsync(client, other.Id, blog.Id, HttpStatusCode.NoContent);

        // And two of them in no group is still the three-in-the-morning trap
        // the uniqueness exists for, which `nulls not distinct` is what holds.
        await CreateProjectAsync(client, "api");
        using var third = await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task A_move_into_a_group_holding_that_name_is_refused()
    {
        using var client = await SignedInAsync();

        var shop = await MakeAsync(client, "shop");

        var inside = await CreateProjectAsync(client, "api");
        await MoveAsync(client, inside.Id, shop.Id, HttpStatusCode.NoContent);

        var outside = await CreateProjectAsync(client, "api");

        // Refused rather than resolved: renaming a project the operator did not
        // ask to rename is not the move's to do.
        await MoveAsync(client, outside.Id, shop.Id, HttpStatusCode.Conflict);

        var listed = await ReadAsync<ListedProjectBody[]>(await client.GetAsync(
            "/projects", TestContext.Current.CancellationToken));
        Assert.Null(listed.Single(project => project.Id == outside.Id).GroupId);
    }

    [Fact]
    public async Task Removing_a_group_leaves_its_projects_and_takes_nothing_with_it()
    {
        using var client = await SignedInAsync();

        var shop = await MakeAsync(client, "shop");
        var project = await CreateProjectAsync(client, "api");
        await MoveAsync(client, project.Id, shop.Id, HttpStatusCode.NoContent);

        var token = await ReadAsync<IssuedIngestTokenBody>(await client.PostAsync(
            $"/projects/{project.Id}/ingest-tokens",
            null,
            TestContext.Current.CancellationToken));

        using var removed = await client.DeleteAsync(
            $"/groups/{shop.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // The project stays, in no group, and it can still be delivered to: the
        // foreign key sets the column to null rather than cascading (ADR 0039).
        var listed = Assert.Single(await ReadAsync<ListedProjectBody[]>(
            await client.GetAsync("/projects", TestContext.Current.CancellationToken)));
        Assert.Equal(project.Id, listed.Id);
        Assert.Null(listed.GroupId);
        Assert.Equal(1, listed.IngestTokens);

        using var readBack = await client.GetAsync(
            $"/ingest-tokens/{token.Id}/token", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_leaves_the_group_it_was_in()
    {
        using var client = await SignedInAsync();

        var shop = await MakeAsync(client, "shop");
        var project = await CreateProjectAsync(client, "api");
        await MoveAsync(client, project.Id, shop.Id, HttpStatusCode.NoContent);

        using var deleted = await client.DeleteAsync(
            $"/projects/{project.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var listed = Assert.Single(await ReadAsync<ListedGroupBody[]>(
            await client.GetAsync("/groups", TestContext.Current.CancellationToken)));
        Assert.Equal(shop.Id, listed.Id);
        Assert.Empty(await ReadAsync<ListedProjectBody[]>(
            await client.GetAsync("/projects", TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_project_is_created_into_a_group_in_one_act()
    {
        using var client = await SignedInAsync();

        var shop = await MakeAsync(client, "shop");

        var created = await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7, groupId = shop.Id },
            TestContext.Current.CancellationToken));

        Assert.Equal(shop.Id, created.GroupId);

        // The name is taken inside that group now and free outside it, which is
        // the index over the group and the name together.
        using var again = await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7, groupId = shop.Id },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var loose = await CreateProjectAsync(client, "api");
        Assert.Null(loose.GroupId);
    }

    [Fact]
    public async Task A_creation_into_a_group_that_is_not_there_is_an_answer()
    {
        using var client = await SignedInAsync();

        // Reaching the foreign key instead would surface as a failure of the
        // installation, when what happened is that the operator named a group
        // another browser removed.
        using var response = await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7, groupId = Guid.Parse(NoSuchGroup) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ReadAsync<ListedProjectBody[]>(
            await client.GetAsync("/projects", TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_group_or_a_project_that_is_not_there_is_an_answer_rather_than_a_failure()
    {
        using var client = await SignedInAsync();

        using var renamed = await client.PatchAsJsonAsync(
            $"/groups/{NoSuchGroup}", new { name = "shop" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);

        // A move naming a group that is gone reaches the foreign key otherwise,
        // and a violation surfacing as a 500 is not what happened: the operator
        // named something another tab removed.
        var project = await CreateProjectAsync(client, "api");
        await MoveAsync(client, project.Id, Guid.Parse(NoSuchGroup), HttpStatusCode.NotFound);

        await MoveAsync(client, Guid.Parse(NoSuchGroup), null, HttpStatusCode.NotFound);
    }

    private async Task<GroupBody> MakeAsync(HttpClient client, string name) =>
        await ReadAsync<GroupBody>(await client.PostAsJsonAsync(
            "/groups", new { name }, TestContext.Current.CancellationToken));

    private async Task<ProjectBody> CreateProjectAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    private async Task MoveAsync(
        HttpClient client, Guid project, Guid? group, HttpStatusCode expected)
    {
        using var response = await client.PutAsJsonAsync(
            $"/projects/{project}/group",
            new { groupId = group },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    /// <inheritdoc cref="ProjectEndpointTests"/>
    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    /// <summary>
    /// Puts the installation in the state a completed claim leaves it in, which
    /// is the state every act here is reached from.
    /// </summary>
    private async Task ClaimAsync()
    {
        var enrolled = await AClaimedInstallation.ClaimAsync(_installation, _volume);

        _secondFactorSecret = enrolled.SecondFactorSecret;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(
                response.IsSuccessStatusCode,
                $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}");

            return (await response.Content.ReadFromJsonAsync<T>(
                TestContext.Current.CancellationToken))!;
        }
    }

    private sealed record Enrolment(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes, string Ticket);

    private sealed record GroupBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record ListedGroupBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record ProjectBody(
        Guid Id, string Name, Guid? GroupId, int RetentionDays, DateTimeOffset CreatedAt);

    private sealed record ListedProjectBody(
        Guid Id,
        string Name,
        Guid? GroupId,
        int RetentionDays,
        DateTimeOffset CreatedAt,
        int IngestTokens,
        DateTimeOffset? LastReceivedAt);

    private sealed record IssuedIngestTokenBody(Guid Id, string Token, DateTimeOffset IssuedAt);
}
