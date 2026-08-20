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
/// <param name="AgentTokensRemoved">
/// How many agent tokens went with the account. It is reported rather than
/// counted silently because every one of them is a client configuration
/// somewhere that has just stopped working, and the operator is the only person
/// who can go and paste a new one in.
/// </param>
/// <param name="Guard">The way back in, which is what the operator is told.</param>
/// <param name="DrawnSecret">
/// The fresh claim secret in the clear, on an installation guarded by one, and
/// <c>null</c> in window mode. The command prints it: the operator running it is
/// at the keyboard, which is the only moment this value is ever handed over.
/// </param>
public sealed record Recovered(
    bool ThereWasAnOperator,
    int AgentTokensRemoved,
    ClaimGuard Guard,
    ClaimSecret? DrawnSecret);

/// <summary>
/// The way back into an installation nobody can sign in to.
/// </summary>
/// <remarks>
/// <para>
/// It <b>returns the installation to unclaimed</b> and opens the way in again
/// (ADR 0013) — a fresh claim secret, or a fresh window, whichever this
/// installation is configured for (ADR 0040). It does not reset a password, and
/// the name will make somebody expect the smaller thing, so the command above
/// says plainly what this does before calling it. Projects, ingest tokens and log entries are untouched: the
/// installation changes hands, it does not lose its contents, and an application
/// shipping logs through it does not notice.
/// </para>
/// <para>
/// Existing sessions and the backup codes go with the account, which is the
/// database's doing rather than a step this has to remember.
/// </para>
/// <para>
/// <b>The agent tokens go too, and that one is a step</b>, because an agent
/// token names no operator to be cascaded from — it is installation-scoped, the
/// way an ingest token is (ADR 0021). It goes for the reason the ingest token
/// stays: this is the act by which an installation changes hands, and an ingest
/// token surviving keeps an application delivering, while an agent token
/// surviving leaves whoever held it past the password and the second factor of
/// an operator who no longer exists — reading every entry in every project, or
/// working the settings of an installation that is no longer theirs, depending
/// on which kind it is (<c>docs/mcp.md</c>, ADR 0046). Both kinds go: they share
/// a table, and the stronger case is the second one.
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
    IOperators operators,
    ITokens tokens,
    IInstallation installation,
    IClaimSecretHandover handover,
    ClaimSettings settings,
    TimeProvider clock)
{
    public async Task<Recovered> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // The way in first, and the account second. The two are two statements,
        // so the order decides what a failure between them leaves behind: a fresh
        // window or a fresh secret on a still-claimed installation admits
        // nothing, because there is no re-claim while claimed, while an unclaimed
        // installation whose window has lapsed is a locked door needing the
        // command run again (ADR 0034).
        var guard = await installation.ArmClaimAsync(now, cancellationToken);

        // Drawn rather than reused, because this is the moment the installation's
        // notion of who may claim it changes and a secret that survived it is one
        // the previous operator still holds. An installation whose secret comes
        // from configuration keeps the one the compose file names: changing that
        // is editing the file, and this command has nothing to say about it.
        var drawn = settings.DrawsItsOwnSecret ? ClaimSecret.Draw() : null;
        if (drawn is not null)
        {
            guard.DrewSecret(drawn);
            await installation.RecordClaimAsync(guard, cancellationToken);
            await handover.WriteAsync(drawn, cancellationToken);
        }

        // And the agent tokens before the account, by the same reading of what a
        // failure in between leaves standing. Stopping here leaves an
        // installation that still has its operator and has lost its agent
        // configurations, which is a paste each and a command that can simply be
        // run again; stopping after would leave read-everything credentials on an
        // installation anybody can now claim, which is the one outcome this step
        // exists to prevent.
        var agentTokens = await tokens.ListAgentTokensAsync(cancellationToken);
        foreach (var token in agentTokens)
        {
            await tokens.RemoveAsync(token, cancellationToken);
        }

        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is not null)
        {
            await operators.RemoveAsync(theOperator, cancellationToken);
        }

        return new Recovered(theOperator is not null, agentTokens.Count, guard, drawn);
    }
}
