using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's host acts over HTTP, asked of an installation that is actually
/// running.
/// </summary>
/// <remarks>
/// <para>
/// As with the projects and the groups, the property worth starting a
/// composition root for is that <b>every one of these is behind the operator's
/// session</b>.
/// </para>
/// <para>
/// The rest is what only a real database answers: that a host's name is unique
/// across the installation with no group to relax it, that removing a host
/// leaves the projects that sat on it sitting on none — which is the
/// <c>on delete set null</c> on <c>fk_project_host</c> — and that it takes its
/// tokens with it.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class HostEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string NoSuchHost = "0195f0d4-0000-7000-8000-000000000000";

    private readonly string _volume = InstallationVolume.Create(nameof(HostEndpointTests));

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

        var enrolled = await AClaimedInstallation.ClaimAsync(_installation, _volume);
        _secondFactorSecret = enrolled.SecondFactorSecret;
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Theory]
    [InlineData("GET", "/hosts")]
    [InlineData("POST", "/hosts")]
    [InlineData("PATCH", $"/hosts/{NoSuchHost}")]
    [InlineData("DELETE", $"/hosts/{NoSuchHost}")]
    [InlineData("GET", $"/hosts/{NoSuchHost}/samples?from=2026-08-08T10:00:00Z&to=2026-08-08T11:00:00Z")]
    [InlineData("POST", $"/hosts/{NoSuchHost}/host-tokens")]
    [InlineData("GET", $"/hosts/{NoSuchHost}/host-tokens")]
    [InlineData("GET", $"/host-tokens/{NoSuchHost}/token")]
    [InlineData("DELETE", $"/host-tokens/{NoSuchHost}")]
    [InlineData("PUT", $"/projects/{NoSuchHost}/host")]
    [InlineData("GET", "/samples/retention")]
    [InlineData("PUT", "/samples/retention")]
    [InlineData("GET", "/samples/retention/outside?retentionDays=7")]
    public async Task Every_host_endpoint_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "web-01", retentionDays = 7 }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_host_is_made_listed_renamed_and_removed()
    {
        using var client = await SignedInAsync();

        var made = await MakeAsync(client, "  web-01  ");

        // The name is stored as it would be, not as it was typed.
        Assert.Equal("web-01", made.Name);

        // A host that has never reported is on the list all the same: it is
        // something the operator made, and a list that omitted it would answer
        // where the machine they just added went.
        var listed = Assert.Single(await ListAsync(client));

        Assert.Equal("web-01", listed.Name);
        Assert.Null(listed.LastReportedAt);
        Assert.Equal(0, listed.HostTokens);
        Assert.Equal(0, listed.Projects);

        using (var renamed = await client.PatchAsJsonAsync(
            $"/hosts/{made.Id}",
            new { name = "web-1" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        }

        // The identity survives it, which is the whole reason a host is a row.
        Assert.Equal(made.Id, Assert.Single(await ListAsync(client)).Id);

        using (var removed = await client.DeleteAsync(
            $"/hosts/{made.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.Empty(await ListAsync(client));

        // One already gone is a second click or another tab, not a failure.
        using var again = await client.DeleteAsync(
            $"/hosts/{made.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_name_is_taken_across_the_installation_because_a_host_sits_in_nothing()
    {
        using var client = await SignedInAsync();

        await MakeAsync(client, "web-01");

        // There is no group to relax it the way a project's name is relaxed: two
        // machines called `web-01` are the trap with nothing beside them to tell
        // them apart.
        using var taken = await client.PostAsJsonAsync(
            "/hosts", new { name = "web-01" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        // And a rename onto one that is taken is the same answer.
        var second = await MakeAsync(client, "web-02");

        using var renamed = await client.PatchAsJsonAsync(
            $"/hosts/{second.Id}",
            new { name = "web-01" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
    }

    [Fact]
    public async Task A_project_is_put_on_a_host_and_taken_off_again()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");
        var project = await CreateProjectAsync(client, "orders");

        // Every project is on no host until the operator says otherwise, and it
        // costs nothing except that there is no band to draw over its entries.
        Assert.Null(project.HostId);

        await PutOnAsync(client, project.Id, host.Id, HttpStatusCode.NoContent);

        Assert.Equal(host.Id, (await ListProjectsAsync(client)).Single().HostId);
        Assert.Equal(1, (await ListAsync(client)).Single().Projects);

        // Unlike a group there is no name to be taken: two projects called `api`
        // may perfectly well run on one machine, because the host is not where
        // they are listed.
        var second = await CreateProjectAsync(client, "billing");
        await PutOnAsync(client, second.Id, host.Id, HttpStatusCode.NoContent);

        Assert.Equal(2, (await ListAsync(client)).Single().Projects);

        await PutOnAsync(client, project.Id, null, HttpStatusCode.NoContent);
        await PutOnAsync(client, project.Id, Guid.Parse(NoSuchHost), HttpStatusCode.NotFound);
        await PutOnAsync(client, Guid.Parse(NoSuchHost), host.Id, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_host_leaves_the_projects_that_sat_on_it_sitting_on_none()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");
        var project = await CreateProjectAsync(client, "orders");

        await PutOnAsync(client, project.Id, host.Id, HttpStatusCode.NoContent);

        using (var removed = await client.DeleteAsync(
            $"/hosts/{host.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        // Forgetting where a project runs destroys nothing that belongs to the
        // project: it keeps its name, its retention and its entries, and loses
        // the band over them.
        var listed = Assert.Single(await ListProjectsAsync(client));

        Assert.Equal("orders", listed.Name);
        Assert.Equal(7, listed.RetentionDays);
        Assert.Null(listed.HostId);
    }

    [Fact]
    public async Task A_host_holds_one_token_and_two_while_it_is_being_rotated()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");

        var first = await IssueAsync(client, host.Id);
        var second = await IssueAsync(client, host.Id);

        Assert.NotEqual(first.Token, second.Token);

        // A third is refused rather than queued: two is what moving a fleet over
        // one machine at a time needs, and a third means the operator has lost
        // track of which one they are retiring.
        using (var third = await client.PostAsync(
            $"/hosts/{host.Id}/host-tokens", null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        }

        Assert.Equal(2, Assert.Single(await ListAsync(client)).HostTokens);

        // A host that is not there is 404 rather than a conflict: nothing about
        // the request is wrong and the address is what is gone.
        using var nowhere = await client.PostAsync(
            $"/hosts/{NoSuchHost}/host-tokens", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [Fact]
    public async Task Issuing_a_token_hands_back_the_command_that_starts_the_collector()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");
        var issued = await IssueAsync(client, host.Id);

        // What the operator pastes, with this installation's address, this token
        // and the two mounts a container needs to see its machine already in it.
        Assert.Contains(issued.Token, issued.CollectorCommand, StringComparison.Ordinal);
        Assert.Contains("-v /proc:/host/proc:ro", issued.CollectorCommand, StringComparison.Ordinal);
        Assert.Contains("-v /:/rootfs:ro,rslave", issued.CollectorCommand, StringComparison.Ordinal);
        Assert.Contains(
            "LOGAFFE_ENDPOINT=http://localhost",
            issued.CollectorCommand,
            StringComparison.Ordinal);

        // Not `--privileged`, no PID namespace and no Docker socket: reading
        // processes is what would need the first and reading containers the
        // second, and the closed schema collects neither.
        Assert.DoesNotContain("--privileged", issued.CollectorCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("--pid", issued.CollectorCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("docker.sock", issued.CollectorCommand, StringComparison.Ordinal);

        // A list carries no secret: opening the settings of a host is not the
        // same act as reading its credential.
        var listed = Assert.Single(await ReadAsync<IReadOnlyList<ListedHostTokenBody>>(
            await client.GetAsync(
                $"/hosts/{host.Id}/host-tokens", TestContext.Current.CancellationToken)));

        Assert.DoesNotContain(issued.Token, listed.Identifier, StringComparison.Ordinal);
        Assert.Null(listed.LastUsedAt);

        // And the same command comes back whenever the token is read, which on a
        // fleet is the difference between looking a value up and going round
        // every machine (ADR 0022).
        var read = await ReadAsync<ReadHostTokenBody>(await client.GetAsync(
            $"/host-tokens/{issued.Id}/token", TestContext.Current.CancellationToken));

        Assert.Equal(issued.Token, read.Token);
        Assert.Equal(issued.CollectorCommand, read.CollectorCommand);

        using (var revoked = await client.DeleteAsync(
            $"/host-tokens/{issued.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        using var gone = await client.GetAsync(
            $"/host-tokens/{issued.Id}/token", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task The_sample_window_is_one_number_for_the_installation_and_it_is_capped()
    {
        using var client = await SignedInAsync();

        // An installation that has never been told keeps the default, and no row
        // is written to say so.
        var standing = await ReadAsync<SampleRetentionBody>(await client.GetAsync(
            "/samples/retention", TestContext.Current.CancellationToken));

        Assert.Equal(30, standing.RetentionDays);

        // Asked before it takes effect, because a settings field that silently
        // destroys data is a bad settings field.
        var outside = await ReadAsync<SamplesOutsideBody>(await client.GetAsync(
            "/samples/retention/outside?retentionDays=7", TestContext.Current.CancellationToken));

        Assert.Equal(7, outside.RetentionDays);
        Assert.Equal(0, outside.Samples);

        using (var changed = await client.PutAsJsonAsync(
            "/samples/retention",
            new { retentionDays = 7 },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        Assert.Equal(
            7,
            (await ReadAsync<SampleRetentionBody>(await client.GetAsync(
                "/samples/retention", TestContext.Current.CancellationToken))).RetentionDays);

        // Refused where every other window is: a settings box without a ceiling
        // is how a product that is not a multi-year archive becomes one
        // (ADR 0020).
        foreach (var days in new[] { 0, 91, 365 })
        {
            using var refused = await client.PutAsJsonAsync(
                "/samples/retention",
                new { retentionDays = days },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [Fact]
    public async Task A_read_of_a_host_that_is_gone_is_an_answer_and_not_an_empty_window()
    {
        using var client = await SignedInAsync();

        using var response = await client.GetAsync(
            $"/hosts/{NoSuchHost}/samples"
            + "?from=2026-08-08T10:00:00Z&to=2026-08-08T11:00:00Z",
            TestContext.Current.CancellationToken);

        // An empty window would read as a quiet machine, and the machine is not
        // quiet — it is not there.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_read_says_how_it_divided_the_range_because_the_caller_did_not()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");

        var window = await ReadAsync<SampleWindowBody>(await client.GetAsync(
            $"/hosts/{host.Id}/samples?from=2026-08-08T10:00:00Z&to=2026-08-08T11:00:00Z",
            TestContext.Current.CancellationToken));

        // An hour is sixty spans of a minute: a bucket is never finer than the
        // interval that fills it. The number is on the answer because nothing
        // asked for it — and a band cannot tell a run from a gap without it.
        Assert.Equal(60, window.BucketSeconds);
        Assert.Equal("web-01", window.HostName);
    }

    [Fact]
    public async Task A_range_far_wider_than_two_hundred_readings_is_still_two_hundred_spans()
    {
        using var client = await SignedInAsync();

        var host = await MakeAsync(client, "web-01");

        var window = await ReadAsync<SampleWindowBody>(await client.GetAsync(
            $"/hosts/{host.Id}/samples?from=2026-08-01T00:00:00Z&to=2026-08-08T00:00:00Z",
            TestContext.Current.CancellationToken));

        // A week is ten thousand readings, and the cap is what keeps the size
        // of an answer a property of the product rather than of the question.
        Assert.Equal(TimeSpan.FromDays(7).TotalSeconds / 200, window.BucketSeconds);
    }

    [Fact]
    public async Task A_host_has_a_name_and_it_is_refused_where_it_is_not_one()
    {
        using var client = await SignedInAsync();

        foreach (var name in new[] { null, string.Empty, "   ", new string('x', 101) })
        {
            using var refused = await client.PostAsJsonAsync(
                "/hosts", new { name }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    private async Task<HostBody> MakeAsync(HttpClient client, string name) =>
        await ReadAsync<HostBody>(await client.PostAsJsonAsync(
            "/hosts", new { name }, TestContext.Current.CancellationToken));

    private async Task<IReadOnlyList<ListedHostBody>> ListAsync(HttpClient client) =>
        await ReadAsync<IReadOnlyList<ListedHostBody>>(await client.GetAsync(
            "/hosts", TestContext.Current.CancellationToken));

    private async Task<IssuedHostTokenBody> IssueAsync(HttpClient client, Guid hostId) =>
        await ReadAsync<IssuedHostTokenBody>(await client.PostAsync(
            $"/hosts/{hostId}/host-tokens", null, TestContext.Current.CancellationToken));

    private async Task<ProjectBody> CreateProjectAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    private async Task<IReadOnlyList<ListedProjectBody>> ListProjectsAsync(HttpClient client) =>
        await ReadAsync<IReadOnlyList<ListedProjectBody>>(await client.GetAsync(
            "/projects", TestContext.Current.CancellationToken));

    private static async Task PutOnAsync(
        HttpClient client, Guid project, Guid? host, HttpStatusCode expected)
    {
        using var response = await client.PutAsJsonAsync(
            $"/projects/{project}/host",
            new { hostId = host },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = AClaimedInstallation.TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
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

    private sealed record HostBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record ListedHostBody(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAt,
        int HostTokens,
        DateTimeOffset? LastReportedAt,
        int Projects);

    private sealed record IssuedHostTokenBody(
        Guid Id, string Token, string CollectorCommand, DateTimeOffset IssuedAt);

    private sealed record ListedHostTokenBody(
        Guid Id, string Identifier, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

    private sealed record ReadHostTokenBody(string Token, string CollectorCommand);

    private sealed record ProjectBody(
        Guid Id, string Name, Guid? GroupId, Guid? HostId, int RetentionDays);

    private sealed record ListedProjectBody(
        Guid Id,
        string Name,
        Guid? GroupId,
        Guid? HostId,
        int RetentionDays,
        DateTimeOffset CreatedAt,
        int IngestTokens,
        DateTimeOffset? LastReceivedAt);

    private sealed record SampleRetentionBody(int RetentionDays);

    private sealed record SamplesOutsideBody(int RetentionDays, long Samples);

    /// <summary>
    /// Only what a read of an empty host answers with. The buckets themselves
    /// are asserted where there are samples to put in them
    /// (<c>SampleEndpointTests</c>, <c>SampleReaderTests</c>).
    /// </summary>
    private sealed record SampleWindowBody(string HostName, double BucketSeconds);
}
