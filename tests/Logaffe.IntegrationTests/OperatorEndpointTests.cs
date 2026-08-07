using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The session list and the operator's own credentials over HTTP, asked of an
/// installation that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// The property worth a running installation here is that <b>every one of these
/// is behind the operator's session</b> — a route that quietly stops requiring
/// one is an installation handing out backup codes to strangers — and that the
/// acts that end sessions take effect on the next request, which is a fact about
/// the authentication reading the row every time rather than about the act.
/// </para>
/// <para>
/// The cookie is carried by hand rather than by a cookie container, for the
/// reason <see cref="TokenEndpointTests"/> gives: it is issued <c>Secure</c> and
/// these requests are not.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class OperatorEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;
    private IReadOnlyList<string> _backupCodes = null!;

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
        Directory.Delete(_volume, recursive: true);
    }

    [Theory]
    [InlineData("GET", "/sessions")]
    [InlineData("DELETE", "/sessions/others")]
    [InlineData("DELETE", "/sessions/0195f0d4-0000-7000-8000-000000000000")]
    [InlineData("PUT", "/password")]
    [InlineData("POST", "/backup-codes")]
    [InlineData("POST", "/second-factor/enrolment")]
    [InlineData("PUT", "/second-factor")]
    public async Task Every_one_of_these_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { password = TheirPassword }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_list_says_which_row_is_this_browser_and_ending_the_others_leaves_it()
    {
        using var here = await SignedInAsync();
        using var elsewhere = await SignedInAsync();

        var listed = await ReadAsync<ListedSession[]>(await here.GetAsync(
            "/sessions", TestContext.Current.CancellationToken));

        // Three: the two signed in here, and the one the claim itself handed
        // out — a claim signs the operator in rather than congratulating them
        // and asking for the password again.
        Assert.Equal(3, listed.Length);

        // The list carries no secret and the cookie carries nothing but one, so
        // there is nothing the interface could compare: if the operator is to
        // know which row is theirs, the server has to say so.
        Assert.Single(listed, session => session.IsCurrent);
        Assert.All(listed, session => Assert.Equal("unknown", session.LastSeenFrom));

        using var ended = await here.DeleteAsync(
            "/sessions/others", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        // Every other, never every one — and the one that was ended stops
        // admitting on its very next request, because the row is read every
        // time and there is no cache in front of it.
        var afterwards = await ReadAsync<ListedSession[]>(await here.GetAsync(
            "/sessions", TestContext.Current.CancellationToken));
        Assert.True(Assert.Single(afterwards).IsCurrent);

        using var refused = await elsewhere.GetAsync(
            "/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Revoking_one_row_from_the_list_ends_that_browser_and_no_other()
    {
        using var here = await SignedInAsync();
        using var elsewhere = await SignedInAsync();

        // Which row the other browser is, asked of that browser: it is the one
        // the server marks as current for it.
        var listedThere = await ReadAsync<ListedSession[]>(await elsewhere.GetAsync(
            "/sessions", TestContext.Current.CancellationToken));
        var other = listedThere.Single(session => session.IsCurrent);

        using var revoked = await here.DeleteAsync(
            $"/sessions/{other.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await elsewhere.GetAsync("/sessions", TestContext.Current.CancellationToken))
                .StatusCode);

        // Already gone is a second click or another tab, not a failure.
        using var again = await here.DeleteAsync(
            $"/sessions/{other.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        using var still = await here.GetAsync(
            "/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task A_password_change_ends_every_other_session_and_the_new_one_signs_in()
    {
        using var here = await SignedInAsync();
        using var elsewhere = await SignedInAsync();

        using var wrong = await here.PutAsJsonAsync(
            "/password",
            new { currentPassword = "not their passphrase", newPassword = "a longer new one" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        using var changed = await here.PutAsJsonAsync(
            "/password",
            new { currentPassword = TheirPassword, newPassword = "the passphrase they moved to" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await elsewhere.GetAsync("/sessions", TestContext.Current.CancellationToken))
                .StatusCode);

        // The browser that made the change is still signed in, and the new
        // password is what the door takes now.
        using var stillHere = await here.GetAsync(
            "/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);

        using var withTheOldOne = await SignInAsync(_installation.CreateClient(), TheirPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldOne.StatusCode);

        using var withTheNewOne = await SignInAsync(
            _installation.CreateClient(), "the passphrase they moved to");
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
    }

    [Fact]
    public async Task A_fresh_sheet_replaces_the_codes_that_were_there()
    {
        using var here = await SignedInAsync();

        using var withoutThePassword = await here.PostAsJsonAsync(
            "/backup-codes",
            new { password = "not their passphrase" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, withoutThePassword.StatusCode);

        var fresh = await ReadAsync<BackupCodes>(await here.PostAsJsonAsync(
            "/backup-codes",
            new { password = TheirPassword },
            TestContext.Current.CancellationToken));

        Assert.Equal(10, fresh.Codes.Count);
        Assert.Empty(fresh.Codes.Intersect(_backupCodes));

        // Wholesale: the sheet the claim printed stops working the moment the
        // new one is shown (ADR 0032).
        using var withTheOldCode = await SignInAsync(
            _installation.CreateClient(), TheirPassword, backupCode: _backupCodes[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldCode.StatusCode);

        using var withTheNewCode = await SignInAsync(
            _installation.CreateClient(), TheirPassword, backupCode: fresh.Codes[0]);
        Assert.Equal(HttpStatusCode.OK, withTheNewCode.StatusCode);
    }

    [Fact]
    public async Task A_re_enrolment_moves_the_second_factor_to_the_app_just_enrolled()
    {
        using var here = await SignedInAsync();
        using var elsewhere = await SignedInAsync();

        var drawn = await ReadAsync<Enrolment>(await here.PostAsync(
            "/second-factor/enrolment", null, TestContext.Current.CancellationToken));

        // Nothing is stored by drawing it: the app in the operator's pocket
        // still works until the step that replaces the row.
        using var stillTheOldOne = await SignInAsync(
            _installation.CreateClient(), TheirPassword);
        Assert.Equal(HttpStatusCode.OK, stillTheOldOne.StatusCode);

        using var withoutTheNewCode = await here.PutAsJsonAsync(
            "/second-factor",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
                newSecondFactorCode = "000000",
                ticket = drawn.Ticket,
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, withoutTheNewCode.StatusCode);

        using var reEnrolled = await here.PutAsJsonAsync(
            "/second-factor",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
                newSecondFactorCode = Authenticator.CodeFor(drawn.SecondFactorSecret),
                ticket = drawn.Ticket,
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reEnrolled.StatusCode);

        // The row holds the new secret, the sheet shown with it is the
        // operator's, and every other session is over.
        using var withTheNewApp = await SignInAsync(
            _installation.CreateClient(),
            TheirPassword,
            code: Authenticator.CodeFor(drawn.SecondFactorSecret));
        Assert.Equal(HttpStatusCode.OK, withTheNewApp.StatusCode);

        using var withTheOldApp = await SignInAsync(_installation.CreateClient(), TheirPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldApp.StatusCode);

        using var withTheNewSheet = await SignInAsync(
            _installation.CreateClient(), TheirPassword, backupCode: drawn.BackupCodes[0]);
        Assert.Equal(HttpStatusCode.OK, withTheNewSheet.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await elsewhere.GetAsync("/sessions", TestContext.Current.CancellationToken))
                .StatusCode);
    }

    [Fact]
    public async Task An_enrolment_this_installation_did_not_seal_is_refused()
    {
        using var here = await SignedInAsync();

        using var refused = await here.PutAsJsonAsync(
            "/second-factor",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
                newSecondFactorCode = "123456",
                ticket = "not-a-ticket-this-installation-sealed",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // And the second factor it holds is untouched.
        using var unchanged = await SignInAsync(_installation.CreateClient(), TheirPassword);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
    }

    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await SignInAsync(client, TheirPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    private Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string password, string? code = null, string? backupCode = null) =>
        client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password,
                secondFactorCode = backupCode is null
                    ? code ?? Authenticator.CodeFor(_secondFactorSecret)
                    : null,
                backupCode,
            },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Puts the installation in the state a completed claim leaves it in, and
    /// keeps what the claim handed over: the enrolled secret and the sheet.
    /// </summary>
    private async Task ClaimAsync()
    {
        using var client = _installation.CreateClient();

        var enrolment = await ReadAsync<Enrolment>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

        _secondFactorSecret = enrolment.SecondFactorSecret;
        _backupCodes = enrolment.BackupCodes;

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
        string SecondFactorSecret,
        string EnrolmentUri,
        IReadOnlyList<string> BackupCodes,
        string Ticket);

    private sealed record ListedSession(
        Guid Id,
        string LastSeenFrom,
        DateTimeOffset StartedAt,
        DateTimeOffset LastUsedAt,
        DateTimeOffset ExpiresAt,
        bool IsCurrent);

    private sealed record BackupCodes(IReadOnlyList<string> Codes);
}
