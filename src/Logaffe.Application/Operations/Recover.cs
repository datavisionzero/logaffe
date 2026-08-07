using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What Host Recovery did.
/// </summary>
/// <param name="ThereWasAnOperator">
/// Whether there was an account to remove. <c>false</c> is the other case
/// <c>VISION.md</c> asks this command to cover — a window that lapsed before
/// anyone claimed the installation — and it is not a failure: the window is
/// armed either way, which is the whole of what that case needs.
/// </param>
/// <param name="Window">The fresh window, which is what the operator is told.</param>
public sealed record Recovered(bool ThereWasAnOperator, ClaimWindow Window);

/// <summary>
/// The way back into an installation nobody can sign in to.
/// </summary>
/// <remarks>
/// <para>
/// It <b>returns the installation to unclaimed</b> and arms a fresh claim window
/// (ADR 0013) — it does not reset a password, and the name will make somebody
/// expect the smaller thing, so the command above says plainly what this does
/// before calling it. Projects, ingest tokens and log entries are untouched: the
/// installation changes hands, it does not lose its contents, and an application
/// shipping logs through it does not notice.
/// </para>
/// <para>
/// Existing sessions and the backup codes go with the account, which is the
/// database's doing rather than a step this has to remember.
/// </para>
/// <para>
/// <b>It is not a security boundary.</b> Whoever can run a command in the
/// container already owns the database and could do this and more by hand. This
/// exists so that the operator does not have to, and its whole security property
/// is that it is reachable from the host and never over the network — which is
/// why there is no endpoint anywhere that calls it.
/// </para>
/// </remarks>
public sealed class Recover(
    IOperators operators, IInstallation installation, TimeProvider clock)
{
    public async Task<Recovered> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // The window first, and the account second. The two are two statements,
        // so the order decides what a failure between them leaves behind: an
        // armed window on a still-claimed installation admits nothing, because
        // there is no re-claim while claimed, while an unclaimed installation
        // whose window has lapsed is a locked door needing the command run again
        // (ADR 0034).
        var window = await installation.ArmClaimWindowAsync(now, cancellationToken);

        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is not null)
        {
            await operators.RemoveAsync(theOperator, cancellationToken);
        }

        return new Recovered(theOperator is not null, window);
    }
}
