using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// One of a project's tokens as the operator sees it in a list.
/// </summary>
/// <remarks>
/// It carries no secret and nothing sealed. Reading a token back is
/// <see cref="ReadTokenBack"/>, asked for one token at a time, so that opening
/// the settings of a project is not the same act as reading its credential.
/// </remarks>
/// <param name="Id">What revoking or reading this token back names it by.</param>
/// <param name="Identifier">
/// The non-secret middle of the token's own text, which is how the operator
/// tells the two tokens of a rotation apart — an ingest token has no name, and
/// this is what a deployment's configuration and a leaked log line both show.
/// </param>
/// <param name="LastUsedAt">
/// Null until a delivery has presented it, and accurate to within five minutes
/// (ADR 0033). It is what says rotation is finished: the old token's last use
/// stops moving.
/// </param>
public sealed record ListedIngestToken(
    Guid Id,
    TokenIdentifier Identifier,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// What one project can currently receive on: one token, or two while it is
/// being rotated.
/// </summary>
public sealed class ListIngestTokens(ITokens tokens)
{
    public async Task<IReadOnlyList<ListedIngestToken>> ExecuteAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var held = await tokens.ListIngestTokensAsync(projectId, cancellationToken);

        return [.. held.Select(token => new ListedIngestToken(
            token.Id, token.Identifier, token.IssuedAt, token.LastUsedAt))];
    }
}
