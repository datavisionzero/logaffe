using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// A session, and what the operator is told about the code they spent to get it.
/// </summary>
/// <remarks>
/// The secret is here because handing it over is the act, exactly as it is for a
/// token — but unlike a token this is the only time it exists. A session secret
/// is stored as a fast hash and is not readable back (ADR 0032), so a browser
/// that loses it signs in again.
/// </remarks>
/// <param name="Secret">
/// What the browser holds from now on. It is put into a cookie by the adapter
/// and appears nowhere else.
/// </param>
/// <param name="Session">The row, which is what the operator sees in their list.</param>
/// <param name="BackupCodesRemaining">
/// How many codes are left, when one was spent getting in, and <c>null</c> when
/// the second factor itself was used. <c>docs/sign-in.md</c> requires the count
/// to be said whenever one is spent, because a set that quietly runs out ends at
/// Host Recovery.
/// </param>
public sealed record SignedIn(
    SessionSecret Secret, Session Session, int? BackupCodesRemaining);

/// <summary>
/// The operator proving both factors and getting a session for it.
/// </summary>
/// <remarks>
/// <para>
/// There is nothing to say which account is meant — there is one, and it has no
/// username and no email address (ADR 0015) — so what arrives is a password and,
/// when the account has a second factor, either the six digits or a backup code
/// standing in for them (<c>docs/sign-in.md</c>). The second factor is the
/// operator's to enrol (ADR 0041), so an account that has none signs in on the
/// password alone.
/// </para>
/// <para>
/// <b>Every refusal is the same refusal.</b> A wrong password, a wrong code, a
/// code already spent and an installation that has no operator at all are one
/// <c>null</c>, and the screen says one thing. Which half was wrong is not a
/// fact this surface gives away, and it is not a fact the operator needs: they
/// are the only person who could be here.
/// </para>
/// <para>
/// <b>Nothing here locks anything</b> (ADR 0017). With exactly one account a
/// lockout is a weapon pointed at its owner, so a failed attempt writes nothing
/// at all and what holds the guessing back is the throttle in the adapter and
/// the second factor itself.
/// </para>
/// </remarks>
public sealed class SignIn(
    IOperators operators,
    ISessions sessions,
    IPasswordHasher hasher,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <summary>
    /// The session both factors bought, or <c>null</c> when they did not.
    /// </summary>
    /// <param name="secondFactorCode">
    /// The six digits from the authenticator app, or <c>null</c> when a backup
    /// code is being given instead.
    /// </param>
    /// <param name="backupCode">
    /// A backup code standing in for the second factor, read however it was
    /// typed. It is consumed by getting in, and it is refused if it was consumed
    /// before.
    /// </param>
    /// <param name="seenFrom">
    /// Where the request came from, which is the column that makes the session
    /// list a security surface rather than a convenience.
    /// </param>
    public async Task<SignedIn?> ExecuteAsync(
        string? password,
        string? secondFactorCode,
        string? backupCode,
        string? seenFrom,
        CancellationToken cancellationToken)
    {
        // The shape first, and before the hasher: hashing is deliberately slow
        // and this surface is public, so a megabyte of input is refused here
        // rather than inside PBKDF2. The minimum length is not applied — it is a
        // rule about choosing a password, and applying it to one being presented
        // would lock out an operator whose password was long enough when they
        // set it (ADR 0042).
        if (!Password.TryRead(password, out var presented))
        {
            return null;
        }

        // An unclaimed installation has nothing to compare against, and no
        // timing to defend: it exposes the claim and nothing else, and it says
        // so plainly (docs/setup.md). Being told there is no operator here is
        // not a disclosure — it is the screen.
        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null)
        {
            return null;
        }

        var check = hasher.Verify(theOperator.PasswordHash, presented);
        if (check is PasswordCheck.Wrong)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        // An account with no second factor has nothing to prove past the
        // password, and anything sent alongside it is ignored rather than
        // refused: what the operator decided is what the sign-in asks for
        // (ADR 0041).
        var spent = !theOperator.HasSecondFactor
            ? NoCodeWasSpent
            : secondFactorCode is null
                ? await SpendBackupCodeAsync(backupCode, now, cancellationToken)
                : VerifiesSecondFactor(theOperator, secondFactorCode, now)
                    ? NoCodeWasSpent
                    : null;

        if (spent is null)
        {
            return null;
        }

        // Only now, and only on the way in. A correct password with a wrong
        // second factor is not a sign-in and must not leave a trace on the row,
        // which is what keeps `RehashedTo` maintenance nobody asked for rather
        // than something an attempt can trigger (ADR 0032).
        if (check is PasswordCheck.RightAndOutOfDate)
        {
            theOperator.RehashedTo(hasher.Hash(presented));
            await operators.RecordAsync(theOperator, cancellationToken);
        }

        var secret = SessionSecret.Mint();
        var session = Session.Start(theOperator.Id, secret, seenFrom, now);
        await sessions.AddAsync(session, cancellationToken);

        return new SignedIn(secret, session, spent.Remaining);
    }

    /// <summary>
    /// What a sign-in that used the authenticator app itself has to say about
    /// backup codes, which is nothing.
    /// </summary>
    private static SpentBackupCode NoCodeWasSpent => new(null);

    private bool VerifiesSecondFactor(
        Operator theOperator, string code, DateTimeOffset now) =>
        secondFactor.Verifies(
            cipher.Decrypt(theOperator.EncryptedSecondFactorSecret!), code, now);

    /// <summary>
    /// Spends the presented backup code, or answers <c>null</c> when it is not
    /// one, is not the operator's, or has been spent already.
    /// </summary>
    /// <remarks>
    /// The set is read whole and every code in it is compared, because there is
    /// no identifier naming a row here the way there is on a token (ADR 0031):
    /// a code carries all of its own entropy and the operator holds ten of them.
    /// The loop does not stop at the one that matched — returning early is
    /// exactly what would say where in the set it sat.
    /// </remarks>
    private async Task<SpentBackupCode?> SpendBackupCodeAsync(
        string? backupCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!BackupCodeText.TryParse(backupCode, out var presented))
        {
            return null;
        }

        var codes = await operators.ListBackupCodesAsync(cancellationToken);

        BackupCode? matched = null;
        foreach (var code in codes)
        {
            if (code.Matches(presented))
            {
                matched ??= code;
            }
        }

        // A spent code matches exactly as a fresh one does — the domain says so
        // on purpose — so refusing it is here, and a code offered twice costs
        // what a code offered once costs.
        if (matched is null || matched.IsSpent)
        {
            return null;
        }

        matched.ConsumeAt(now);
        await operators.RecordConsumptionAsync(matched, cancellationToken);

        return new SpentBackupCode(codes.Count(code => !code.IsSpent));
    }

    /// <summary>
    /// That the second factor was satisfied, and how many codes are left if it
    /// was satisfied by spending one.
    /// </summary>
    private sealed record SpentBackupCode(int? Remaining);
}
