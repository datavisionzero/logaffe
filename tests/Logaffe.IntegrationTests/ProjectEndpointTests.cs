using System.Net;
using System.Net.Http.Json;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's project acts over HTTP, asked of an installation that is
/// actually running.
/// </summary>
/// <remarks>
/// <para>
/// As with the token endpoints, the property worth starting a composition root
/// for is that <b>every one of these is behind the operator's session</b>. A
/// project surface reachable without one is an installation whose log store a
/// stranger can create in and delete from, and it is a one-line mistake.
/// </para>
/// <para>
/// The rest is what only a real database answers: that a name is unique across
/// the installation, that deleting takes the project's tokens with it by the
/// cascade rather than by anything the act remembers, and that issuing into a
/// project that is not there is an answer rather than a foreign key violation
/// surfacing as a failure of the installation.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ProjectEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";
    private const string NoSuchProject = "0195f0d4-0000-7000-8000-000000000000";

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await ClaimAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Theory]
    [InlineData("GET", "/projects")]
    [InlineData("POST", "/projects")]
    [InlineData("GET", $"/projects/{NoSuchProject}")]
    [InlineData("PATCH", $"/projects/{NoSuchProject}")]
    [InlineData("PUT", $"/projects/{NoSuchProject}/retention")]
    [InlineData("DELETE", $"/projects/{NoSuchProject}")]
    public async Task Every_project_endpoint_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "api", retentionDays = 7 }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_project_is_created_listed_renamed_retained_and_deleted()
    {
        using var client = await SignedInAsync();

        var created = await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name = "  api  ", retentionDays = 14 },
            TestContext.Current.CancellationToken));

        // The name is stored as it would be, not as it was typed.
        Assert.Equal("api", created.Name);
        Assert.Equal(14, created.RetentionDays);

        // Creation mints no credential: the project receives nothing until a
        // token is issued, and the list is where that is visible.
        var listed = Assert.Single(await ReadAsync<ListedProjectBody[]>(
            await client.GetAsync("/projects", TestContext.Current.CancellationToken)));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(0, listed.IngestTokens);

        using var renamed = await client.PatchAsJsonAsync(
            $"/projects/{created.Id}",
            new { name = "orders-api" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var retained = await client.PutAsJsonAsync(
            $"/projects/{created.Id}/retention",
            new { retentionDays = 90 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, retained.StatusCode);

        // The identity survives both, which is what entries, tokens and queries
        // attach to.
        var read = await ReadAsync<ProjectBody>(await client.GetAsync(
            $"/projects/{created.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, read.Id);
        Assert.Equal("orders-api", read.Name);
        Assert.Equal(90, read.RetentionDays);

        using var deleted = await client.DeleteAsync(
            $"/projects/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // A project already gone is a second click or another tab, and not a
        // failure of anything.
        using var again = await client.DeleteAsync(
            $"/projects/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_second_project_by_that_name_is_refused_by_the_installation()
    {
        using var client = await SignedInAsync();

        var first = await CreateAsync(client, "api");

        // Two projects called `api` is a trap for the operator reaching for one
        // of them at three in the morning, and the unique index is what holds
        // it — a rename onto a taken name is the same conflict.
        using var second = await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var other = await CreateAsync(client, "web");
        using var renamed = await client.PatchAsJsonAsync(
            $"/projects/{other.Id}",
            new { name = "api" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        Assert.Equal(2, (await ReadAsync<ListedProjectBody[]>(await client.GetAsync(
            "/projects", TestContext.Current.CancellationToken))).Length);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Theory]
    [InlineData("", 7)]
    [InlineData("   ", 7)]
    [InlineData("api", 0)]
    [InlineData("api", RetentionWindow.MaximumDays + 1)]
    public async Task A_name_or_a_window_that_is_not_one_is_refused(string name, int retentionDays)
    {
        using var client = await SignedInAsync();

        using var response = await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_project_that_is_not_there_is_an_answer_rather_than_a_failure()
    {
        using var client = await SignedInAsync();

        // The rough edge #7 left: issuing reached the foreign key and the
        // DbUpdateException surfaced as a 500, when what happened is that the
        // operator named something that is gone.
        using var issued = await client.PostAsync(
            $"/projects/{NoSuchProject}/ingest-tokens",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, issued.StatusCode);

        foreach (var path in new[]
        {
            $"/projects/{NoSuchProject}",
            $"/projects/{NoSuchProject}/ingest-tokens",
        })
        {
            using var read = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }

        using var renamed = await client.PatchAsJsonAsync(
            $"/projects/{NoSuchProject}",
            new { name = "api" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);

        using var retained = await client.PutAsJsonAsync(
            $"/projects/{NoSuchProject}/retention",
            new { retentionDays = 7 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, retained.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_takes_its_tokens_and_its_visibility_at_once()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "api");

        var token = await ReadAsync<IssuedIngestTokenBody>(await client.PostAsync(
            $"/projects/{project.Id}/ingest-tokens",
            null,
            TestContext.Current.CancellationToken));

        using var deleted = await client.DeleteAsync(
            $"/projects/{project.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The project, its tokens and its visibility go at once (ADR 0019). The
        // token's row went with the cascade, so reading it back finds nothing —
        // which is the same lookup a delivery presenting it would miss on.
        using var readBack = await client.GetAsync(
            $"/ingest-tokens/{token.Id}/token", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, readBack.StatusCode);

        using var tokens = await client.GetAsync(
            $"/projects/{project.Id}/ingest-tokens", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, tokens.StatusCode);
    }

    private async Task<ProjectBody> CreateAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    /// <inheritdoc cref="TokenEndpointTests"/>
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
        using var client = _installation.CreateClient();

        var enrolment = await ReadAsync<Enrolment>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

        _secondFactorSecret = enrolment.SecondFactorSecret;

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                backupCode = enrolment.BackupCodes[0],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
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

    private sealed record ProjectBody(
        Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt);

    private sealed record ListedProjectBody(
        Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt, int IngestTokens);

    private sealed record IssuedIngestTokenBody(Guid Id, string Token, DateTimeOffset IssuedAt);
}
