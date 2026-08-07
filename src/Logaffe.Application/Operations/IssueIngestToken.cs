using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// Gives a project a token to receive on, and gives it the second one that
/// rotation is made of.
/// </summary>
/// <remarks>
/// <para>
/// The whole of issuing is here: draw a token, seal its secret with the key on
/// the host volume, keep the identifier in the clear so the row can be found
/// again, and hand the token to the operator once. The secret is never held
/// anywhere else in the clear, and the row keeps only what
/// <see cref="ISecretCipher"/> made of it (ADR 0022).
/// </para>
/// <para>
/// It is an operator act and is unreachable over MCP, which is a property of the
/// interface rather than a permission: a log entry that asks an agent to mint a
/// credential must find nothing to call
/// (ADR 0018).
/// </para>
/// </remarks>
public sealed class IssueIngestToken(ITokens tokens, ISecretCipher cipher, TimeProvider clock)
{
    /// <summary>
    /// The token the project may now receive on, or <c>null</c> when it already
    /// holds <see cref="IngestToken.MaximumPerProject"/> of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is the rotation model saying what it is for: two tokens exist
    /// so that deployments can be moved over one at a time, and a third would
    /// mean the operator has lost track of which one they are retiring. They
    /// revoke one first, which is immediate.
    /// </para>
    /// <para>
    /// Two issues racing each other could pass the count together and leave the
    /// project holding three. That is one operator racing themselves in two
    /// browser tabs — there is exactly one account (ADR 0015) — and the outcome
    /// is a token too many rather than anything unsafe, so it is not bought off
    /// with a lock the rest of the product would then have to carry.
    /// </para>
    /// <para>
    /// That the project exists is not asked here. The caller reached this
    /// through a project, and the foreign key is what answers a project deleted
    /// underneath it.
    /// </para>
    /// </remarks>
    public async Task<IssuedToken?> ExecuteAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var held = await tokens.ListIngestTokensAsync(projectId, cancellationToken);
        if (held.Count >= IngestToken.MaximumPerProject)
        {
            return null;
        }

        var minted = TokenText.Mint(TokenKind.Ingest);
        var issuedAt = clock.GetUtcNow();
        var token = IngestToken.Issue(
            projectId, minted.Identifier, cipher.Encrypt(minted.Secret), issuedAt);

        await tokens.AddAsync(token, cancellationToken);

        return new IssuedToken(token.Id, minted, issuedAt);
    }
}
