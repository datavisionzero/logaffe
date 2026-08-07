using System.Security.Cryptography;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a claim ended.
/// </summary>
/// <remarks>
/// Unlike a sign-in, which answers every refusal with one refusal, this says
/// which step failed. There is nothing to protect: the installation is open to
/// whoever reaches it by design (<c>docs/setup.md</c>), the person on the other
/// end is setting up their own installation, and telling them the code did not
/// verify rather than "no" is the difference between finishing and giving up.
/// </remarks>
public enum ClaimOutcome
{
    /// <summary>The installation has an operator, and it is this one.</summary>
    Claimed,

    /// <summary>
    /// Somebody else got there first, or it was claimed long ago. It is the
    /// same answer either way, and the loser learns it here rather than at the
    /// beginning — the price of holding nothing (ADR 0014).
    /// </summary>
    AlreadyClaimed,

    /// <summary>
    /// The thirty minutes are up. Claiming over the network is over and the way
    /// back is the host (ADR 0013).
    /// </summary>
    WindowClosed,

    /// <summary>Shorter than a password may be, or longer than one is hashed.</summary>
    PasswordNotOne,

    /// <summary>
    /// The ticket is not one this installation sealed, or it belongs to a
    /// window that is no longer the current one (ADR 0035). The claimant starts
    /// the enrolment again.
    /// </summary>
    EnrolmentNotOurs,

    /// <summary>
    /// The six digits are not what the drawn secret produces now. It is the
    /// step that proves the authenticator app really holds the enrolment, and a
    /// phone whose clock is out fails here rather than at the first sign-in.
    /// </summary>
    SecondFactorRefused,

    /// <summary>
    /// The code typed back is not one of the ten. It is the step that proves the
    /// sheet was actually kept, which is the only thing standing between the
    /// operator and Host Recovery on the day they lose their phone.
    /// </summary>
    BackupCodeRefused,
}

/// <summary>
/// The end of a claim, and the session it hands out when it succeeded.
/// </summary>
/// <remarks>
/// The claim signs the operator in, because the alternative is a screen that
/// congratulates somebody and then asks them for the password they chose four
/// seconds ago.
/// </remarks>
public sealed record ClaimAttempt(
    ClaimOutcome Outcome, SessionSecret? Secret, Session? Session);

/// <summary>
/// A stranger taking an installation nobody owns, which is the only act the
/// unclaimed surface offers.
/// </summary>
/// <remarks>
/// <para>
/// It is one act and it stores nothing until it succeeds (ADR 0014). Everything
/// the operator established on the way here — the password they chose, the
/// authenticator they enrolled, the sheet they printed — arrives in this one
/// request, the enrolment in the sealed ticket the previous step handed them
/// (ADR 0035), and either all of it is written or none of it is.
/// </para>
/// <para>
/// <b>The database decides the race.</b> Two claimants both walk the whole flow
/// and both reach here; what settles it is the account table holding one row,
/// not the check this could have run first and been wrong about a moment later.
/// </para>
/// <para>
/// The steps are ordered by what they cost. The window and the account are one
/// small read each, the ticket is one decryption, the two codes are arithmetic —
/// and hashing the password is deliberately slow, so it happens once everything
/// else has already said yes.
/// </para>
/// </remarks>
public sealed class ClaimTheInstallation(
    IInstallation installation,
    IOperators operators,
    ISessions sessions,
    IPasswordHasher hasher,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <param name="backupCode">
    /// One of the ten, typed back off the sheet, read however it was typed.
    /// </param>
    /// <param name="seenFrom">
    /// Where the request came from, which the session this hands out is listed
    /// with from its first moment.
    /// </param>
    public async Task<ClaimAttempt> ExecuteAsync(
        string? password,
        string? ticket,
        string? secondFactorCode,
        string? backupCode,
        string? seenFrom,
        CancellationToken cancellationToken)
    {
        // Asked as its own question rather than as a null check on the account
        // row, so that this path does not read a credential to answer whether
        // there is one.
        if (await operators.IsClaimedAsync(cancellationToken))
        {
            return Refused(ClaimOutcome.AlreadyClaimed);
        }

        var now = clock.GetUtcNow();

        var window = await installation.ReadClaimWindowAsync(cancellationToken);
        if (window is null || !window.IsOpenAt(now))
        {
            return Refused(ClaimOutcome.WindowClosed);
        }

        // The shape before the hasher, as on the sign-in: hashing is slow and
        // this surface is public, so a megabyte of input is refused here rather
        // than inside PBKDF2.
        if (!Password.TryCreate(password, out var chosen))
        {
            return Refused(ClaimOutcome.PasswordNotOne);
        }

        // A ticket names the window it was drawn in, so one drawn before a Host
        // Recovery is refused after it: the installation's notion of who may
        // claim it changed, and the enrolment that was in flight belongs to the
        // installation that no longer exists.
        if (!ClaimTicket.TryOpen(ticket, cipher, out var enrolment)
            || enrolment.WindowOpenedAt != window.OpenedAt)
        {
            return Refused(ClaimOutcome.EnrolmentNotOurs);
        }

        if (!secondFactor.Verifies(enrolment.SecondFactorSecret, secondFactorCode, now))
        {
            return Refused(ClaimOutcome.SecondFactorRefused);
        }

        // Compared against the hashes in the ticket rather than against rows,
        // because the rows cannot exist yet: a backup code hangs off an operator
        // this act has not created. It is the comparison `BackupCode.Matches`
        // makes and it is made the same way, in constant time.
        if (!BackupCodeText.TryParse(backupCode, out var confirmed)
            || !enrolment.BackupCodeHashes.Any(
                hash => CryptographicOperations.FixedTimeEquals(hash, confirmed.Hash)))
        {
            return Refused(ClaimOutcome.BackupCodeRefused);
        }

        var theOperator = Operator.Claim(
            hasher.Hash(chosen), cipher.Encrypt(enrolment.SecondFactorSecret), now);

        var claimed = await operators.TryClaimAsync(
            theOperator,
            BackupCode.SetOf(theOperator.Id, enrolment.BackupCodeHashes, now),
            cancellationToken);

        // The other claimant confirmed their sheet first. Nothing here was
        // written — it was one transaction — and this is the screen ADR 0014
        // describes.
        if (!claimed)
        {
            return Refused(ClaimOutcome.AlreadyClaimed);
        }

        var secret = SessionSecret.Mint();
        var session = Session.Start(theOperator.Id, secret, seenFrom, now);
        await sessions.AddAsync(session, cancellationToken);

        return new ClaimAttempt(ClaimOutcome.Claimed, secret, session);
    }

    private static ClaimAttempt Refused(ClaimOutcome outcome) => new(outcome, null, null);
}
