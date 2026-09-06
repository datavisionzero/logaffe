using System.Net;
using System.Net.Http.Json;
using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The first administrator out of the configuration, and the one exchange that
/// turns the bootstrap token into a password, asked of an installation that is
/// actually running (ADR 0054).
/// </summary>
/// <remarks>
/// <para>
/// The properties worth a running host here are the ones no registration can
/// vouch for: that the administrator named in the configuration is written on
/// the start where there is nobody, that they cannot sign in until the token has
/// been exchanged, that the exchange happens once, and that the session it hands
/// out is the same session everything else stands behind.
/// </para>
/// <para>
/// The cookie is carried by hand rather than by a cookie container, for the
/// reason <see cref="TokenEndpointTests"/> gives: it is issued <c>Secure</c> and
/// these requests are not.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class BootstrapEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = InstallationVolume.Create(nameof(BootstrapEndpointTests));

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        // Said rather than left to what another class set, because this is the
        // one class the three keys are the subject of.
        ABootstrappedInstallation.Configure();

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
    public async Task The_first_start_writes_the_administrator_the_configuration_names()
    {
        await using var context = ContextFor(_connectionString);
        var administrator = Assert.Single(
            await new Identities(context).ListUsersAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ABootstrappedInstallation.TheirName, administrator.Name);
        Assert.Equal(ABootstrappedInstallation.TheirAddress, administrator.Email);
        Assert.True(administrator.Administrator);

        // Invited and without a password until the token is exchanged, which is
        // what makes the exchange single-use without the token being stored.
        Assert.Equal(UserState.Invited, administrator.State);
        Assert.Null(administrator.PasswordHash);
    }

    [Fact]
    public async Task Nobody_signs_in_before_the_token_has_been_exchanged()
    {
        using var client = _installation.CreateClient();

        using var refused = await client.PostAsJsonAsync(
            "/sign-in",
            new { email = ABootstrappedInstallation.TheirAddress, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/projects")]
    [InlineData("GET", "/sessions")]
    [InlineData("GET", "/second-factor")]
    public async Task An_installation_nobody_has_signed_into_admits_nothing(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_exchange_hands_out_the_session_every_other_surface_stands_behind()
    {
        using var client = _installation.CreateClient();

        using var exchanged = await client.PostAsJsonAsync(
            "/bootstrap",
            new { token = ABootstrappedInstallation.TheToken, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, exchanged.StatusCode);

        var cookie = Assert.Single(exchanged.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        using var projects = await client.GetAsync(
            "/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
    }

    [Fact]
    public async Task The_exchange_establishes_a_password_and_no_second_factor()
    {
        using var client = await ExchangedClient();

        var state = await client.GetFromJsonAsync<SecondFactorBody>(
            "/second-factor", TestContext.Current.CancellationToken);

        // The second factor is each user's to enrol afterwards (ADR 0041), so an
        // account that has none is an ordinary account rather than a half-built
        // one.
        Assert.NotNull(state);
        Assert.False(state.IsEnrolled);
        Assert.Null(state.EnrolledAt);
    }

    [Fact]
    public async Task The_address_signs_in_once_the_exchange_has_happened()
    {
        using var setUp = await ExchangedClient();
        using var client = _installation.CreateClient();

        using var signedIn = await client.PostAsJsonAsync(
            "/sign-in",
            new { email = "ADMINISTRATOR@Example.com", password = TheirPassword },
            TestContext.Current.CancellationToken);

        // However it was typed: the address is folded before it is looked up
        // (ADR 0052).
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_second_exchange_finds_nothing()
    {
        using var setUp = await ExchangedClient();
        using var client = _installation.CreateClient();

        using var second = await client.PostAsJsonAsync(
            "/bootstrap",
            new
            {
                token = ABootstrappedInstallation.TheToken,
                password = "an entirely different passphrase",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("not-the-token-this-installation-names-at-all", TheirPassword, "token")]
    [InlineData(ABootstrappedInstallation.TheToken, "short", "password")]
    public async Task A_field_that_is_wrong_names_itself_and_writes_nothing(
        string token, string password, string field)
    {
        using var client = _installation.CreateClient();

        using var refused = await client.PostAsJsonAsync(
            "/bootstrap", new { token, password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var problem = await refused.Content.ReadFromJsonAsync<ValidationProblem>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey(field));

        // Whoever is here is setting up their own installation with the compose
        // file open in another window, and nothing was written meanwhile.
        await using var context = ContextFor(_connectionString);
        var administrator = Assert.Single(
            await new Identities(context).ListUsersAsync(TestContext.Current.CancellationToken));
        Assert.Null(administrator.PasswordHash);
    }

    private async Task<HttpClient> ExchangedClient()
    {
        var client = _installation.CreateClient();

        using var exchanged = await client.PostAsJsonAsync(
            "/bootstrap",
            new { token = ABootstrappedInstallation.TheToken, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, exchanged.StatusCode);

        var cookie = Assert.Single(exchanged.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private sealed record SecondFactorBody(bool IsEnrolled, DateTimeOffset? EnrolledAt);

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
