using System.Buffers.Text;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// Taking an installation nobody owns, and handing one back.
/// </summary>
public sealed class ClaimActsTests
{
    private const string TheirPassword = "a passphrase they typed";
    private const string TheCode = "314159";

    private static readonly DateTimeOffset FirstRun = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_run_opens_the_window_and_a_restart_does_not_extend_it()
    {
        var installation = new Unclaimed();

        var opened = await installation.Open.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FirstRun.AddMinutes(30), opened.ClosesAt);

        // The deadline belongs to the installation rather than to the process,
        // so nobody gains anything by forcing a restart (docs/setup.md).
        installation.Clock.Now = FirstRun.AddMinutes(20);
        var restarted = await installation.Open.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(opened.ClosesAt, restarted.ClosesAt);
        Assert.Equal(1, installation.Installation.Writes);
    }

    [Fact]
    public async Task An_installation_nobody_owns_says_so_until_the_window_closes()
    {
        var installation = await new Unclaimed().StartedAsync();

        var open = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(open.IsClaimed);
        Assert.True(open.WindowIsOpen);
        Assert.Equal(FirstRun.AddMinutes(30), open.ClosesAt);

        installation.Clock.Now = FirstRun.AddMinutes(30);

        var lapsed = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(lapsed.IsClaimed);
        Assert.False(lapsed.WindowIsOpen);

        // Nothing to count down to. What the screen needs now is the host
        // command, and it names it.
        Assert.Null(lapsed.ClosesAt);
    }

    [Fact]
    public async Task Drawing_an_enrolment_stores_nothing()
    {
        var installation = await new Unclaimed().StartedAsync();

        var begun = await installation.Begin.ExecuteAsync(
            "logs.example.org", TestContext.Current.CancellationToken);

        Assert.NotNull(begun.Enrolment);
        Assert.Equal(BackupCode.SetSize, begun.Enrolment.BackupCodes.Count);
        Assert.Contains("logs.example.org", begun.Enrolment.EnrolmentUri);

        // Every step before the last is a form with no effect (ADR 0014).
        Assert.Equal(0, installation.Operators.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
        Assert.Empty(installation.Operators.BackupCodes);
    }

    [Fact]
    public async Task An_enrolment_is_refused_once_the_window_has_closed()
    {
        var installation = await new Unclaimed().StartedAsync();
        installation.Clock.Now = FirstRun.AddMinutes(31);

        var begun = await installation.Begin.ExecuteAsync(
            "logs.example.org", TestContext.Current.CancellationToken);

        Assert.Null(begun.Enrolment);
        Assert.False(begun.State.IsClaimed);
        Assert.False(begun.State.WindowIsOpen);
    }

    [Fact]
    public async Task The_claim_writes_the_account_the_codes_and_a_session_and_nothing_before()
    {
        var installation = await new Unclaimed().StartedAsync();
        var enrolment = await installation.EnrolAsync();

        var attempt = await installation.ClaimWith(enrolment, "198.51.100.4");

        Assert.Equal(ClaimOutcome.Claimed, attempt.Outcome);
        Assert.NotNull(attempt.Session);
        Assert.NotNull(attempt.Secret);
        Assert.Equal("198.51.100.4", attempt.Session.LastSeenFrom);

        // The account and its first set of codes are one act, and the session
        // the claim hands out is the second (ADR 0014).
        Assert.Equal(1, installation.Operators.Writes);
        Assert.Equal(BackupCode.SetSize, installation.Operators.BackupCodes.Count);
        Assert.Equal([attempt.Session], installation.Sessions.Stored);

        // The second factor was sealed on the way in, not stored as it arrived
        // (ADR 0032).
        var stored = await installation.Operators.FindAsync(
            TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(
            installation.Cipher.Encrypt(StubSecondFactor.Secret),
            stored.EncryptedSecondFactorSecret);
    }

    [Fact]
    public async Task The_codes_that_were_shown_are_the_codes_that_were_stored()
    {
        var installation = await new Unclaimed().StartedAsync();
        var enrolment = await installation.EnrolAsync();

        Assert.Equal(ClaimOutcome.Claimed, (await installation.ClaimWith(enrolment)).Outcome);

        // Every one of the ten on the sheet spends, and no eleventh does. The
        // sheet is the only copy from the moment it is shown (ADR 0035).
        Assert.All(
            enrolment.BackupCodes,
            code => Assert.Contains(
                installation.Operators.BackupCodes, stored => stored.Matches(code)));

        Assert.DoesNotContain(
            installation.Operators.BackupCodes,
            stored => stored.Matches(BackupCodeText.Mint()));
    }

    [Theory]
    [InlineData("short", TheCode, true, ClaimOutcome.PasswordNotOne)]
    [InlineData(TheirPassword, "000000", true, ClaimOutcome.SecondFactorRefused)]
    [InlineData(TheirPassword, TheCode, false, ClaimOutcome.BackupCodeRefused)]
    public async Task A_step_that_fails_names_itself_and_stores_nothing(
        string password, string code, bool codeFromTheSheet, ClaimOutcome expected)
    {
        var installation = await new Unclaimed().StartedAsync();
        var enrolment = await installation.EnrolAsync();

        var attempt = await installation.Claim.ExecuteAsync(
            password,
            enrolment.Ticket,
            code,
            codeFromTheSheet ? enrolment.BackupCodes[3].Display : BackupCodeText.Mint().Symbols,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, attempt.Outcome);
        Assert.Null(attempt.Session);

        // Refusing is a screen, not a state. Nothing was written on the way to
        // it, so there is nothing to clean up (ADR 0014).
        Assert.Equal(0, installation.Operators.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
    }

    [Fact]
    public async Task An_enrolment_this_installation_did_not_seal_is_refused()
    {
        var installation = await new Unclaimed().StartedAsync();
        var enrolment = await installation.EnrolAsync();

        foreach (var ticket in new[]
        {
            null,
            string.Empty,
            "not base64url at all !!",
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes("a ticket somebody made up")),
        })
        {
            var attempt = await installation.Claim.ExecuteAsync(
                TheirPassword,
                ticket,
                TheCode,
                enrolment.BackupCodes[0].Display,
                null,
                TestContext.Current.CancellationToken);

            Assert.Equal(ClaimOutcome.EnrolmentNotOurs, attempt.Outcome);
        }
    }

    [Fact]
    public async Task An_enrolment_drawn_before_a_host_recovery_is_refused_after_it()
    {
        var installation = await new Unclaimed().StartedAsync();
        var enrolment = await installation.EnrolAsync();

        installation.Clock.Now = FirstRun.AddMinutes(5);
        await installation.Recover.ExecuteAsync(TestContext.Current.CancellationToken);

        // The window it was drawn in is not the current one any more, and a
        // ticket belongs to one window (ADR 0035).
        var attempt = await installation.ClaimWith(enrolment);

        Assert.Equal(ClaimOutcome.EnrolmentNotOurs, attempt.Outcome);
    }

    [Fact]
    public async Task There_is_no_re_claim_while_claimed()
    {
        var installation = await new Unclaimed().StartedAsync();
        Assert.Equal(
            ClaimOutcome.Claimed,
            (await installation.ClaimWith(await installation.EnrolAsync())).Outcome);

        var again = await installation.Begin.ExecuteAsync(
            "logs.example.org", TestContext.Current.CancellationToken);

        Assert.Null(again.Enrolment);
        Assert.True(again.State.IsClaimed);
        Assert.False(again.State.WindowIsOpen);
    }

    [Fact]
    public async Task Host_recovery_removes_the_account_and_arms_a_fresh_window()
    {
        var installation = await new Unclaimed().StartedAsync();
        await installation.ClaimWith(await installation.EnrolAsync());

        // Long after the first window lapsed, which is the case this command
        // exists for: nobody can sign in any more (ADR 0013).
        installation.Clock.Now = FirstRun.AddDays(90);
        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(recovered.ThereWasAnOperator);
        Assert.Equal(FirstRun.AddDays(90).AddMinutes(30), recovered.Window.ClosesAt);

        var state = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(state.IsClaimed);
        Assert.True(state.WindowIsOpen);

        // And it is claimable again, which is the whole point of the reset.
        Assert.Equal(
            ClaimOutcome.Claimed,
            (await installation.ClaimWith(await installation.EnrolAsync())).Outcome);
    }

    [Fact]
    public async Task Host_recovery_on_an_installation_nobody_claimed_still_arms_the_window()
    {
        var installation = await new Unclaimed().StartedAsync();
        installation.Clock.Now = FirstRun.AddHours(3);

        // The other case VISION.md asks this command to cover: a window that
        // lapsed before anyone got to it. There is nothing to remove and that
        // is not a failure.
        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.False(recovered.ThereWasAnOperator);
        Assert.True(recovered.Window.IsOpenAt(installation.Clock.Now));
    }

    /// <summary>
    /// An installation that has just been started for the first time, with the
    /// five acts of the claim wired to stores that hold nothing.
    /// </summary>
    private sealed class Unclaimed
    {
        public Unclaimed()
        {
            Clock = new StoppedClock(FirstRun);
            Installation = new InMemoryInstallation();
            Operators = new InMemoryOperators();
            Sessions = new InMemorySessions();
            Cipher = new ReversingCipher();
            SecondFactor = new StubSecondFactor(TheCode);

            Check = new CheckTheClaim(Operators, Installation, Clock);
            Open = new OpenTheClaimWindow(Installation, Clock);
            Begin = new BeginEnrolment(Check, Installation, SecondFactor, Cipher);
            Claim = new ClaimTheInstallation(
                Installation,
                Operators,
                Sessions,
                new StubPasswordHasher(),
                SecondFactor,
                Cipher,
                Clock);
            Recover = new Recover(Operators, Installation, Clock);
        }

        public StoppedClock Clock { get; }

        public InMemoryInstallation Installation { get; }

        public InMemoryOperators Operators { get; }

        public InMemorySessions Sessions { get; }

        public ReversingCipher Cipher { get; }

        public StubSecondFactor SecondFactor { get; }

        public CheckTheClaim Check { get; }

        public OpenTheClaimWindow Open { get; }

        public BeginEnrolment Begin { get; }

        public ClaimTheInstallation Claim { get; }

        public Recover Recover { get; }

        public async Task<Unclaimed> StartedAsync()
        {
            await Open.ExecuteAsync(TestContext.Current.CancellationToken);

            return this;
        }

        public async Task<Enrolment> EnrolAsync()
        {
            var begun = await Begin.ExecuteAsync(
                "logs.example.org", TestContext.Current.CancellationToken);

            Assert.NotNull(begun.Enrolment);

            return begun.Enrolment;
        }

        /// <summary>
        /// The last step, walked the way the operator walks it: the code typed
        /// back off the sheet, grouped as it was printed.
        /// </summary>
        public Task<ClaimAttempt> ClaimWith(Enrolment enrolment, string? seenFrom = null) =>
            Claim.ExecuteAsync(
                TheirPassword,
                enrolment.Ticket,
                TheCode,
                enrolment.BackupCodes[0].Display,
                seenFrom,
                TestContext.Current.CancellationToken);
    }
}
