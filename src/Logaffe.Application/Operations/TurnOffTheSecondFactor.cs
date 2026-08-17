using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How turning the second factor off ended.
/// </summary>
public enum TurningOffOutcome
{
    /// <summary>
    /// The row holds no second factor, the backup codes are gone with it, and
    /// every other session is over.
    /// </summary>
    TurnedOff,

    /// <summary>
    /// The password was not it — or there is no account, in the moment after a
    /// Host Recovery.
    /// </summary>
    PasswordRefused,

    /// <summary>
    /// Neither the six digits the app produces now nor an unspent backup code.
    /// </summary>
    SecondFactorRefused,

    /// <summary>
    /// There is nothing to turn off. It is not a failure of anything the operator
    /// did, and the screen that offers this act does not offer it in this state —
    /// so it is the answer to a request that raced a different browser.
    /// </summary>
    NoSecondFactor,
}

/// <summary>
/// The operator deciding their installation runs behind a password alone.
/// </summary>
/// <remarks>
/// <para>
/// It is theirs to decide (ADR 0041) and it costs exactly what enrolling costs:
/// the password, and the second factor in use — the current code, or a backup
/// code standing in for it. A session that has been taken is not a session that
/// can strip the account down to one credential, and the act that removes a
/// factor is not cheaper than the act that added one.
/// </para>
/// <para>
/// <b>The backup codes go with it.</b> A code that stands in for a second factor
/// that is not there stands in for nothing, and leaving the set behind would
/// leave ten values that look like a way in on an account where the only way in
/// is the password.
/// </para>
/// <para>
/// <b>A backup code offered here is not spent</b>, for the reason it is not spent
/// on an enrolment: the set is deleted by this same act a moment later.
/// </para>
/// <para>
/// It ends every other session, as every change to the second factor does. The
/// point of the session list is that the operator notices when somebody else is
/// signed in, and this is a moment worth noticing.
/// </para>
/// </remarks>
public sealed class TurnOffTheSecondFactor(
    IOperators operators,
    ISessions sessions,
    IPasswordHasher hasher,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <param name="keeping">
    /// The session making the request, which is the one that survives.
    /// </param>
    public async Task<TurningOffOutcome> ExecuteAsync(
        string? password,
        string? secondFactorCode,
        string? backupCode,
        Session keeping,
        CancellationToken cancellationToken)
    {
        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null || !Password.TryRead(password, out var presented))
        {
            return TurningOffOutcome.PasswordRefused;
        }

        if (!theOperator.HasSecondFactor)
        {
            return TurningOffOutcome.NoSecondFactor;
        }

        var now = clock.GetUtcNow();

        if (!await ProvesTheSecondFactorAsync(
            theOperator, secondFactorCode, backupCode, now, cancellationToken))
        {
            return TurningOffOutcome.SecondFactorRefused;
        }

        if (hasher.Verify(theOperator.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return TurningOffOutcome.PasswordRefused;
        }

        theOperator.RemoveSecondFactor();
        await operators.RecordAsync(theOperator, cancellationToken);

        await operators.ReplaceBackupCodesAsync([], cancellationToken);
        await sessions.RemoveEveryOtherAsync(keeping, cancellationToken);

        return TurningOffOutcome.TurnedOff;
    }

    /// <inheritdoc cref="EnrolTheSecondFactor"/>
    private async Task<bool> ProvesTheSecondFactorAsync(
        Operator theOperator,
        string? secondFactorCode,
        string? backupCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
