using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The operator's own credentials: the password, the second factor and the
/// backup codes.
/// </summary>
public sealed class OperatorCredentialActsTests
{
    private const string TheirPassword = "a passphrase they typed";
    private const string TheirNewPassword = "the passphrase they moved to";

    private static readonly DateTimeOffset Claimed =
        new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryOperators _operators = new();
    private readonly InMemorySessions _sessions = new();
    private readonly StubPasswordHasher _hasher = new();
    private readonly MintingSecondFactor _secondFactor = new();
    private readonly ReversingCipher _cipher = new();
    private readonly StoppedClock _clock = new(Claimed.AddDays(1));

    private readonly Session _asking;
    private readonly Session _elsewhere;
    private readonly Operator _theOperator;
    private readonly IReadOnlyList<BackupCodeText> _backupCodes;
    private readonly string _enrolledSecret;

    public OperatorCredentialActsTests()
    {
        _enrolledSecret = _secondFactor.MintSecret();
        _theOperator = Operator.Claim(
            StubPasswordHasher.HashOf(TheirPassword),
            _cipher.Encrypt(_enrolledSecret),
            Claimed);

        _backupCodes = _operators.Claim(_theOperator, Claimed).Shown;

        _asking = Seed();
        _elsewhere = Seed();
    }

    [Fact]
    public async Task A_password_change_takes_the_current_one_and_ends_every_other_session()
    {
        var outcome = await ChangePassword(TheirPassword, TheirNewPassword);

        Assert.Equal(PasswordChangeOutcome.Changed, outcome);
        Assert.Equal(StubPasswordHasher.HashOf(TheirNewPassword), _theOperator.PasswordHash);

        // Ending every other session is what makes a password change worth
        // reaching for after a cookie has gone somewhere it should not have.
        Assert.Equal([_asking], _sessions.Stored);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing_and_ends_nothing()
    {
        var outcome = await ChangePassword("some other passphrase", TheirNewPassword);

        Assert.Equal(PasswordChangeOutcome.CurrentPasswordRefused, outcome);
        Assert.Equal(StubPasswordHasher.HashOf(TheirPassword), _theOperator.PasswordHash);
        Assert.Equal(0, _operators.Writes);
        Assert.Equal(0, _sessions.Writes);
    }

    [Fact]
    public async Task A_chosen_password_below_the_minimum_never_reaches_the_hasher()
    {
        var outcome = await ChangePassword(TheirPassword, "short");

        Assert.Equal(PasswordChangeOutcome.ChosenPasswordNotOne, outcome);
        Assert.Equal(0, _hasher.Verifications);
        Assert.Equal(0, _hasher.Hashes);
    }

    [Fact]
    public async Task A_fresh_sheet_replaces_the_previous_set_and_ends_no_session()
    {
        var spent = _operators.BackupCodes[0];
        spent.ConsumeAt(_clock.Now);

        var shown = await IssueBackupCodes(TheirPassword);

        Assert.NotNull(shown);
        Assert.Equal(BackupCode.SetSize, shown.Count);

        // Wholesale (ADR 0032): the spent ones go with the rest, and nothing of
        // the old sheet survives.
        Assert.Equal(BackupCode.SetSize, _operators.BackupCodes.Count);
        Assert.DoesNotContain(spent, _operators.BackupCodes);
        Assert.All(_operators.BackupCodes, code => Assert.False(code.IsSpent));

        // Replacing the codes is not one of the ways a session ends
        // (docs/sign-in.md).
        Assert.Equal([_asking, _elsewhere], _sessions.Stored);
    }

    [Fact]
    public async Task A_fresh_sheet_is_refused_without_the_password()
    {
        Assert.Null(await IssueBackupCodes("some other passphrase"));

        // Ten of these are ten ways past the second factor, so an unlocked
        // browser on its own does not get them.
        Assert.Equal(0, _operators.Writes);
    }

    [Fact]
    public async Task A_re_enrolment_replaces_the_secret_the_sheet_and_every_other_session()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            TheirPassword,
            _secondFactor.CodeFor(_enrolledSecret),
            null,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            drawn.Ticket);

        Assert.Equal(ReEnrolmentOutcome.ReEnrolled, outcome);

        // The row holds the new secret sealed, and the previous one is gone
        // rather than kept beside it.
        Assert.Equal(
            drawn.SecondFactorSecret,
            _cipher.Decrypt(_theOperator.EncryptedSecondFactorSecret));
        Assert.Equal(_clock.Now, _theOperator.SecondFactorEnrolledAt);

        // The sheet shown with it is the operator's now, and it is the one that
        // was shown — the ticket carried the hashes, so these are the same ten.
        Assert.Equal(
            [.. drawn.BackupCodes.Select(code => Convert.ToHexString(code.Hash))],
            [.. _operators.BackupCodes.Select(code => Convert.ToHexString(code.Hash))]);

