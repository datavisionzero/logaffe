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
public sealed class ListIngestTokens(IProjects projects, ITokens tokens)
{
    /// <summary>
    /// What the project holds, or <c>null</c> when there is no such project.
    /// </summary>
    /// <remarks>
    /// A project that is not there and a project holding no token are two
    /// different readings — one is an address that is gone, the other is a door
    /// the operator closed themselves — and an empty list for both would show
    /// the settings of something deleted.
    /// </remarks>
    public async Task<IReadOnlyList<ListedIngestToken>?> ExecuteAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        if (await projects.FindAsync(projectId, cancellationToken) is null)
        {
            return null;
        }

        var held = await tokens.ListIngestTokensAsync(projectId, cancellationToken);

        return [.. held.Select(Listed)];
    }

    /// <summary>
    /// What every project holds, keyed by the project; a project holding no
    /// token is not in the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One read for the whole installation, and the same act as the one above
    /// rather than a second way to learn the same fact — which is what makes it
    /// safe for the settings tree to be assembled out of it while the screen
    /// that opens one project still asks for one project.
    /// </para>
    /// <para>
    /// There is no project that is not there to report here, so nothing is
    /// nullable: the caller is holding the project list this was read beside,
    /// and a project missing from the answer is one whose door is closed.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ListedIngestToken>>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await tokens.ListIngestTokensAsync(cancellationToken);

        return held.ToDictionary(
            project => project.Key,
            IReadOnlyList<ListedIngestToken> (project) => [.. project.Value.Select(Listed)]);
    }

    private static ListedIngestToken Listed(HeldToken token) =>
        new(token.Id, token.Identifier, token.IssuedAt, token.LastUsedAt);
}
