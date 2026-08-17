using System.Net;
using System.Net.Http.Json;
using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The whole reachable surface of an installation nobody owns, asked of one that
/// is actually running — guarded by the claim secret it drew for itself, which is
/// what an installation does by default (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// The properties worth a running host here are the ones no registration can
/// vouch for: that an unclaimed installation admits <b>nothing but the claim</b>,
/// that the secret it wrote to its volume is the secret that opens it, that the
/// claim really stores nothing until it succeeds, and that the session it hands
/// out is the same session everything else stands behind.
/// </para>
/// <para>
/// The cookie is carried by hand rather than by a cookie container, for the
/// reason <see cref="TokenEndpointTests"/> gives: it is issued <c>Secure</c> and
/// these requests are not.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ClaimEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = InstallationVolume.Create(nameof(ClaimEndpointTests));

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        // Said rather than left to the default, so that this class does not
        // depend on what another one set the variable to.
        Environment.SetEnvironmentVariable("Logaffe__Claim__Mode", "secret");
        Environment.SetEnvironmentVariable("Logaffe__Claim__Secret", null);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Fact]
    public async Task The_first_run_draws_a_secret_and_the_installation_says_it_wants_one()
    {
        using var client = _installation.CreateClient();

        var state = await ReadAsync<ClaimStateBody>(await client.GetAsync(
            "/claim", TestContext.Current.CancellationToken));

        Assert.False(state.IsClaimed);
        Assert.True(state.CanBeClaimed);
        Assert.True(state.NeedsSecret);

        // A door that is locked does not need a clock, so there is nothing to
        // count down to (ADR 0040).
        Assert.Null(state.ClosesAt);

        // The secret is on the volume for the operator to read, and what the row
        // holds is its hash.
        var drawn = TheSecretOnTheVolume();
        Assert.Equal(ClaimSecret.DrawnLength, drawn.Length);

        await using var context = ContextFor(_connectionString);
        var guard = await context.ClaimGuards.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(guard.DrawnSecretHash);
        Assert.DoesNotContain(drawn, Convert.ToHexString(guard.DrawnSecretHash));
    }

    [Theory]
    [InlineData("GET", "/agent-tokens")]
    [InlineData("POST", "/agent-tokens")]
    [InlineData("POST", "/sign-out")]
    [InlineData("POST", "/second-factor/enrolment")]
    public async Task An_unclaimed_installation_admits_nothing_but_the_claim(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_claim_hands_out_the_session_every_other_surface_stands_behind()
    {
        using var client = _installation.CreateClient();

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new { password = TheirPassword, secret = TheSecretOnTheVolume() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);

        // The claim signs them in rather than sending them to a form for the
        // password they chose four seconds ago.
        var cookie = Assert.Single(claimed.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        using var tokens = await client.GetAsync(
            "/agent-tokens", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode);

        // And the installation now says what it is.
        var state = await ReadAsync<ClaimStateBody>(await client.GetAsync(
            "/claim", TestContext.Current.CancellationToken));
        Assert.True(state.IsClaimed);
        Assert.False(state.CanBeClaimed);

        // The file the secret was handed over in is removed by the claim: what
        // is left otherwise is a credential for a door that no longer opens.
        Assert.False(File.Exists(Path.Combine(_volume, "claim-secret.txt")));
    }

    [Fact]
    public async Task The_claim_establishes_a_password_and_no_second_factor()
    {
        using var client = _installation.CreateClient();
        await ClaimedAsync(client);

        // The second factor is the operator's to enrol afterwards (ADR 0041), so
        // the account this leaves behind holds neither it nor a sheet of codes —
        // and the sign-in asks for neither.
        await using var context = ContextFor(_connectionString);
        var stored = await context.Operators.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(stored.EncryptedSecondFactorSecret);
        Assert.Null(stored.SecondFactorEnrolledAt);
        Assert.Empty(await context.BackupCodes.ToListAsync(TestContext.Current.CancellationToken));

        using var signedIn = await client.PostAsJsonAsync(
            "/sign-in",
            new { password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_second_claim_is_refused()
    {
        using var client = _installation.CreateClient();
        var secret = await ClaimedAsync(client);

        // There is no re-claim while claimed, and the only route back is the
        // host (ADR 0013). The loser of a race meets this at the one step there
        // is, which is the price of holding nothing (ADR 0014).
        using var again = await client.PostAsJsonAsync(
            "/claim",
            new { password = TheirPassword, secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_field_that_is_wrong_names_itself_and_leaves_the_installation_unclaimed()
    {
        using var client = _installation.CreateClient();
        var secret = TheSecretOnTheVolume();

        var wrong = new (string Field, object Body)[]
        {
            ("password", new { password = "short", secret }),
            ("secret", new { password = TheirPassword, secret = "not the one" }),
        };

        foreach (var (field, body) in wrong)
        {
            using var response = await client.PostAsJsonAsync(
                "/claim", body, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // The operator is filling in a form and has to be told which box is
            // wrong. What this says about the secret is only whether the one
            // presented was right.
            Assert.Contains(
                $"\"{field}\"",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        await using var context = ContextFor(_connectionString);
        Assert.Empty(await context.Operators.ToListAsync(TestContext.Current.CancellationToken));

        // And the secret is still there to be presented, because nothing
        // happened.
        Assert.Equal(secret, TheSecretOnTheVolume());
    }

    /// <summary>
    /// Claims the installation and leaves the client carrying the session it
    /// handed out.
    /// </summary>
    private async Task<string> ClaimedAsync(HttpClient client)
    {
        var secret = TheSecretOnTheVolume();

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new { password = TheirPassword, secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);

        var cookie = Assert.Single(claimed.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return secret;
    }

    /// <summary>
    /// The secret the way the operator gets it: read off the file the
    /// installation wrote it to on its first start.
    /// </summary>
    private string TheSecretOnTheVolume() =>
        File.ReadAllText(Path.Combine(_volume, "claim-secret.txt")).Trim();

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

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private sealed record ClaimStateBody(
        bool IsClaimed, bool CanBeClaimed, bool NeedsSecret, DateTimeOffset? ClosesAt);
}

/// <summary>
/// The other guard: no secret, and anyone who reaches the installation may claim
/// it for thirty minutes after its first run (ADR 0040).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ClaimWindowEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = InstallationVolume.Create(nameof(ClaimWindowEndpointTests));

    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres", await postgres.CreateDatabaseAsync());
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);
        Environment.SetEnvironmentVariable("Logaffe__Claim__Mode", "window");
        Environment.SetEnvironmentVariable("Logaffe__Claim__Secret", null);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
        Environment.SetEnvironmentVariable("Logaffe__Claim__Mode", null);
    }

    [Fact]
    public async Task The_window_counts_down_and_nothing_is_drawn_to_present()
    {
        using var client = _installation.CreateClient();

        using var response = await client.GetAsync(
            "/claim", TestContext.Current.CancellationToken);
        var state = (await response.Content.ReadFromJsonAsync<ClaimStateBody>(
            TestContext.Current.CancellationToken))!;

        Assert.False(state.IsClaimed);
        Assert.True(state.CanBeClaimed);
        Assert.False(state.NeedsSecret);
        Assert.NotNull(state.ClosesAt);

        // Thirty minutes from the run that created the schema (ADR 0034).
        Assert.True(state.ClosesAt > DateTimeOffset.UtcNow);
        Assert.True(state.ClosesAt <= DateTimeOffset.UtcNow + ClaimGuard.WindowDuration);

        // No secret was drawn, so there is no file holding one.
        Assert.False(File.Exists(Path.Combine(_volume, "claim-secret.txt")));
    }

    [Fact]
    public async Task A_claim_needs_no_secret_while_the_window_is_open()
    {
        using var client = _installation.CreateClient();

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new { password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
    }

    private sealed record ClaimStateBody(
        bool IsClaimed, bool CanBeClaimed, bool NeedsSecret, DateTimeOffset? ClosesAt);
}
