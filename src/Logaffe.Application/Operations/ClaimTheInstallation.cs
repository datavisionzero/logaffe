using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a claim ended.
/// </summary>
/// <remarks>
/// Unlike a sign-in, which answers every refusal with one refusal, this says
/// which step failed. There is little to protect and much to lose by being
/// unhelpful: the person on the other end is setting up their own installation,
/// and telling them the secret did not match rather than "no" is the difference
/// between finishing and giving up. What it never says is anything about the
/// secret beyond whether the one presented was right.
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
    /// Window mode, and the thirty minutes are up. Claiming over the network is
    /// over and the way back is the host (ADR 0013).
    /// </summary>
    WindowClosed,

    /// <summary>
    /// Secret mode, and there is no secret to compare against: configuration
    /// named none and the installation holds none. It is a start that never drew
    /// one — a database made by hand, or a first start that stopped between
    /// writing the hash and being asked — and the host command is the way out of
    /// it, as it is for a lapsed window.
    /// </summary>
    NoSecretToPresentTo,

    /// <summary>
    /// The secret presented is not the one that guards this installation
    /// (ADR 0040). It is the only thing this refusal says.
    /// </summary>
    SecretRefused,

    /// <summary>Shorter than a password may be, or longer than one is hashed.</summary>
    PasswordNotOne,
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
/// It is one request and it establishes one thing: a password (ADR 0041). The
/// second factor is not here — it is the operator's to enrol afterwards from the
/// settings — and with nothing to carry between two steps there is nothing to
/// seal and nothing to hold. The claim stores nothing until it succeeds, and a
/// claim that is abandoned leaves the installation exactly as unclaimed as it was
/// (ADR 0014).
/// </para>
/// <para>
/// <b>The database decides the race.</b> Two claimants both reach here; what
/// settles it is the account table holding one row, not the check this could have
/// run first and been wrong about a moment later.
/// </para>
/// <para>
/// The steps are ordered by what they cost. The account and the guard are one
/// small read each, the secret is one SHA-256 — and hashing the password is
/// deliberately slow, so it happens once everything else has already said yes.
/// </para>
/// </remarks>
public sealed class ClaimTheInstallation(
    IInstallation installation,
    IOperators operators,
    ISessions sessions,
    IClaimSecretHandover handover,
    IPasswordHasher hasher,
    ClaimSettings settings,
    TimeProvider clock)
{
    /// <param name="secret">
    /// The claim secret, as it was read off the file or out of the compose file.
    /// Ignored in window mode, where there is none to present.
    /// </param>
    /// <param name="seenFrom">
    /// Where the request came from, which the session this hands out is listed
    /// with from its first moment.
    /// </param>
    public async Task<ClaimAttempt> ExecuteAsync(
        string? password,
        string? secret,
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

        var guard = await installation.ReadClaimGuardAsync(cancellationToken);
        if (guard is null)
        {
            return Refused(
                settings.Mode is ClaimMode.Secret
                    ? ClaimOutcome.NoSecretToPresentTo
                    : ClaimOutcome.WindowClosed);
        }

        var admitted = Admits(guard, secret, now);
        if (admitted is not ClaimOutcome.Claimed)
        {
            return Refused(admitted);
        }

        // The shape before the hasher, as on the sign-in: hashing is slow and
        // this surface is public, so a megabyte of input is refused here rather
        // than inside PBKDF2.
        if (!Password.TryCreate(password, out var chosen))
        {
            return Refused(ClaimOutcome.PasswordNotOne);
        }

        var theOperator = Operator.Claim(hasher.Hash(chosen), now);

        var claimed = await operators.TryClaimAsync(theOperator, cancellationToken);

        // The other claimant sent theirs first. Nothing here was written — it was
        // one statement — and this is the screen ADR 0014 describes.
        if (!claimed)
        {
            return Refused(ClaimOutcome.AlreadyClaimed);
        }

        // The file the secret was handed over in is a delivery copy, and what it
        // delivered has arrived. Leaving it would leave a credential for a door
        // that no longer opens.
        handover.Remove();

        var sessionSecret = SessionSecret.Mint();
        var session = Session.Start(theOperator.Id, sessionSecret, seenFrom, now);
        await sessions.AddAsync(session, cancellationToken);

        return new ClaimAttempt(ClaimOutcome.Claimed, sessionSecret, session);
    }

    /// <summary>
    /// Whether this request gets as far as choosing a password, which is the one
    /// place the two modes differ.
    /// </summary>
    /// <remarks>
    /// A supplied secret is compared against configuration and a drawn one
    /// against the hash in the row; both comparisons are made through the hashes
    /// and take the same time whatever was typed. In window mode nothing is
    /// presented and the deadline is the whole of the guard.
    /// </remarks>
    private ClaimOutcome Admits(ClaimGuard guard, string? presented, DateTimeOffset now)
    {
        if (settings.Mode is ClaimMode.Window)
        {
            return guard.WindowIsOpenAt(now)
                ? ClaimOutcome.Claimed
                : ClaimOutcome.WindowClosed;
        }

        if (settings.SuppliedSecret is null && !guard.HasDrawnSecret)
        {
            return ClaimOutcome.NoSecretToPresentTo;
        }

        if (!ClaimSecret.TryRead(presented, out var offered))
        {
            return ClaimOutcome.SecretRefused;
        }

        var right = settings.SuppliedSecret is { } supplied
            ? offered.Matches(supplied)
            : guard.AdmitsDrawn(offered);

        return right ? ClaimOutcome.Claimed : ClaimOutcome.SecretRefused;
    }

    private static ClaimAttempt Refused(ClaimOutcome outcome) => new(outcome, null, null);
}
