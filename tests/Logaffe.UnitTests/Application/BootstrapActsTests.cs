using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The first administrator out of the configuration, and the one exchange that
/// turns the bootstrap token into a password (ADR 0054).
/// </summary>
public sealed class BootstrapActsTests
{
    private const string TheToken = "a-bootstrap-token-long-enough-to-be-one";
    private const string TheirPassword = "a passphrase they typed";

    private static readonly DateTimeOffset Started = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryIdentities _identities = new();
    private readonly InMemorySessions _sessions = new();
    private readonly StubPasswordHasher _hasher = new();
    private readonly StoppedClock _clock = new(Started);

    [Fact]
    public async Task An_installation_with_nobody_on_it_writes_the_first_administrator()
    {
        var outcome = await Bootstrap(Settings());

        Assert.Equal(BootstrapOutcome.Bootstrapped, outcome);

        var administrator = Assert.IsType<User>(Assert.Single(_identities.Stored));
        Assert.Equal("The Administrator", administrator.Name);
        Assert.Equal("somebody@example.com", administrator.Email);
        Assert.True(administrator.Administrator);

        // Invited and without a password: the exchange is what gives them one,
        // and that is what makes the token single-use without it being stored.
        Assert.Equal(UserState.Invited, administrator.State);
        Assert.Null(administrator.PasswordHash);
    }

    [Fact]
    public async Task The_second_start_ignores_the_configuration_whatever_it_says()
    {
        await Bootstrap(Settings());
        var writes = _identities.Writes;

        var outcome = await Bootstrap(
            Settings() with { Administrator = "Somebody Else", Email = "else@example.com" });

        Assert.Equal(BootstrapOutcome.AlreadyBootstrapped, outcome);
        Assert.Single(_identities.Stored);
        Assert.Equal(writes, _identities.Writes);
    }

    [Fact]
    public async Task An_installation_told_nothing_starts_and_says_so()
    {
        var outcome = await Bootstrap(new BootstrapSettings(null, null, null));

        // It starts: ingestion needs no identity, and an installation receiving
        // logs while its compose file is being written is doing something useful
        // (ADR 0054).
        Assert.Equal(BootstrapOutcome.NothingToBootstrapFrom, outcome);
        Assert.Empty(_identities.Stored);
    }

    [Fact]
    public async Task A_token_shorter_than_the_minimum_stops_the_start()
    {
        var refusal = await Assert.ThrowsAsync<BootstrapRefusedException>(
            () => Bootstrap(Settings() with { Token = "too-short" }));

        Assert.Contains(BootstrapSettings.TokenKey, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(_identities.Stored);
    }

    [Fact]
    public async Task An_address_that_is_not_one_stops_the_start()
    {
        await Assert.ThrowsAsync<BootstrapRefusedException>(
            () => Bootstrap(Settings() with { Email = "not an address" }));

        Assert.Empty(_identities.Stored);
    }

    [Fact]
    public async Task An_upgraded_installation_takes_its_address_from_the_configuration()
    {
        var carriedOver = CarriedOverOperator();

        var outcome = await Bootstrap(Settings());

        Assert.Equal(BootstrapOutcome.AddressAdopted, outcome);
        Assert.Equal("somebody@example.com", carriedOver.Email);
        Assert.Equal("The Administrator", carriedOver.Name);

        // Their password, second factor and backup codes are untouched: nobody
        // is locked out by the upgrade and nobody has to reset anything.
        Assert.Equal(StubPasswordHasher.HashOf(TheirPassword), carriedOver.PasswordHash);
        Assert.True(carriedOver.IsActive);
    }

    [Fact]
    public async Task An_upgraded_installation_with_no_address_configured_does_not_start()
    {
        CarriedOverOperator();

        var refusal = await Assert.ThrowsAsync<BootstrapRefusedException>(
            () => Bootstrap(new BootstrapSettings(null, null, null)));

        // The alternative is a placeholder address somebody signs in with once
        // and never changes.
        Assert.Contains(BootstrapSettings.EmailKey, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_exchange_sets_the_first_password_and_hands_out_a_session()
    {
        await Bootstrap(Settings());

        var exchange = await Exchange(TheToken, TheirPassword);

        Assert.Equal(ExchangeOutcome.Exchanged, exchange.Outcome);
        Assert.NotNull(exchange.Session);
        Assert.Equal([exchange.Session], _sessions.Stored);

        var administrator = Assert.IsType<User>(Assert.Single(_identities.Stored));
        Assert.Equal(StubPasswordHasher.HashOf(TheirPassword), administrator.PasswordHash);
        Assert.Equal(UserState.Active, administrator.State);
    }

    [Fact]
    public async Task The_exchange_happens_once_and_finds_nothing_afterwards()
    {
        await Bootstrap(Settings());
        await Exchange(TheToken, TheirPassword);

        var second = await Exchange(TheToken, "another passphrase entirely");

        // Nothing was stored to mark it spent: the administrator has a password
        // now, so there is no account left for a token to be exchanged against.
        Assert.Equal(ExchangeOutcome.NothingToExchange, second.Outcome);
        Assert.Single(_sessions.Stored);
    }

    [Fact]
    public async Task A_wrong_token_exchanges_nothing_and_never_reaches_the_hasher()
    {
        await Bootstrap(Settings());

        var exchange = await Exchange("not-the-bootstrap-token-this-one-names", TheirPassword);

        Assert.Equal(ExchangeOutcome.TokenRefused, exchange.Outcome);
        Assert.Equal(0, _hasher.Hashes);
        Assert.Empty(_sessions.Stored);
    }

    [Fact]
    public async Task A_password_below_the_minimum_is_refused_after_the_token_is_checked()
    {
        await Bootstrap(Settings());

        var exchange = await Exchange(TheToken, "short");

        Assert.Equal(ExchangeOutcome.PasswordNotOne, exchange.Outcome);
        Assert.Empty(_sessions.Stored);
    }

    [Fact]
    public async Task An_installation_that_names_no_token_offers_no_exchange()
    {
        var exchange = await Exchange(TheToken, TheirPassword, new BootstrapSettings(null, null, null));

        Assert.Equal(ExchangeOutcome.NothingToExchange, exchange.Outcome);
    }

    /// <summary>
    /// What the schema migration leaves behind on an installation that had an
    /// operator: the account, its password and its second factor, under an
    /// address nobody can deliver to.
    /// </summary>
    private User CarriedOverOperator()
    {
        var carriedOver = User.Bootstrap(
            "Operator",
            $"{Guid.CreateVersion7():N}{BootstrapTheInstallation.PendingAddressDomain}",
            Started.AddYears(-1));
        carriedOver.ActivateWith(StubPasswordHasher.HashOf(TheirPassword));

        return _identities.Seed(carriedOver);
    }

    private static BootstrapSettings Settings() =>
        new("The Administrator", "somebody@example.com", TheToken);

    private Task<BootstrapOutcome> Bootstrap(BootstrapSettings settings) =>
        new BootstrapTheInstallation(_identities, _clock)
            .ExecuteAsync(settings, TestContext.Current.CancellationToken);

    private Task<BootstrapExchange> Exchange(
        string? token, string? password, BootstrapSettings? settings = null) =>
        new ExchangeBootstrapToken(
                _identities, _sessions, _hasher, settings ?? Settings(), _clock)
            .ExecuteAsync(token, password, "203.0.113.7", TestContext.Current.CancellationToken);
}
