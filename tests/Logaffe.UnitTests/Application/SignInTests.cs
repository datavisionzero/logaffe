using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

public sealed class SignInTests
{
    private const string TheirPassword = "a passphrase they typed";
    private const string TheCode = "314159";

    private static readonly DateTimeOffset Claimed = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_password_and_the_second_factor_start_a_session()
    {
        var installation = Claimed_installation();

        var signedIn = await installation.SignIn.ExecuteAsync(
            TheirPassword, TheCode, null, "203.0.113.7", TestContext.Current.CancellationToken);

        Assert.NotNull(signedIn);
        Assert.Equal([signedIn.Session], installation.Sessions.Stored);
        Assert.Equal("203.0.113.7", signedIn.Session.LastSeenFrom);

        // Nothing was spent to get in, so there is nothing to say about the set.
        Assert.Null(signedIn.BackupCodesRemaining);
    }

    [Fact]
    public async Task The_session_holds_a_hash_and_the_secret_is_handed_over_once()
    {
        var installation = Claimed_installation();

        var signedIn = await installation.SignIn.ExecuteAsync(
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken);

        Assert.NotNull(signedIn);

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
        var installation = Claimed_installation();

        await installation.SignIn.ExecuteAsync(
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken);

        // The row holds it sealed (ADR 0032); what the arithmetic gets is what
        // the key on the host volume made of it again.
        Assert.Equal(StubSecondFactor.Secret, installation.SecondFactor.CheckedAgainst);
    }

