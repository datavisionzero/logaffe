using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How asking for a fresh sheet ended.
/// </summary>
public enum SheetOutcome
{
    /// <summary>Ten codes, and the previous set is gone.</summary>
    Issued,

    /// <summary>
    /// The password was not it — or there is no account, in the moment after a
    /// Host Recovery.
    /// </summary>
    PasswordRefused,

    /// <summary>
    /// There is no second factor for these to stand in for (ADR 0041), so there
    /// is nothing to issue. The screen does not offer the act in this state; this
    /// is the answer to a request that raced a different browser, or to one made
    /// by hand.
    /// </summary>
    NoSecondFactor,
}

/// <summary>
/// The sheet, when there was one to issue.
/// </summary>
public sealed record IssuedSheet(SheetOutcome Outcome, IReadOnlyList<BackupCodeText> Codes);

/// <summary>
/// A fresh sheet of backup codes, shown once.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the previous set wholesale — spent codes and unspent ones alike
/// go, and nothing of them survives (ADR 0032). An operator who has spent a few
/// and wants ten again gets ten, and the sheet they printed last year stops
/// working the moment this returns, which is what they asked for.
/// </para>
/// <para>
/// <b>It requires the password</b>, because ten of these are ten ways past the
/// second factor and handing them out on the strength of an unlocked browser
/// alone would make the sheet the weakest way in.
/// </para>
/// <para>
/// <b>It ends no session.</b> The ways a session ends are listed in
/// <c>docs/sign-in.md</c> and this is not among them: the codes are a way back
/// in when the second factor is unavailable, and replacing them says nothing
/// about the browsers already signed in.
/// </para>
/// </remarks>
public sealed class IssueBackupCodes(
    IOperators operators,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    /// <summary>The ten codes to show, or why there are none.</summary>
    public async Task<IssuedSheet> ExecuteAsync(
        string? password, CancellationToken cancellationToken)
    {
        if (!Password.TryCreate(password, out var presented))
        {
            return Refused(SheetOutcome.PasswordRefused);
        }

        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null
            || hasher.Verify(theOperator.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return Refused(SheetOutcome.PasswordRefused);
        }

        // Asked after the password rather than before it, so that whether this
        // installation has a second factor is not something an unauthenticated
        // guess can ask about.
        if (!theOperator.HasSecondFactor)
        {
            return Refused(SheetOutcome.NoSecondFactor);
        }

        var minted = BackupCode.MintSet(theOperator.Id, clock.GetUtcNow());
        await operators.ReplaceBackupCodesAsync(minted.Stored, cancellationToken);

        return new IssuedSheet(SheetOutcome.Issued, minted.Shown);
    }

    private static IssuedSheet Refused(SheetOutcome outcome) => new(outcome, []);
}
