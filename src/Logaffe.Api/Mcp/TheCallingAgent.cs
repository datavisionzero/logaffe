using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.Api.Mcp;

/// <summary>
/// Who is on the other end of this tool call: the agent's owner, and what that
/// person reaches (ADR 0052, ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// Scoped, and filled in by <see cref="AgentAuthenticationHandler"/> before any
/// tool runs — one resolution per call, for the reason the session's door
/// resolves one per request: a call answered out of two different reaches is a
/// call that contradicts itself.
/// </para>
/// <para>
/// It is a service rather than something read off the <c>HttpContext</c> because
/// a tool is a method the SDK invokes with what dependency injection hands it,
/// and asking for this is how a tool says it needs to know whose call it is.
/// <b>A tool that reads entries cannot be written without it</b>, which is the
/// point: the acts take a <see cref="Projects.Reach"/> and there is exactly one
/// place on this surface to get one.
/// </para>
/// </remarks>
public sealed class TheCallingAgent
{
    private User? owner;
    private Reach? reach;

    /// <summary>The person this agent acts for.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing admitted this call. Every tool sits behind the scheme, so
    /// reaching it unauthenticated is a routing mistake rather than an
    /// unauthenticated call.
    /// </exception>
    public User Owner =>
        owner ?? throw new InvalidOperationException("This call was not admitted by a token.");

    /// <inheritdoc cref="Owner"/>
    public Reach Reach =>
        reach ?? throw new InvalidOperationException("This call was not admitted by a token.");

    /// <summary>
    /// Whether the person this agent acts for administers the installation. An
    /// agent is never one itself, and what an administering token reaches beyond
    /// its own projects is exactly what its owner reaches (ADR 0052).
    /// </summary>
    public bool Administers => Owner.Administrator;

    internal void Admitted(User owner, Reach reach)
    {
        this.owner = owner;
        this.reach = reach;
    }
}