    [Fact]
    public async Task A_wrong_password_admits_nothing_and_writes_nothing()
    {
        var installation = Claimed_installation();

        Assert.Null(await installation.SignIn.ExecuteAsync(
            "some other passphrase", TheCode, null, null, TestContext.Current.CancellationToken));

        // ADR 0017: with exactly one account a lockout is a weapon pointed at
        // its owner, so there is no counter, no flag, and nothing at all for a
        // failed attempt to leave behind.
        Assert.Empty(installation.Sessions.Stored);
        Assert.Equal(0, installation.Operators.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
    }

    [Fact]
    public async Task The_right_password_with_the_wrong_code_admits_nothing()
    {
        var installation = Claimed_installation();

        Assert.Null(await installation.SignIn.ExecuteAsync(
            TheirPassword, "000000", null, null, TestContext.Current.CancellationToken));
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task A_password_below_the_minimum_never_reaches_the_hasher()
    {
        var installation = Claimed_installation();

        Assert.Null(await installation.SignIn.ExecuteAsync(
            "short", TheCode, null, null, TestContext.Current.CancellationToken));

        // Hashing is deliberately slow and this surface is public, so what is
        // not a password is refused before PBKDF2 is asked to spend anything on
        // it.
        Assert.Equal(0, installation.Hasher.Verifications);
    }

    [Fact]
    public async Task An_unclaimed_installation_admits_nothing()
    {
        var installation = Unclaimed_installation();

        Assert.Null(await installation.SignIn.ExecuteAsync(
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken));
        Assert.Equal(0, installation.Hasher.Verifications);
    }

    [Fact]
    public async Task A_backup_code_stands_in_for_the_second_factor_and_is_spent()
    {
        var installation = Claimed_installation();
        var code = installation.BackupCodes[0];

        var signedIn = await installation.SignIn.ExecuteAsync(
            TheirPassword, null, code.Display, null, TestContext.Current.CancellationToken);

        Assert.NotNull(signedIn);

        // docs/sign-in.md: the product says how many remain whenever one is
        // spent, because a set that quietly runs out ends at Host Recovery.
        Assert.Equal(BackupCode.SetSize - 1, signedIn.BackupCodesRemaining);
        Assert.Equal(1, installation.Operators.BackupCodes.Count(stored => stored.IsSpent));
    }

    [Fact]
    public async Task A_backup_code_is_read_however_it_was_typed()
    {
        var installation = Claimed_installation();
        var code = installation.BackupCodes[0];

        // Refusing a code over a dash or a capital is refusing the operator
        // their way back in.
        var signedIn = await installation.SignIn.ExecuteAsync(
            TheirPassword,
            null,
            $"  {code.Symbols.ToUpperInvariant()}  ",
            null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(signedIn);
    }

    [Fact]
    public async Task A_backup_code_offered_twice_is_refused_the_second_time()
    {
        var installation = Claimed_installation();
        var code = installation.BackupCodes[0].Display;

        Assert.NotNull(await installation.SignIn.ExecuteAsync(
            TheirPassword, null, code, null, TestContext.Current.CancellationToken));

        // A spent code matches exactly as a fresh one does; being single use is
        // what refuses it, and it is refused with the same answer as a code that
        // was never theirs.
        Assert.Null(await installation.SignIn.ExecuteAsync(
            TheirPassword, null, code, null, TestContext.Current.CancellationToken));
        Assert.Single(installation.Sessions.Stored);
    }

    [Fact]
    public async Task A_code_that_is_nobody_s_admits_nothing()
    {
        var installation = Claimed_installation();

        Assert.Null(await installation.SignIn.ExecuteAsync(
            TheirPassword,
            null,
            BackupCodeText.Mint().Display,
            null,
            TestContext.Current.CancellationToken));
        Assert.Empty(installation.Sessions.Stored);
    }

    [Fact]
    public async Task Getting_in_rewrites_a_hash_that_is_out_of_date()
    {
        var installation = Claimed_installation();
        installation.Hasher.Answer = PasswordCheck.RightAndOutOfDate;

        Assert.NotNull(await installation.SignIn.ExecuteAsync(
            TheirPassword, TheCode, null, null, TestContext.Current.CancellationToken));

        // ADR 0032: raising the cost later is a path rather than an intention,
        // and this is the step that walks it.
        Assert.Equal(1, installation.Hasher.Hashes);
        Assert.Equal(1, installation.Operators.Writes);
    }

    [Fact]
    public async Task An_attempt_that_fails_on_the_second_factor_rewrites_nothing()
    {
        var installation = Claimed_installation();
        installation.Hasher.Answer = PasswordCheck.RightAndOutOfDate;

        Assert.Null(await installation.SignIn.ExecuteAsync(
            TheirPassword, "000000", null, null, TestContext.Current.CancellationToken));

        // The rewrite is maintenance a sign-in owes the row, not something a
        // correct password on its own gets to trigger.
        Assert.Equal(0, installation.Hasher.Hashes);
        Assert.Equal(0, installation.Operators.Writes);
    }

    private static Installation Claimed_installation()
    {
        var installation = Unclaimed_installation();
        var theOperator = Operator.Claim(StubPasswordHasher.HashOf(TheirPassword), Claimed);
        theOperator.EnrolSecondFactor(
            installation.Cipher.Encrypt(StubSecondFactor.Secret), Claimed);

        installation.BackupCodes = installation.Operators.Claim(theOperator, Claimed).Shown;

        return installation;
    }

    private static Installation Unclaimed_installation()
    {
        var operators = new InMemoryOperators();
        var sessions = new InMemorySessions();
        var hasher = new StubPasswordHasher();
        var secondFactor = new StubSecondFactor(TheCode);
        var cipher = new ReversingCipher();

        return new Installation
        {
            Operators = operators,
            Sessions = sessions,
            Hasher = hasher,
            SecondFactor = secondFactor,
            Cipher = cipher,
            SignIn = new SignIn(
                operators,
                sessions,
                hasher,
                secondFactor,
                cipher,
                new StoppedClock(Claimed.AddDays(1))),
        };
    }

    private sealed class Installation
    {
        public required InMemoryOperators Operators { get; init; }

        public required InMemorySessions Sessions { get; init; }

        public required StubPasswordHasher Hasher { get; init; }

        public required StubSecondFactor SecondFactor { get; init; }

        public required ReversingCipher Cipher { get; init; }

        public required SignIn SignIn { get; init; }

        public IReadOnlyList<BackupCodeText> BackupCodes { get; set; } = [];
    }
}