        Assert.Equal([_asking], _sessions.Stored);
    }

    [Fact]
    public async Task A_backup_code_stands_in_for_a_phone_that_is_already_gone()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            TheirPassword,
            null,
            _backupCodes[0].Display,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            drawn.Ticket);

        Assert.Equal(ReEnrolmentOutcome.ReEnrolled, outcome);

        // It is not spent: the set it belongs to is replaced by this same act a
        // moment later, so consuming it would be a fact written about a row that
        // is about to be gone.
        Assert.All(_operators.BackupCodes, code => Assert.False(code.IsSpent));
    }

    [Fact]
    public async Task A_ticket_this_installation_did_not_seal_is_refused()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            TheirPassword,
            _secondFactor.CodeFor(_enrolledSecret),
            null,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            "not-a-ticket-this-installation-sealed");

        Assert.Equal(ReEnrolmentOutcome.EnrolmentNotOurs, outcome);
        Assert.Equal(0, _operators.Writes);
    }

    [Fact]
    public async Task A_ticket_drawn_too_long_ago_is_refused()
    {
        var drawn = await BeginReEnrolment();
        _clock.Now += ReEnrolmentTicket.Lifetime + TimeSpan.FromMinutes(1);

        var outcome = await ReEnrol(
            TheirPassword,
            _secondFactor.CodeFor(_enrolledSecret),
            null,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            drawn.Ticket);

        // The operator starts the enrolment again, which costs them a QR code
        // and nothing else.
        Assert.Equal(ReEnrolmentOutcome.EnrolmentNotOurs, outcome);
        Assert.Equal(0, _operators.Writes);
    }

    [Fact]
    public async Task A_code_the_new_app_does_not_produce_leaves_the_second_factor_alone()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            TheirPassword,
            _secondFactor.CodeFor(_enrolledSecret),
            null,
            "000000",
            drawn.Ticket);

        // The step that proves the app really holds the enrolment. Failing it
        // here is an afternoon; failing it at the next sign-in is a phone that
        // cannot produce the code the installation now wants.
        Assert.Equal(ReEnrolmentOutcome.NewSecondFactorRefused, outcome);
        Assert.Equal(_enrolledSecret, _cipher.Decrypt(_theOperator.EncryptedSecondFactorSecret));
        Assert.Equal(0, _operators.Writes);
        Assert.Equal(2, _sessions.Stored.Count);
    }

    [Fact]
    public async Task The_second_factor_in_use_has_to_be_proved()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            TheirPassword,
            "000000",
            null,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            drawn.Ticket);

        // Otherwise an unlocked browser is enough to replace the factor that
        // makes public exposure defensible (ADR 0016).
        Assert.Equal(ReEnrolmentOutcome.SecondFactorRefused, outcome);
        Assert.Equal(0, _operators.Writes);
    }

    [Fact]
    public async Task A_re_enrolment_without_the_password_writes_nothing()
    {
        var drawn = await BeginReEnrolment();

        var outcome = await ReEnrol(
            "some other passphrase",
            _secondFactor.CodeFor(_enrolledSecret),
            null,
            _secondFactor.CodeFor(drawn.SecondFactorSecret),
            drawn.Ticket);

        Assert.Equal(ReEnrolmentOutcome.PasswordRefused, outcome);
        Assert.Equal(0, _operators.Writes);
        Assert.Equal(0, _sessions.Writes);
    }

    [Fact]
    public async Task The_step_that_draws_the_enrolment_stores_nothing()
    {
        var drawn = await BeginReEnrolment();

        Assert.NotNull(drawn);
        Assert.Equal(BackupCode.SetSize, drawn.BackupCodes.Count);
        Assert.Contains(drawn.SecondFactorSecret, drawn.EnrolmentUri);

        // The second factor that worked this morning still works this evening,
        // and the sheet in the operator's drawer is still the one that admits.
        Assert.Equal(_enrolledSecret, _cipher.Decrypt(_theOperator.EncryptedSecondFactorSecret));
        Assert.Equal(0, _operators.Writes);
        Assert.Equal(0, _sessions.Writes);
    }

    private Session Seed() => _sessions.Seed(
        Session.Start(_theOperator.Id, SessionSecret.Mint(), "203.0.113.7", Claimed));

    private Task<PasswordChangeOutcome> ChangePassword(string? current, string? chosen) =>
        new ChangePassword(_operators, _sessions, _hasher).ExecuteAsync(
            current, chosen, _asking, TestContext.Current.CancellationToken);

    private Task<IReadOnlyList<BackupCodeText>?> IssueBackupCodes(string? password) =>
        new IssueBackupCodes(_operators, _hasher, _clock).ExecuteAsync(
            password, TestContext.Current.CancellationToken);

    private async Task<ReEnrolment> BeginReEnrolment()
    {
        var drawn = await new BeginReEnrolment(_operators, _secondFactor, _cipher, _clock)
            .ExecuteAsync("logs.example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(drawn);

        return drawn;
    }

    private Task<ReEnrolmentOutcome> ReEnrol(
        string? password,
        string? secondFactorCode,
        string? backupCode,
        string? newSecondFactorCode,
        string? ticket) =>
        new ReEnrolTheSecondFactor(
                _operators, _sessions, _hasher, _secondFactor, _cipher, _clock)
            .ExecuteAsync(
                password,
                secondFactorCode,
                backupCode,
                newSecondFactorCode,
                ticket,
                _asking,
                TestContext.Current.CancellationToken);
}

/// <summary>
/// A second factor that mints a fresh secret every time and answers one code per
/// secret, which is what a re-enrolment needs of it: the old app and the new one
/// hold different secrets and must not produce the same digits.
/// </summary>
internal sealed class MintingSecondFactor : ISecondFactor
{
    private int _minted;

    public string MintSecret() => $"the-secret-{++_minted}";

    /// <summary>The six digits <paramref name="secret"/> stands for here.</summary>
    public string CodeFor(string secret) =>
        (BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(secret))) % 1_000_000)
        .ToString("D6");

    public bool Verifies(string secret, string? code, DateTimeOffset at) =>
        code == CodeFor(secret);

    public string EnrolmentUri(string secret, string account) =>
        $"otpauth://totp/logaffe:{account}?secret={secret}";
}
