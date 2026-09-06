using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// Gives an agent a token to read or to administer with, under a name its owner
/// will recognize in the list.
/// </summary>
/// <remarks>
/// <para>
/// The same three steps as an ingest token, and deliberately so — one credential
/// model pointing in two directions (ADR 0021). What differs is that there is no
/// project and no maximum: several exist at once so that a terminal agent and a
/// desktop agent can be retired separately (<c>docs/mcp.md</c>).
/// </para>
/// <para>
/// <b>It creates an agent as well as a token</b> (ADR 0052). The agent is an
/// identity, owned by the person issuing it, and it is what the token
/// authenticates as: an agent sees the projects its owner sees, and reaches the
/// installation-wide acts only while that owner is an administrator. The two
/// rows are one write, so a token never names an agent that was not created.
/// </para>
/// <para>
/// <b>A person issues it and an agent never does.</b> That exclusion was already
/// ADR 0046's — an agent that can issue an agent token grants itself the kind and
/// the flag its owner withheld — and it now has a second reason: an agent issuing
/// one would be an identity escaping the one it was given.
/// </para>
/// <para>
/// The kind and the flag beside it come from the operator too, and they are
/// settled here for good: nothing changes either afterwards, so an agent that
/// needs the other kind is given a second token and the first is revoked
/// (ADR 0046). The prefix the token is minted with is what the kind chooses, so
/// a token presented to the wrong half of the surface fails at the door.
/// </para>
/// <para>
/// The name is conventionally the client it is being issued for. It is a label
/// for the list and does not identify the token to the server — and the agent
/// identity carries the same one, because that is what a record of what the
/// agent did reads as.
/// </para>
/// <para>
/// What is handed back is a token; what the product hands over is the finished
/// client configuration with this token and the installation's address already
/// in it. Assembling that is an adapter's work, because the address is something
/// only the adapter knows.
/// </para>
/// </remarks>
public sealed class IssueAgentToken(ITokens tokens, ISecretCipher cipher, TimeProvider clock)
{
    /// <param name="owner">
    /// The user issuing it, which is who the agent will act for and never an
    /// agent itself.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="AgentToken.NameMaxLength"/> — or <paramref name="mayDestroy"/>
    /// was asked of a reading token. A caller taking either from a person says
    /// so before it gets here; the domain refusing them is the backstop.
    /// </exception>
    public async Task<IssuedToken> ExecuteAsync(
        User owner,
        string name,
        AgentTokenKind kind,
        bool mayDestroy,
        CancellationToken cancellationToken)
    {
        var minted = TokenText.Mint(kind.AsTokenKind());
        var issuedAt = clock.GetUtcNow();

        var agent = Agent.Create(name, owner.Id, issuedAt);
        var token = AgentToken.Issue(
            agent.Id,
            name,
            kind,
            mayDestroy,
            minted.Identifier,
            cipher.Encrypt(minted.Secret),
            issuedAt);

        await tokens.AddAsync(agent, token, cancellationToken);

        return new IssuedToken(token.Id, minted, issuedAt);
    }
}
