using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// What Host Recovery did.
/// </summary>
/// <param name="IdentitiesRemoved">
/// How many users and agents went. Zero is not a failure: an installation that
/// was never bootstrapped is the other case <c>VISION.md</c> asks this command
/// to cover, and it needs nothing done to it.
/// </param>
/// <param name="AgentTokensRemoved">
/// How many agent tokens went with them. It is reported rather than counted
/// silently because every one of them is a client configuration somewhere that
/// has just stopped working, and somebody has to go and paste a new one in.
/// </param>
public sealed record Recovered(int IdentitiesRemoved, int AgentTokensRemoved);

/// <summary>
/// The way back into an installation nobody can sign in to.
/// </summary>
/// <remarks>
/// <para>
/// It <b>removes every identity</b> — every user and every agent — and leaves
/// everything else standing: projects, groups, ingest tokens, host tokens,
/// settings and entries (ADR 0058). The way back in afterwards is the bootstrap
/// from configuration (ADR 0054), which is why this command no longer has to
/// arm anything or draw anything.
/// </para>
/// <para>
/// <b>It costs more than its name suggests</b>, so the command above says
/// plainly what it does before calling it: it does not reset a password and it
/// does not pick an account, it removes all of them. An application shipping logs
/// through this installation does not notice, which is the point of the line the
/// removal stops at.
/// </para>
/// <para>
/// Sessions, backup codes and project assignments go with the identities, which
/// is the database's doing rather than a step this has to remember.
/// </para>
/// <para>
/// <b>The agent tokens go too, and that one is a step.</b> They go for the
/// reason the ingest tokens stay: an ingest token surviving keeps an application
/// delivering, while an agent token surviving leaves whoever holds it reading
/// entries or working settings on an installation whose people are gone
/// (<c>docs/mcp.md</c>, ADR 0046). It runs <em>before</em> the identities, so
/// that a failure between the two leaves an installation that still has its
/// users and has lost its agent configurations — a paste each, and a command
/// that can simply be run again — rather than live read-everything credentials
/// on an installation anybody can now bootstrap.
/// </para>
/// <para>
/// <b>It is not a security boundary.</b> Whoever can run a command in the
/// container already owns the database and could do this and more by hand. This
/// exists so that nobody has to, and its whole security property is that it is
/// reachable from the host and never over the network — which is why there is no
/// endpoint anywhere that calls it.
/// </para>
/// </remarks>
public sealed class Recover(IIdentities identities, ITokens tokens)
{
    public async Task<Recovered> ExecuteAsync(CancellationToken cancellationToken)
    {
        var agentTokens = await tokens.ListAgentTokensAsync(cancellationToken);
        foreach (var token in agentTokens)
        {
            await tokens.RemoveAsync(token, cancellationToken);
        }

        var removed = await identities.RemoveEveryIdentityAsync(cancellationToken);

        return new Recovered(removed, agentTokens.Count);
    }
}
