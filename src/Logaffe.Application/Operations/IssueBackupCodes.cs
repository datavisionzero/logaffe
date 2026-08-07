using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

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
    /// <summary>
    /// The ten codes to show, or <c>null</c> when the password was not it —
    /// which is also what an installation with no account answers, in the moment
    /// after a Host Recovery.
    /// </summary>
    public async Task<IReadOnlyList<BackupCodeText>?> ExecuteAsync(
        string? password, CancellationToken cancellationToken)
    {
        if (!Password.TryCreate(password, out var presented))
        {
            return null;
        }

        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null
            || hasher.Verify(theOperator.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return null;
        }

        var minted = BackupCode.MintSet(theOperator.Id, clock.GetUtcNow());
        await operators.ReplaceBackupCodesAsync(minted.Stored, cancellationToken);

        return minted.Shown;
    }
}
