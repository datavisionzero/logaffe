using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// Gives an agent a token to read or to administer with, under a name the
/// operator will recognize in the list.
/// </summary>
/// <remarks>
/// <para>
/// The same three steps as an ingest token, and deliberately so — one credential
/// model pointing in two directions (ADR 0021). What differs is that there is no
/// project and no maximum: an agent token reads every project, and several exist
/// at once so that a terminal agent and a desktop agent can be retired
/// separately (<c>docs/mcp.md</c>).
/// </para>
/// <para>
/// The kind and the flag beside it come from the operator too, and they are
/// settled here for good: nothing changes either afterwards, so an agent that
/// needs the other kind is given a second token and the first is revoked
/// (ADR 0046). The prefix the token is minted with is what the kind chooses, so
/// a token presented to the wrong half of the surface fails at the door.
/// </para>
/// <para>
/// The name comes from the operator, conventionally the client it is being
/// issued for, and it is a label for the list and nothing more: it does not
/// identify the token to the server, and two agents may share one.
/// </para>
/// <para>
/// What the operator is handed is a token; what the product hands over is the
/// finished client configuration with this token and the installation's address
/// already in it. Assembling that is an adapter's work, because the address is
/// something only the adapter knows.
/// </para>
/// </remarks>
public sealed class IssueAgentToken(ITokens tokens, ISecretCipher cipher, TimeProvider clock)
{
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="AgentToken.NameMaxLength"/> — or <paramref name="mayDestroy"/>
    /// was asked of a reading token. A caller taking either from a person says
    /// so before it gets here; the domain refusing them is the backstop.
    /// </exception>
    public async Task<IssuedToken> ExecuteAsync(
        string name,
        AgentTokenKind kind,
        bool mayDestroy,
        CancellationToken cancellationToken)
    {
        var minted = TokenText.Mint(kind.AsTokenKind());
        var issuedAt = clock.GetUtcNow();
        var token = AgentToken.Issue(
            name, kind, mayDestroy, minted.Identifier, cipher.Encrypt(minted.Secret), issuedAt);

        await tokens.AddAsync(token, cancellationToken);

        return new IssuedToken(token.Id, minted, issuedAt);
    }
}
