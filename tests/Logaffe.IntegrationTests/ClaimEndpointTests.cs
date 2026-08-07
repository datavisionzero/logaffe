using System.Net;
using System.Net.Http.Json;
using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The whole reachable surface of an installation nobody owns, asked of one that
/// is actually running.
/// </summary>
/// <remarks>
/// <para>
/// The properties worth a running host here are the ones no registration can
/// vouch for: that an unclaimed installation admits <b>nothing but the claim</b>,
/// that the claim really stores nothing until its last step, and that the
/// session it hands out is the same session everything else stands behind.
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

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Fact]
    public async Task The_first_run_opens_a_window_and_the_installation_says_it_is_unclaimed()
    {
        using var client = _installation.CreateClient();

        var state = await ReadAsync<ClaimStateBody>(await client.GetAsync(
            "/claim", TestContext.Current.CancellationToken));

        Assert.False(state.IsClaimed);
        Assert.True(state.WindowIsOpen);
        Assert.NotNull(state.ClosesAt);

        // Thirty minutes from the run that created the schema (ADR 0034), and
        // the row is what says so.
        await using var context = ContextFor(_connectionString);
        var window = await context.ClaimWindows.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(window.ClosesAt, state.ClosesAt);
        Assert.Equal(ClaimWindow.Duration, window.ClosesAt - window.OpenedAt);
    }

    [Theory]
    [InlineData("GET", "/agent-tokens")]
    [InlineData("POST", "/agent-tokens")]
    [InlineData("POST", "/sign-out")]
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
    public async Task Drawing_an_enrolment_writes_nothing_at_all()
    {
        using var client = _installation.CreateClient();

        var enrolment = await EnrolAsync(client);

        Assert.Equal(BackupCode.SetSize, enrolment.BackupCodes.Count);
        Assert.StartsWith("otpauth://totp/", enrolment.EnrolmentUri);
        Assert.NotEmpty(enrolment.Ticket);

        // Every step before the last is a form with no effect (ADR 0014), and
        // that is a claim about the database rather than about the response.
        await using var context = ContextFor(_connectionString);
        Assert.Empty(await context.Operators.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.BackupCodes.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_claim_hands_out_the_session_every_other_surface_stands_behind()
    {
        using var client = _installation.CreateClient();
        var enrolment = await EnrolAsync(client);

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
        Assert.False(state.WindowIsOpen);
    }

    [Fact]
    public async Task A_backup_code_from_the_sheet_signs_the_operator_in_afterwards()
    {
        using var client = _installation.CreateClient();
        var enrolment = await ClaimedAsync(client);

        // The sheet is what stands in when the phone is gone, so the codes it
        // showed have to be the codes the rows hold — and there is no second
        // chance to find out (ADR 0035).
        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new { password = TheirPassword, backupCode = enrolment.BackupCodes[7] },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signedIn = await response.Content.ReadFromJsonAsync<SignInBody>(
            TestContext.Current.CancellationToken);
        Assert.Equal(BackupCode.SetSize - 1, signedIn!.BackupCodesRemaining);
    }

    [Fact]
    public async Task A_second_claim_is_refused_and_so_is_a_second_enrolment()
    {
        using var client = _installation.CreateClient();
        var enrolment = await ClaimedAsync(client);

        // There is no re-claim while claimed, and the only route back is the
        // host (ADR 0013). The loser of a race meets this at their last step,
        // which is the price of holding nothing (ADR 0014).
        using var again = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                backupCode = enrolment.BackupCodes[1],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        using var enrolling = await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, enrolling.StatusCode);
    }

    [Fact]
    public async Task A_step_that_fails_names_the_field_and_leaves_the_installation_unclaimed()
    {
        using var client = _installation.CreateClient();
        var enrolment = await EnrolAsync(client);

        var code = Authenticator.CodeFor(enrolment.SecondFactorSecret);

        var wrong = new (string Field, object Body)[]
        {
            ("password", new
            {
                password = "short",
                ticket = enrolment.Ticket,
                secondFactorCode = code,
                backupCode = enrolment.BackupCodes[0],
            }),
            ("ticket", new
            {
                password = TheirPassword,
                ticket = "not a ticket this installation sealed",
                secondFactorCode = code,
                backupCode = enrolment.BackupCodes[0],
            }),
            ("secondFactorCode", new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = "000000",
                backupCode = enrolment.BackupCodes[0],
            }),
            ("backupCode", new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = code,
                backupCode = "aaaa-bbbb-cccc-dddd",
            }),
        };

        foreach (var (field, body) in wrong)
        {
            using var response = await client.PostAsJsonAsync(
                "/claim", body, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // The operator is filling in a form and has to be told which box is
            // wrong. There is nothing to give away: the door is open by design.
            Assert.Contains(
                $"\"{field}\"",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        await using var context = ContextFor(_connectionString);
        Assert.Empty(await context.Operators.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Walks the whole flow and leaves the client carrying the session it
    /// handed out.
    /// </summary>
    private async Task<EnrolmentBody> ClaimedAsync(HttpClient client)
    {
        var enrolment = await EnrolAsync(client);

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

        return enrolment;
    }

    private static async Task<EnrolmentBody> EnrolAsync(HttpClient client) =>
        await ReadAsync<EnrolmentBody>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

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
        bool IsClaimed, bool WindowIsOpen, DateTimeOffset? ClosesAt);

    private sealed record EnrolmentBody(
        string SecondFactorSecret,
        string EnrolmentUri,
        IReadOnlyList<string> BackupCodes,
        string Ticket);

    private sealed record SignInBody(int? BackupCodesRemaining);
}
