using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.UnitTests.Application;

public sealed class SignInTests
{
    private const string TheirPassword = "a passphrase they typed";
    private const string TheirAddress = "somebody@example.com";
    private const string TheCode = "314159";

    private static readonly DateTimeOffset Claimed = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_password_and_the_second_factor_start_a_session()
    {
        var installation = Signed_up_installation();

        var attempt = await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, "203.0.113.7", TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Session);
        var signedIn = attempt.Session;
        Assert.Equal([signedIn.Session], installation.Sessions.Stored);
        Assert.Equal("203.0.113.7", signedIn.Session.LastSeenFrom);

        // Nothing was spent to get in, so there is nothing to say about the set.
        Assert.Null(signedIn.BackupCodesRemaining);
    }

    [Fact]
    public async Task The_session_holds_a_hash_and_the_secret_is_handed_over_once()
    {
        var installation = Signed_up_installation();

        var attempt = await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Session);
        var signedIn = attempt.Session;

        // ADR 0032: a session secret is stored as a fast hash and is not
        // readable back. The row must not hold the value the browser holds.
        Assert.NotEqual(
            Encoding.UTF8.GetBytes(signedIn.Secret.Text), signedIn.Session.SecretHash);
        Assert.Equal(SHA256.HashSizeInBytes, signedIn.Session.SecretHash.Length);
        Assert.True(signedIn.Session.Matches(signedIn.Secret));
    }

    [Fact]
    public async Task The_second_factor_is_checked_against_the_secret_the_key_opens()
    {
        var installation = Signed_up_installation();

        await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken);

        // The row holds it sealed (ADR 0057); what the arithmetic gets is what
        // the key on the host volume made of it again.
        Assert.Equal(StubSecondFactor.Secret, installation.SecondFactor.CheckedAgainst);
    }

    [Fact]
    public async Task A_wrong_password_admits_nothing_and_writes_nothing()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            "some other passphrase", TheCode, null, null, TestContext.Current.CancellationToken)).Session);

        // ADR 0017: with exactly one account a lockout is a weapon pointed at
        // its owner, so there is no counter, no flag, and nothing at all for a
        // failed attempt to leave behind.
        Assert.Empty(installation.Sessions.Stored);
        Assert.Equal(0, installation.Identities.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
    }

    [Fact]
    public async Task The_right_password_with_the_wrong_code_admits_nothing()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, "000000", null, null, TestContext.Current.CancellationToken)).Session);
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task A_password_below_the_minimum_is_wrong_rather_than_malformed()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            "short", TheCode, null, null, TestContext.Current.CancellationToken)).Session);

        // The minimum is a rule about choosing a password (ADR 0042). A short
        // one presented here is simply not the one, and it is the hasher that
        // says so — because refusing it for its length is what would lock out an
        // operator whose password was long enough when they set it.
        Assert.Equal(1, installation.Hasher.Verifications);
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task A_password_that_would_be_a_denial_of_service_never_reaches_the_hasher()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            new string('x', Password.MaximumLength + 1),
            TheCode,
            null,
            null,
            TestContext.Current.CancellationToken)).Session);

        // Hashing is deliberately slow and this surface is public, so a megabyte
        // of input is refused before PBKDF2 is asked to spend anything on it.
        // That bound is the one shape a presented password still has.
        Assert.Equal(0, installation.Hasher.Verifications);
    }

    [Fact]
    public async Task A_failed_attempt_counts_and_a_successful_one_clears_the_account()
    {
        var installation = Signed_up_installation();

        await installation.SignIn.ExecuteAsync(
            TheirAddress,
            "some other passphrase", TheCode, null, "203.0.113.7",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, installation.Throttle.Failures);

        await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, "203.0.113.7", TestContext.Current.CancellationToken);

        // The account's window and not the source's: somebody who mistyped and
        // then got it right is back to zero, and a source that has been trying
        // twenty addresses has proven nothing (ADR 0056).
        Assert.Equal(1, installation.Throttle.Successes);
        Assert.Equal(1, installation.Throttle.FailuresFrom("203.0.113.7"));
    }

    [Fact]
    public async Task An_exhausted_window_is_answered_before_anything_is_hashed()
    {
        var installation = Signed_up_installation();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            await installation.SignIn.ExecuteAsync(
                TheirAddress,
                "some other passphrase", TheCode, null, "203.0.113.7",
                TestContext.Current.CancellationToken);
        }

        var hashes = installation.Hasher.Verifications;

        var refused = await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, "203.0.113.7", TestContext.Current.CancellationToken);

        // Told apart from a refusal, because the caller answers it differently —
        // and it costs nothing, because a request the throttle will not answer
        // must not buy an attacker a hash.
        Assert.True(refused.Throttled);
        Assert.Null(refused.Session);
        Assert.Equal(hashes, installation.Hasher.Verifications);
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task An_address_nobody_holds_fills_its_own_window()
    {
        var installation = Signed_up_installation();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            await installation.SignIn.ExecuteAsync(
                "nobody@example.com",
                TheirPassword, TheCode, null, "203.0.113.7",
                TestContext.Current.CancellationToken);
        }

        // Counting per address must not become a way of asking which addresses
        // exist, so an address nobody holds fills its window exactly as one
        // somebody holds does.
        Assert.True((await installation.SignIn.ExecuteAsync(
            "nobody@example.com",
            TheirPassword, TheCode, null, "203.0.113.7",
            TestContext.Current.CancellationToken)).Throttled);

        // And the account that does exist is untouched by it.
        Assert.NotNull((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, "203.0.113.7",
            TestContext.Current.CancellationToken)).Session);
    }

    [Fact]
    public async Task An_installation_with_no_identity_admits_nothing()
    {
        var installation = Installation_with_nobody_on_it();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken)).Session);

        // And it still hashed. An address nobody holds has to cost what one
        // somebody holds costs, or the clock says which is which (ADR 0056).
        Assert.Equal(1, installation.Hasher.Verifications);
    }

    [Fact]
    public async Task An_address_nobody_holds_costs_what_a_wrong_password_costs()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            "somebody.else@example.com",
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken)).Session);

        Assert.Equal(1, installation.Hasher.Verifications);
        Assert.Equal(0, installation.Identities.Writes);
    }

    [Fact]
    public async Task A_user_who_was_invited_and_never_arrived_admits_nothing()
    {
        var installation = Installation_with_nobody_on_it();
        installation.Identities.Seed(
            User.Invite("Newcomer", TheirAddress, administrator: false, Claimed));

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, null, null, null, TestContext.Current.CancellationToken)).Session);
    }

    [Fact]
    public async Task A_deactivated_user_admits_nothing()
    {
        var installation = Signed_up_installation();
        var user = installation.Identities.Stored.OfType<User>().Single();
        user.Deactivate();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken)).Session);
    }

    [Fact]
    public async Task The_address_is_read_however_it_was_typed()
    {
        var installation = Signed_up_installation();

        Assert.NotNull(await installation.SignIn.ExecuteAsync(
            "  SOMEBODY@Example.COM ",
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_backup_code_stands_in_for_the_second_factor_and_is_spent()
    {
        var installation = Signed_up_installation();
        var code = installation.BackupCodes[0];

        var attempt = await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, null, code.Display, null, TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Session);
        var signedIn = attempt.Session;

        // docs/sign-in.md: the product says how many remain whenever one is
        // spent, because a set that quietly runs out ends at Host Recovery.
        Assert.Equal(BackupCode.SetSize - 1, signedIn.BackupCodesRemaining);
        Assert.Equal(1, installation.Identities.BackupCodes.Count(stored => stored.IsSpent));
    }

    [Fact]
    public async Task A_backup_code_is_read_however_it_was_typed()
    {
        var installation = Signed_up_installation();
        var code = installation.BackupCodes[0];

        // Refusing a code over a dash or a capital is refusing the operator
        // their way back in.
        var attempt = await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword,
            null,
            $"  {code.Symbols.ToUpperInvariant()}  ",
            null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Session);
        var signedIn = attempt.Session;
    }

    [Fact]
    public async Task A_backup_code_offered_twice_is_refused_the_second_time()
    {
        var installation = Signed_up_installation();
        var code = installation.BackupCodes[0].Display;

        Assert.NotNull(await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, null, code, null, TestContext.Current.CancellationToken));

        // A spent code matches exactly as a fresh one does; being single use is
        // what refuses it, and it is refused with the same answer as a code that
        // was never theirs.
        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, null, code, null, TestContext.Current.CancellationToken)).Session);
        Assert.Single(installation.Sessions.Stored);
    }

    [Fact]
    public async Task A_code_that_is_nobody_s_admits_nothing()
    {
        var installation = Signed_up_installation();

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword,
            null,
            BackupCodeText.Mint().Display,
            null,
            TestContext.Current.CancellationToken)).Session);
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task Getting_in_rewrites_a_hash_that_is_out_of_date()
    {
        var installation = Signed_up_installation();
        installation.Hasher.Answer = PasswordCheck.RightAndOutOfDate;

        Assert.NotNull(await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken));

        // ADR 0032: raising the cost later is a path rather than an intention,
        // and this is the step that walks it.
        Assert.Equal(1, installation.Hasher.Hashes);
        Assert.Equal(1, installation.Identities.Writes);
    }

    [Fact]
    public async Task An_attempt_that_fails_on_the_second_factor_rewrites_nothing()
    {
        var installation = Signed_up_installation();
        installation.Hasher.Answer = PasswordCheck.RightAndOutOfDate;

        Assert.Null((await installation.SignIn.ExecuteAsync(
            TheirAddress,
            TheirPassword, "000000", null, null, TestContext.Current.CancellationToken)).Session);

        // The rewrite is maintenance a sign-in owes the row, not something a
        // correct password on its own gets to trigger.
        Assert.Equal(0, installation.Hasher.Hashes);
        Assert.Equal(0, installation.Identities.Writes);
    }

    private static Installation Signed_up_installation()
    {
        var installation = Installation_with_nobody_on_it();
        var user = TheUser();
        user.EnrolSecondFactor(installation.Cipher.Encrypt(StubSecondFactor.Secret), Claimed);

        installation.BackupCodes =
            installation.Identities.SeedWithBackupCodes(user, Claimed).Shown;

        return installation;
    }

    private static User TheUser()
    {
        var user = User.Bootstrap("The Administrator", TheirAddress, Claimed);
        user.ActivateWith(StubPasswordHasher.HashOf(TheirPassword));

        return user;
    }

    private static Installation Installation_with_nobody_on_it()
    {
        var identities = new InMemoryIdentities();
        var sessions = new InMemorySessions();
        var hasher = new StubPasswordHasher();
        var throttle = new InMemorySignInThrottle();
        var secondFactor = new StubSecondFactor(TheCode);
        var cipher = new ReversingCipher();

        return new Installation
        {
            Identities = identities,
            Sessions = sessions,
            Hasher = hasher,
            SecondFactor = secondFactor,
            Cipher = cipher,
            Throttle = throttle,
            SignIn = new SignIn(
                identities,
                sessions,
                hasher,
                new DummyPasswordHash(hasher),
                throttle,
                secondFactor,
                cipher,
                new StoppedClock(Claimed.AddDays(1))),
        };
    }

    private sealed class Installation
    {
        public required InMemoryIdentities Identities { get; init; }

        public required InMemorySessions Sessions { get; init; }

        public required StubPasswordHasher Hasher { get; init; }

        public required StubSecondFactor SecondFactor { get; init; }

        public required ReversingCipher Cipher { get; init; }

        public required InMemorySignInThrottle Throttle { get; init; }

        public required SignIn SignIn { get; init; }

        public IReadOnlyList<BackupCodeText> BackupCodes { get; set; } = [];
    }
}
