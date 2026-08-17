using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How an enrolment ended.
/// </summary>
/// <remarks>
/// It says which step refused, as the claim does and for a reason of its own:
/// the request came from a session, so there is nobody here but the operator,
/// and somebody enrolling with a phone in one hand needs to know whether it was
/// the old code, the new one or the password that did not take.
/// </remarks>
public enum EnrolmentOutcome
{
    /// <summary>
    /// The row holds the new secret, the sheet shown with it is the operator's,
    /// and every other session is over.
    /// </summary>
    Enrolled,

    /// <summary>
    /// The password was not it — or there is no account, in the moment after a
    /// Host Recovery.
    /// </summary>
    PasswordRefused,

    /// <summary>
    /// The second factor in use was not proved: neither the six digits it
    /// produces now nor an unspent backup code. It cannot be the answer on an
    /// account that has none, where there is nothing in use to prove.
    /// </summary>
    SecondFactorRefused,

    /// <summary>
    /// The ticket is not one this installation sealed, belongs to an account
    /// that is not this one, or was drawn more than
    /// <see cref="EnrolmentTicket.Lifetime"/> ago. The operator starts the
    /// enrolment again, which costs them a QR code and nothing else.
    /// </summary>
    EnrolmentNotOurs,

    /// <summary>
    /// The six digits from the newly enrolled app are not what the drawn secret
    /// produces now. It is the step that proves the app really holds the
    /// enrolment, so that a phone whose clock is out fails here rather than at
    /// the next sign-in — which is the sign-in that would have no second factor
    /// to offer.
    /// </summary>
    NewSecondFactorRefused,
}

/// <summary>
/// The operator enrolling a second factor, or replacing the one they have.
/// </summary>
/// <remarks>
/// <para>
/// It asks for everything at once: the password, a code from the app just
/// enrolled, and — when the account already has a second factor — the current
/// code or a backup code standing in for it, which is the case of the phone that
/// is already gone. The enrolment itself arrives in the sealed ticket the previous
/// step handed over. Either all of it is written or none of it is.
/// </para>
/// <para>
/// <b>An account with no second factor has nothing in use to prove</b>
/// (ADR 0041), so a first enrolment asks for the password and the new code and
/// stops there. It is not a weaker act than a replacement: what stands in front
/// of both is the password, and there is no set of backup codes to get past
/// because there is none.
/// </para>
/// <para>
/// <b>It issues the sheet of backup codes with it</b>, shown at the step before
/// this one. An enrolment is exactly the event after which an old sheet should
/// not be the way back to an old authenticator, and ADR 0032 has the set replaced
/// wholesale rather than topped up.
/// </para>
/// <para>
/// <b>A backup code offered here is not spent.</b> The set it belongs to is
/// replaced by this same act a moment later, so consuming it would be writing
/// down a fact about a row that is about to be gone — and an enrolment that then
/// fails on the new code would have cost the operator one of the codes they have
/// left.
/// </para>
/// <para>
/// The steps are ordered by what they cost, as the claim's are: one read, one
/// decryption, then arithmetic, and the slow hash last.
/// </para>
/// </remarks>
public sealed class EnrolTheSecondFactor(
    IOperators operators,
    ISessions sessions,
    IPasswordHasher hasher,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <param name="secondFactorCode">
    /// The six digits the app in use produces now, or <c>null</c> when a backup
    /// code is being given instead — or when there is no second factor in use at
    /// all.
    /// </param>
    /// <param name="newSecondFactorCode">
    /// The six digits from the app just enrolled, which is what proves it holds
    /// the secret in the ticket.
    /// </param>
    /// <param name="keeping">
    /// The session making the request, which is the one that survives.
    /// </param>
    public async Task<EnrolmentOutcome> ExecuteAsync(
        string? password,
        string? secondFactorCode,
        string? backupCode,
        string? newSecondFactorCode,
        string? ticket,
        Session keeping,
        CancellationToken cancellationToken)
    {
        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null || !Password.TryCreate(password, out var presented))
        {
            return EnrolmentOutcome.PasswordRefused;
        }

        var now = clock.GetUtcNow();

        if (!EnrolmentTicket.TryOpen(ticket, cipher, out var enrolment)
            || !enrolment.BelongsTo(theOperator, now))
        {
            return EnrolmentOutcome.EnrolmentNotOurs;
        }

        if (!await ProvesTheSecondFactorInUseAsync(
            theOperator, secondFactorCode, backupCode, now, cancellationToken))
        {
            return EnrolmentOutcome.SecondFactorRefused;
        }

        if (!secondFactor.Verifies(enrolment.SecondFactorSecret, newSecondFactorCode, now))
        {
            return EnrolmentOutcome.NewSecondFactorRefused;
        }

        if (hasher.Verify(theOperator.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return EnrolmentOutcome.PasswordRefused;
        }

        theOperator.EnrolSecondFactor(cipher.Encrypt(enrolment.SecondFactorSecret), now);
        await operators.RecordAsync(theOperator, cancellationToken);

        await operators.ReplaceBackupCodesAsync(
            BackupCode.SetOf(theOperator.Id, enrolment.BackupCodeHashes, now),
            cancellationToken);

        await sessions.RemoveEveryOtherAsync(keeping, cancellationToken);

        return EnrolmentOutcome.Enrolled;
    }

    /// <summary>
    /// Whether the second factor the account holds today was proved, by the app
    /// that holds it or by one of the codes that stands in for it — and
    /// vacuously true on an account that holds none.
    /// </summary>
    /// <remarks>
    /// The set is read whole and every code compared, as a sign-in does it: a
    /// code carries all of its own entropy and there is no identifier naming its
    /// row (ADR 0031). A spent one is refused here rather than by the comparison,
    /// which matches a spent code exactly as it matches a fresh one.
    /// </remarks>
    private async Task<bool> ProvesTheSecondFactorInUseAsync(
        Operator theOperator,
        string? secondFactorCode,
        string? backupCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!theOperator.HasSecondFactor)
        {
            return true;
        }

        if (secondFactorCode is not null)
        {
            return secondFactor.Verifies(
                cipher.Decrypt(theOperator.EncryptedSecondFactorSecret!),
                secondFactorCode,
                now);
        }

        if (!BackupCodeText.TryParse(backupCode, out var presented))
        {
            return false;
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

        return matched is { IsSpent: false };
    }
}
