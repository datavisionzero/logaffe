using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Mail;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The three links, against a real Postgres and a real SMTP conversation
/// (ADR 0053).
/// </summary>
/// <remarks>
/// What needs both is what no substitute can vouch for: that a newer secret of
/// one purpose stops the previous one working — held by a partial unique index —
/// and that what a mail server takes is the two bodies that were written, sent
/// to the address they were meant for.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class OneTimeSecretTests(PostgresFixture postgres, MailpitFixture mailpit)
    : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        await using var context = ContextFor(_connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task An_invitation_writes_an_invited_account_and_sends_one_link()
    {
        Assert.Equal(InviteOutcome.Invited, await InviteAsync("newcomer@example.com"));

        await using var reader = ContextFor(_connectionString);
        var invited = Assert.Single(
            await new Identities(reader).ListUsersAsync(TestContext.Current.CancellationToken));

        Assert.Equal("newcomer@example.com", invited.Email);
        Assert.Equal(UserState.Invited, invited.State);
        Assert.Null(invited.PasswordHash);

        var message = await mailpit.OnlyMessageAsync(TestContext.Current.CancellationToken);

        Assert.Equal("newcomer@example.com", Assert.Single(message.To).Address);
        Assert.Contains("invited", message.Subject, StringComparison.OrdinalIgnoreCase);

        // Both bodies, written separately, and the link in each of them
        // (ADR 0053).
        Assert.Contains("http://logs.example.com/invitation?secret=", message.Text,
            StringComparison.Ordinal);
        Assert.Contains("http://logs.example.com/invitation?secret=", message.Html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_address_is_refused_and_nothing_is_sent()
    {
        await InviteAsync("newcomer@example.com");
        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InviteOutcome.AddressTaken, await InviteAsync("NEWCOMER@Example.com"));
        Assert.Equal(0, await mailpit.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Redeeming_an_invitation_sets_the_first_password_and_activates()
    {
        await InviteAsync("newcomer@example.com");
        var secret = await SecretFromTheMessageAsync("invitation");

        Assert.Equal(
            RedeemOutcome.Redeemed,
            await RedeemAsync(OneTimeSecretPurpose.Invitation, secret, TheirPassword));

        await using var reader = ContextFor(_connectionString);
        var arrived = Assert.Single(
            await new Identities(reader).ListUsersAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UserState.Active, arrived.State);
        Assert.NotNull(arrived.PasswordHash);

        // Once, and the second attempt finds it spent — which is what stops a
        // link found in a mailbox later from setting a password.
        Assert.Equal(
            RedeemOutcome.LinkRefused,
            await RedeemAsync(OneTimeSecretPurpose.Invitation, secret, "another passphrase"));
    }

    [Fact]
    public async Task A_fresh_invitation_stops_the_previous_link_working()
    {
        await InviteAsync("newcomer@example.com");
        var first = await SecretFromTheMessageAsync("invitation");

        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);

        await using (var context = ContextFor(_connectionString))
        {
            var invited = Assert.Single(await new Identities(context)
                .ListUsersAsync(TestContext.Current.CancellationToken));

            Assert.Equal(
                InviteOutcome.Invited,
                await new ReinviteAUser(
                        new Identities(context),
                        new OneTimeSecrets(context),
                        Mail(),
                        Templates(),
                        At(Now.AddMinutes(1)))
                    .ExecuteAsync(invited.Id, TestContext.Current.CancellationToken));
        }

        var second = await SecretFromTheMessageAsync("invitation");
        Assert.NotEqual(first, second);

        // One live link per purpose, held by the partial unique index rather
        // than by the act that wrote it (ADR 0053).
        Assert.Equal(
            RedeemOutcome.LinkRefused,
            await RedeemAsync(OneTimeSecretPurpose.Invitation, first, TheirPassword));
        Assert.Equal(
            RedeemOutcome.Redeemed,
            await RedeemAsync(OneTimeSecretPurpose.Invitation, second, TheirPassword));
    }

    [Fact]
    public async Task A_recovery_says_nothing_about_whether_the_address_exists()
    {
        var arrived = await AnArrivedUserAsync();
        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);

        // An address nobody holds: the act answers exactly as it does for one
        // somebody holds, and sends nothing.
        await BeginRecoveryAsync("nobody@example.com");
        Assert.Equal(0, await mailpit.CountAsync(TestContext.Current.CancellationToken));

        await BeginRecoveryAsync(arrived.Email);
        var message = await mailpit.OnlyMessageAsync(TestContext.Current.CancellationToken);
        Assert.Equal(arrived.Email, Assert.Single(message.To).Address);
    }

    [Fact]
    public async Task Redeeming_a_recovery_ends_every_session_that_account_had()
    {
        var arrived = await AnArrivedUserAsync();

        await using (var context = ContextFor(_connectionString))
        {
            await new Sessions(context).AddAsync(
                Session.Start(arrived.Id, SessionSecret.Mint(), "203.0.113.7", Now),
                TestContext.Current.CancellationToken);
        }

        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);
        await BeginRecoveryAsync(arrived.Email);

        Assert.Equal(
            RedeemOutcome.Redeemed,
            await RedeemAsync(
                OneTimeSecretPurpose.PasswordRecovery,
                await SecretFromTheMessageAsync("recovery"),
                "an entirely different passphrase"));

        // Every session and not every other: whoever redeemed this is not
        // signed in, so one that survived would be the one this ends.
        await using var reader = ContextFor(_connectionString);
        Assert.Empty(await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_change_of_address_goes_to_the_new_one_and_changes_nothing_until_used()
    {
        var arrived = await AnArrivedUserAsync();
        await mailpit.ForgetAsync(TestContext.Current.CancellationToken);

        await using (var context = ContextFor(_connectionString))
        {
            Assert.Equal(
                ChangeAddressOutcome.Sent,
                await Changing(context).ExecuteAsync(
                    arrived,
                    TheirPassword,
                    "moved@example.com",
                    TestContext.Current.CancellationToken));
        }

        var message = await mailpit.OnlyMessageAsync(TestContext.Current.CancellationToken);

        // To the address being moved to, because what it proves is that
        // somebody reads mail there.
        Assert.Equal("moved@example.com", Assert.Single(message.To).Address);

        await using (var reader = ContextFor(_connectionString))
        {
            // Until it is redeemed the old address is still the one that signs
            // in, so a change nobody confirms costs nothing.
            Assert.NotNull(await new Identities(reader).FindByEmailAsync(
                arrived.Email, TestContext.Current.CancellationToken));
            Assert.Null(await new Identities(reader).FindByEmailAsync(
                "moved@example.com", TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            RedeemOutcome.Redeemed,
            await RedeemAsync(
                OneTimeSecretPurpose.EmailChange,
                await SecretFromTheMessageAsync("address"),
                password: null));

        await using var after = ContextFor(_connectionString);
        Assert.NotNull(await new Identities(after).FindByEmailAsync(
            "moved@example.com", TestContext.Current.CancellationToken));
        Assert.Null(await new Identities(after).FindByEmailAsync(
            arrived.Email, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_address_somebody_is_already_moving_to_is_reserved()
    {
        var first = await AnArrivedUserAsync();
        await InviteAsync("second@example.com");

        await using var context = ContextFor(_connectionString);
        await Changing(context).ExecuteAsync(
            first, TheirPassword, "moved@example.com", TestContext.Current.CancellationToken);

        var second = await new Identities(context).FindByEmailAsync(
            "second@example.com", TestContext.Current.CancellationToken);
        second!.ActivateWith(new Argon2idPasswordHasher().Hash(Password.Create(TheirPassword)));
        await new Identities(context).RecordAsync(second, TestContext.Current.CancellationToken);

        // The users' unique index catches an address that is held; this catches
        // one that is spoken for, which nothing else would.
        Assert.Equal(
            ChangeAddressOutcome.AddressTaken,
            await Changing(context).ExecuteAsync(
                second, TheirPassword, "moved@example.com", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_link_that_was_never_one_is_the_same_refusal_as_a_spent_one() =>
        Assert.Equal(
            RedeemOutcome.LinkRefused,
            await RedeemAsync(OneTimeSecretPurpose.Invitation, "not-a-secret", TheirPassword));

    private async Task<InviteOutcome> InviteAsync(string email)
    {
        await using var context = ContextFor(_connectionString);

        return await new InviteAUser(
                new Identities(context),
                new OneTimeSecrets(context),
                Mail(),
                Templates(), Recording.Nobody(),
                At(Now))
            .ExecuteAsync(
                "Newcomer", email, administrator: false, TestContext.Current.CancellationToken);
    }

    private async Task BeginRecoveryAsync(string email)
    {
        await using var context = ContextFor(_connectionString);

        await new BeginRecovery(
                new Identities(context),
                new OneTimeSecrets(context),
                Mail(),
                Templates(),
                At(Now))
            .ExecuteAsync(email, TestContext.Current.CancellationToken);
    }

    private async Task<RedeemOutcome> RedeemAsync(
        OneTimeSecretPurpose purpose, string? secret, string? password)
    {
        await using var context = ContextFor(_connectionString);

        return await new RedeemALink(
                new Identities(context),
                new OneTimeSecrets(context),
                new Sessions(context),
                new Argon2idPasswordHasher(),
                At(Now.AddMinutes(2)))
            .ExecuteAsync(purpose, secret, password, TestContext.Current.CancellationToken);
    }

    private ChangeAddress Changing(LogaffeDbContext context) =>
        new(
            new Identities(context),
            new OneTimeSecrets(context),
            Mail(),
            Templates(),
            new Argon2idPasswordHasher(),
            At(Now));

    /// <summary>An account that was invited and has set its password.</summary>
    private async Task<User> AnArrivedUserAsync()
    {
        await InviteAsync("newcomer@example.com");

        Assert.Equal(
            RedeemOutcome.Redeemed,
            await RedeemAsync(
                OneTimeSecretPurpose.Invitation,
                await SecretFromTheMessageAsync("invitation"),
                TheirPassword));

        await using var context = ContextFor(_connectionString);

        return (await new Identities(context)
            .ListUsersAsync(TestContext.Current.CancellationToken))[0];
    }

    /// <summary>
    /// The secret out of the link, which is the only place it exists: the row
    /// holds a hash and nothing else (ADR 0053).
    /// </summary>
    private async Task<string> SecretFromTheMessageAsync(string path)
    {
        var message = await mailpit.OnlyMessageAsync(TestContext.Current.CancellationToken);
        var marker = $"http://logs.example.com/{path}?secret=";
        var start = message.Text.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"The message carries no {path} link.");

        var value = message.Text[(start + marker.Length)..];
        var end = value.IndexOfAny([' ', '\r', '\n']);

        return Uri.UnescapeDataString(end < 0 ? value : value[..end]);
    }

    private SmtpMail Mail() =>
        new(mailpit.Settings(), NullLogger<SmtpMail>.Instance);

    private MailTemplates Templates() => new(mailpit.Settings());

    private static TimeProvider At(DateTimeOffset now) => new FixedClock(now);

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>
    /// A clock that does not move, so that a link's lifetime is arithmetic
    /// rather than a race with the test.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
