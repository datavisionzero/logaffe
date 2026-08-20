using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// One of a host's tokens as the operator sees it in a list.
/// </summary>
/// <remarks>
/// It carries no secret and nothing sealed, exactly as
/// <see cref="ListedIngestToken"/> does. Reading a token back is
/// <see cref="ReadTokenBack"/>, asked for one token at a time — and on a fleet
/// of machines that separation is worth more than it is on a project, because
/// the settings screen holding these is opened for every machine and the
/// credential is wanted for one.
/// </remarks>
/// <param name="LastUsedAt">
/// When a collector last presented it, to within five minutes (ADR 0033). It is
/// what says a rotation is finished — the old token's last use stops moving —
/// and it is not the same fact as when the host last reported, which is read off
/// the newest sample.
/// </param>
public sealed record ListedHostToken(
    Guid Id,
    TokenIdentifier Identifier,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// What one host can currently receive samples on: one token, or two while it is
/// being rotated.
/// </summary>
public sealed class ListHostTokens(IHosts hosts, ITokens tokens)
{
    /// <summary>
    /// What the host holds, or <c>null</c> when there is no such host.
    /// </summary>
    /// <remarks>
    /// A host that is not there and a host holding no token are two different
    /// readings — one is an address that is gone, the other is a machine nothing
    /// can deliver to — and an empty list for both would show the settings of
    /// something deleted.
    /// </remarks>
    public async Task<IReadOnlyList<ListedHostToken>?> ExecuteAsync(
        Guid hostId, CancellationToken cancellationToken)
    {
        if (await hosts.FindAsync(hostId, cancellationToken) is null)
        {
            return null;
        }

        var held = await tokens.ListHostTokensAsync(hostId, cancellationToken);

        return [.. held.Select(Listed)];
    }

    /// <summary>
    /// What every host holds, keyed by the host; a host holding no token is not
    /// in the answer.
    /// </summary>
    /// <remarks>
    /// One read for the whole fleet, and the same act as the one above rather
    /// than a second way to learn the same fact. On a fleet this is the
    /// difference the settings tree is made of: the machines are read once
    /// together, and the credential is still asked for one machine at a time.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ListedHostToken>>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await tokens.ListHostTokensAsync(cancellationToken);

        return held.ToDictionary(
            host => host.Key,
            IReadOnlyList<ListedHostToken> (host) => [.. host.Value.Select(Listed)]);
    }

    private static ListedHostToken Listed(HeldToken token) =>
        new(token.Id, token.Identifier, token.IssuedAt, token.LastUsedAt);
}
