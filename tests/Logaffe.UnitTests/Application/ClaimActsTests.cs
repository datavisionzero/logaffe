using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// Taking an installation nobody owns, and handing one back.
/// </summary>
public sealed class ClaimActsTests
{
    private const string TheirPassword = "a passphrase they typed";

    private static readonly DateTimeOffset FirstRun = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_run_opens_the_window_and_a_restart_does_not_extend_it()
    {
        var installation = new Unclaimed(InWindowMode);

        var opened = await installation.Open.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FirstRun.AddMinutes(30), opened.Guard.WindowClosesAt);

        // The deadline belongs to the installation rather than to the process,
        // so nobody gains anything by forcing a restart (docs/setup.md).
        installation.Clock.Now = FirstRun.AddMinutes(20);
        var restarted = await installation.Open.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(opened.Guard.WindowClosesAt, restarted.Guard.WindowClosesAt);
        Assert.Equal(1, installation.Installation.Writes);
    }

    [Fact]
    public async Task The_first_start_draws_a_secret_and_hands_it_over_once()
    {
        var installation = new Unclaimed(ClaimSettings.Default);

        var opened = await installation.Open.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(opened.Drawn);
        Assert.Equal(ClaimSecret.DrawnLength, opened.Drawn.Text.Length);

        // The value goes to the volume for the operator to read; what the row
        // holds is its hash (ADR 0040).
        Assert.Equal(opened.Drawn.Text, installation.Handover.HandedOver);
        Assert.True(opened.Guard.HasDrawnSecret);

        // And a restart draws no second one: the secret that was handed over is
        // the one that opens the door.
        installation.Clock.Now = FirstRun.AddDays(3);
        var restarted = await installation.Open.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(restarted.Drawn);
        Assert.Equal(opened.Drawn.Text, installation.Handover.HandedOver);
    }

    [Fact]
    public async Task An_installation_told_its_secret_draws_none_and_writes_nothing()
    {
        var installation = new Unclaimed(WithSuppliedSecret);

        var opened = await installation.Open.ExecuteAsync(TestContext.Current.CancellationToken);

        // A supplied secret is compared against configuration and stored
        // nowhere, so there is no second copy to disagree with it (ADR 0040).
        Assert.Null(opened.Drawn);
        Assert.False(opened.Guard.HasDrawnSecret);
        Assert.Null(installation.Handover.HandedOver);
    }

    [Fact]
    public async Task An_installation_with_an_operator_draws_no_secret_for_itself()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();
        await installation.ClaimWith(installation.Secret);

        // There is no re-claim while claimed, so a start now would be writing a
        // live credential to the volume of an installation in ordinary use.
        installation.Handover.Remove();
        var started = await installation.Open.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Null(started.Drawn);
        Assert.Null(installation.Handover.HandedOver);
    }

    [Fact]
    public async Task An_installation_nobody_owns_says_so_until_the_window_closes()
    {
        var installation = await new Unclaimed(InWindowMode).StartedAsync();

        var open = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(open.IsClaimed);
        Assert.True(open.CanBeClaimed);
        Assert.False(open.NeedsSecret);
        Assert.Equal(FirstRun.AddMinutes(30), open.ClosesAt);

        installation.Clock.Now = FirstRun.AddMinutes(30);

        var lapsed = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(lapsed.IsClaimed);
        Assert.False(lapsed.CanBeClaimed);

        // Nothing to count down to. What the screen needs now is the host
        // command, and it names it.
        Assert.Null(lapsed.ClosesAt);
    }

    [Fact]
    public async Task An_installation_guarded_by_a_secret_has_nothing_to_count_down_to()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();

        // A door that is locked does not need a clock, so this is the same
        // answer a week later as it is now (ADR 0040).
        installation.Clock.Now = FirstRun.AddDays(7);

        var state = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(state.IsClaimed);
        Assert.True(state.CanBeClaimed);
        Assert.True(state.NeedsSecret);
        Assert.Null(state.ClosesAt);
    }

    [Fact]
    public async Task The_claim_writes_the_account_and_a_session_and_nothing_else()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();

        var attempt = await installation.ClaimWith(installation.Secret, "198.51.100.4");

        Assert.Equal(ClaimOutcome.Claimed, attempt.Outcome);
        Assert.NotNull(attempt.Session);
        Assert.NotNull(attempt.Secret);
        Assert.Equal("198.51.100.4", attempt.Session.LastSeenFrom);

        // One statement for the account and one for the session, and no third:
        // the second factor and its backup codes are the operator's to enrol
        // afterwards (ADR 0041).
        Assert.Equal(1, installation.Operators.Writes);
        Assert.Empty(installation.Operators.BackupCodes);
        Assert.Equal([attempt.Session], installation.Sessions.Stored);

        var stored = await installation.Operators.FindAsync(
            TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.False(stored.HasSecondFactor);
        Assert.Null(stored.SecondFactorEnrolledAt);

        // The file the secret was handed over in delivered what it was for.
        Assert.Null(installation.Handover.HandedOver);
        Assert.Equal(1, installation.Handover.Removals);
    }

    [Fact]
    public async Task A_secret_that_is_not_the_one_is_refused_and_stores_nothing()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();

        foreach (var presented in new[] { null, string.Empty, "not the one at all" })
        {
            var attempt = await installation.Claim.ExecuteAsync(
                TheirPassword, presented, null, TestContext.Current.CancellationToken);

            Assert.Equal(ClaimOutcome.SecretRefused, attempt.Outcome);
        }

        Assert.Equal(0, installation.Operators.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
        Assert.Equal(0, installation.Handover.Removals);
    }

    [Fact]
    public async Task A_secret_admits_a_claim_however_long_the_installation_has_stood()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();

        // The window is not consulted in this mode at all: it is a fact about
        // the row and not the guard in force (ADR 0040).
        installation.Clock.Now = FirstRun.AddDays(30);

        Assert.Equal(
            ClaimOutcome.Claimed,
            (await installation.ClaimWith(installation.Secret)).Outcome);
    }

    [Fact]
    public async Task A_supplied_secret_is_the_one_that_admits()
    {
        var installation = await new Unclaimed(WithSuppliedSecret).StartedAsync();

        var wrong = await installation.Claim.ExecuteAsync(
            TheirPassword, "something else entirely", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(ClaimOutcome.SecretRefused, wrong.Outcome);

        var right = await installation.Claim.ExecuteAsync(
            TheirPassword, TheSuppliedSecret, null, TestContext.Current.CancellationToken);
        Assert.Equal(ClaimOutcome.Claimed, right.Outcome);
    }

    [Fact]
    public async Task A_window_takes_no_secret_and_closes()
    {
        var installation = await new Unclaimed(InWindowMode).StartedAsync();

        var attempt = await installation.Claim.ExecuteAsync(
            TheirPassword, null, null, TestContext.Current.CancellationToken);
        Assert.Equal(ClaimOutcome.Claimed, attempt.Outcome);

        var next = await new Unclaimed(InWindowMode).StartedAsync();
        next.Clock.Now = FirstRun.AddMinutes(31);

        var lapsed = await next.Claim.ExecuteAsync(
            TheirPassword, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ClaimOutcome.WindowClosed, lapsed.Outcome);
        Assert.Equal(0, next.Operators.Writes);
    }

    [Fact]
    public async Task A_password_that_is_not_one_names_itself_and_stores_nothing()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();

        var attempt = await installation.Claim.ExecuteAsync(
            "short", installation.Secret, null, TestContext.Current.CancellationToken);

        Assert.Equal(ClaimOutcome.PasswordNotOne, attempt.Outcome);
        Assert.Null(attempt.Session);

        // Refusing is a screen, not a state. Nothing was written on the way to
        // it, so there is nothing to clean up (ADR 0014).
        Assert.Equal(0, installation.Operators.Writes);
        Assert.Equal(0, installation.Sessions.Writes);
    }

    [Fact]
    public async Task An_installation_with_no_secret_to_present_to_cannot_be_claimed()
    {
        // A database somebody made by hand: no start ever wrote the row, so
        // there is nothing to present to and the host command is the way out.
        var installation = new Unclaimed(ClaimSettings.Default);

        var state = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(state.CanBeClaimed);

        var attempt = await installation.Claim.ExecuteAsync(
            TheirPassword, "anything at all", null, TestContext.Current.CancellationToken);

        Assert.Equal(ClaimOutcome.NoSecretToPresentTo, attempt.Outcome);
    }

    [Fact]
    public async Task There_is_no_re_claim_while_claimed()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();
        var secret = installation.Secret;

        Assert.Equal(ClaimOutcome.Claimed, (await installation.ClaimWith(secret)).Outcome);

        var again = await installation.ClaimWith(secret);
        Assert.Equal(ClaimOutcome.AlreadyClaimed, again.Outcome);

        var state = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.True(state.IsClaimed);
        Assert.False(state.CanBeClaimed);
    }

    [Fact]
    public async Task Host_recovery_removes_the_account_and_draws_a_fresh_secret()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();
        var first = installation.Secret;
        await installation.ClaimWith(first);

        // Long after the account was made, which is the case this command
        // exists for: nobody can sign in any more (ADR 0013).
        installation.Clock.Now = FirstRun.AddDays(90);
        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(recovered.ThereWasAnOperator);
        Assert.NotNull(recovered.DrawnSecret);
        Assert.NotEqual(first, recovered.DrawnSecret.Text);
        Assert.Equal(recovered.DrawnSecret.Text, installation.Handover.HandedOver);

        var state = await installation.Check.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.False(state.IsClaimed);
        Assert.True(state.CanBeClaimed);

        // The secret the previous operator held opens nothing: this is the
        // moment the installation's notion of who may claim it changes.
        Assert.Equal(
            ClaimOutcome.SecretRefused, (await installation.ClaimWith(first)).Outcome);

        Assert.Equal(
            ClaimOutcome.Claimed,
            (await installation.ClaimWith(recovered.DrawnSecret.Text)).Outcome);
    }

    [Fact]
    public async Task Host_recovery_in_window_mode_arms_a_fresh_window_and_draws_nothing()
    {
        var installation = await new Unclaimed(InWindowMode).StartedAsync();
        await installation.Claim.ExecuteAsync(
            TheirPassword, null, null, TestContext.Current.CancellationToken);

        installation.Clock.Now = FirstRun.AddDays(90);
        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.DrawnSecret);
        Assert.Null(installation.Handover.HandedOver);
        Assert.Equal(FirstRun.AddDays(90).AddMinutes(30), recovered.Guard.WindowClosesAt);
    }

    [Fact]
    public async Task Host_recovery_takes_the_agent_tokens_and_leaves_the_ingest_tokens()
    {
        var installation = await new Unclaimed(ClaimSettings.Default).StartedAsync();
        await installation.ClaimWith(installation.Secret);

        await installation.IssueAgentTokenAsync("terminal agent");
        await installation.IssueAgentTokenAsync("desktop agent");
        var delivering = installation.HoldIngestToken();

        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        // An agent token reads every entry in every project, past the password
        // and the second factor, and this is the act by which an installation
        // changes hands (docs/mcp.md). Leaving one behind hands the reading to
        // whoever held it.
        Assert.Empty(installation.Tokens.StoredAgentTokens);
        Assert.Equal(2, recovered.AgentTokensRemoved);

        // The ingest token stays for the reason the agent token goes: it keeps
        // an application delivering, and the installation losing its contents is
        // not what this command is for (ADR 0013).
        Assert.Equal([delivering], installation.Tokens.Stored);
    }

    [Fact]
    public async Task Host_recovery_on_an_installation_nobody_claimed_still_opens_the_way_in()
    {
        var installation = await new Unclaimed(InWindowMode).StartedAsync();
        installation.Clock.Now = FirstRun.AddHours(3);

        // The other case VISION.md asks this command to cover: a window that
        // lapsed before anyone got to it. There is nothing to remove and that
        // is not a failure.
        var recovered = await installation.Recover.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.False(recovered.ThereWasAnOperator);
        Assert.True(recovered.Guard.WindowIsOpenAt(installation.Clock.Now));
    }

    private const string TheSuppliedSecret = "the-one-the-compose-file-names";

    /// <summary>An installation whose claim is guarded by an open window.</summary>
    private static ClaimSettings InWindowMode => new(ClaimMode.Window, null);

    /// <summary>One whose secret the operator set before the first start.</summary>
    private static ClaimSettings WithSuppliedSecret =>
        new(ClaimMode.Secret, ClaimSecret.TryCreate(TheSuppliedSecret, out var secret)
            ? secret
            : throw new InvalidOperationException("The supplied secret is not one."));

    /// <summary>
    /// An installation that has just been started for the first time, with the
    /// acts of the claim wired to stores that hold nothing.
    /// </summary>
    private sealed class Unclaimed
    {
        public Unclaimed(ClaimSettings settings)
        {
            Clock = new StoppedClock(FirstRun);
            Installation = new InMemoryInstallation();
            Operators = new InMemoryOperators();
            Sessions = new InMemorySessions();
            Tokens = new InMemoryTokens();
            Cipher = new ReversingCipher();
            Handover = new InMemoryClaimSecretHandover();

            Check = new CheckTheClaim(Operators, Installation, settings, Clock);
            Open = new OpenTheClaim(Installation, Operators, Handover, settings, Clock);
            Claim = new ClaimTheInstallation(
                Installation,
                Operators,
                Sessions,
                Handover,
                new StubPasswordHasher(),
                settings,
                Clock);
            Recover = new Recover(Operators, Tokens, Installation, Handover, settings, Clock);
        }

        public StoppedClock Clock { get; }

        public InMemoryInstallation Installation { get; }

        public InMemoryOperators Operators { get; }

        public InMemorySessions Sessions { get; }

        public InMemoryTokens Tokens { get; }

        public ReversingCipher Cipher { get; }

        public InMemoryClaimSecretHandover Handover { get; }

        public CheckTheClaim Check { get; }

        public OpenTheClaim Open { get; }

        public ClaimTheInstallation Claim { get; }

        public Recover Recover { get; }

        /// <summary>
        /// The secret the operator was handed, read the way they read it: off
        /// the file the installation wrote it to.
        /// </summary>
        public string Secret =>
            Handover.HandedOver
            ?? throw new InvalidOperationException("Nothing was handed over.");

        /// <summary>
        /// An agent the operator connected, issued the way they issue one.
        /// </summary>
        public Task<IssuedToken> IssueAgentTokenAsync(string name) =>
            new IssueAgentToken(Tokens, Cipher, Clock)
                .ExecuteAsync(name, TestContext.Current.CancellationToken);

        /// <summary>
        /// A project's ingest token, put in the store directly: which project it
        /// belongs to is not this test's business, and recovery is not allowed
        /// to have an opinion about it either.
        /// </summary>
        public IngestToken HoldIngestToken()
        {
            var minted = TokenText.Mint(TokenKind.Ingest);
            var token = IngestToken.Issue(
                Guid.CreateVersion7(), minted.Identifier, Cipher.Encrypt(minted.Secret),
                Clock.GetUtcNow());

            Tokens.AddAsync(token, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

            return token;
        }

        public async Task<Unclaimed> StartedAsync()
        {
            await Open.ExecuteAsync(TestContext.Current.CancellationToken);

            return this;
        }

        /// <summary>The claim, walked the way the operator walks it.</summary>
        public Task<ClaimAttempt> ClaimWith(string? secret, string? seenFrom = null) =>
            Claim.ExecuteAsync(
                TheirPassword, secret, seenFrom, TestContext.Current.CancellationToken);
    }
}
