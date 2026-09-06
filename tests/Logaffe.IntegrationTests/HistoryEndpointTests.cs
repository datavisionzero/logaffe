using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The history over HTTP, asked of an installation that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// The property worth starting a composition root for is that <b>the rows are
/// written by the requests that changed something</b> and by nothing else. Who
/// acted is a scoped service the two authentication doors fill in, so nothing
/// below them names an actor and nothing in a unit test can show that the door
/// actually does — an act performed with nobody behind it records nothing, and
/// a signed-in request that recorded nothing would look exactly the same.
/// </para>
/// <para>
/// The rest is the surface: it is an administrator's, and it is read-only —
/// there is no verb here that writes a row.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class HistoryEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _volume = InstallationVolume.Create(nameof(HistoryEndpointTests));

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

        _secondFactorSecret =
            (await ABootstrappedInstallation.SignInAsync(_installation)).SecondFactorSecret;
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Fact]
    public async Task The_history_is_behind_an_administrator_s_session()
    {
        using var stranger = _installation.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await stranger.GetAsync("/history", TestContext.Current.CancellationToken))
                .StatusCode);
    }

    [Fact]
    public async Task What_a_request_changed_is_written_down_as_it_happens()
    {
        using var client = await SignedInAsync();

        var project = await CreatedAsync(client, "orders-api");

        using var renamed = await client.PatchAsJsonAsync(
            $"/projects/{project.Id}",
            new { name = "orders" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var deleted = await client.DeleteAsync(
            $"/projects/{project.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var page = await HistoryAsync(client);

        // Newest first, and the three acts are the three rows — including the
        // deletion, which is the one whose subject is no longer there to look up.
        Assert.Equal(
            ["removed", "renamed", "created"],
            page.Take(3).Select(change => change.Act));

        Assert.All(
            page.Take(3),
            change =>
            {
                Assert.Equal("project", change.Subject);
                Assert.Equal(project.Id, change.SubjectId);

                // The door admitted a person, and the row says so by name — an
                // administrator reading this afterwards is not looking up a
                // GUID (ADR 0052).
                Assert.Equal("user", change.ActorKind);
                Assert.Equal(ABootstrappedInstallation.TheirName, change.ActorName);
            });

        Assert.Equal("orders", page[0].SubjectName);
        // A rename is its own act rather than a field moving, so it names none
        // and carries the two names instead.
        Assert.Equal("orders-api", page[1].From);
        Assert.Equal("orders", page[1].To);
        Assert.Null(page[1].Field);
    }

    [Fact]
    public async Task A_page_resumes_on_the_row_the_last_one_ended_at()
    {
        using var client = await SignedInAsync();

        foreach (var name in new[] { "first", "second", "third" })
        {
            await CreatedAsync(client, name);
        }

        var page = await HistoryAsync(client);
        Assert.Equal(["third", "second", "first"], page.Select(change => change.SubjectName));

        var rest = await HistoryAsync(client, page[0].Id);

        // The cursor is the row's own id and the page it opens excludes it, so
        // walking the history never shows one twice.
        Assert.Equal(["second", "first"], rest.Select(change => change.SubjectName));
    }

    private static async Task<IReadOnlyList<ChangeBody>> HistoryAsync(
        HttpClient client, long? before = null)
    {
        using var response = await client.GetAsync(
            before is null ? "/history" : $"/history?before={before}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<ChangeBody>>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<ProjectBody> CreatedAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ProjectBody>(
            TestContext.Current.CancellationToken))!;
    }

    /// <inheritdoc cref="TokenEndpointTests"/>
    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                email = ABootstrappedInstallation.TheirAddress,
                password = ABootstrappedInstallation.TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    /// <remarks>
    /// The three enumerations are read as the strings they go over the wire as,
    /// which is what a client written against the contract sees.
    /// </remarks>
    private sealed record ChangeBody(
        long Id,
        Guid ActorId,
        string ActorKind,
        string ActorName,
        DateTimeOffset At,
        string Subject,
        Guid? SubjectId,
        string SubjectName,
        string Act,
        string? Field,
        string? From,
        string? To);

    private sealed record ProjectBody(Guid Id, string Name);
}
