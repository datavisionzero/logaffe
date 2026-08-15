using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// Gives an agent a token to read with, under a name the operator will
/// recognize in the list.
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
    /// <see cref="AgentToken.NameMaxLength"/>. A caller taking this from a person
    /// says so before it gets here; the domain refusing it is the backstop.
    /// </exception>
    public async Task<IssuedToken> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var minted = TokenText.Mint(TokenKind.Agent);
        var issuedAt = clock.GetUtcNow();
        var token = AgentToken.Issue(
            name, minted.Identifier, cipher.Encrypt(minted.Secret), issuedAt);

        await tokens.AddAsync(token, cancellationToken);

        return new IssuedToken(token.Id, minted, issuedAt);
    }
}
