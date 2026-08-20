using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// One agent token as the operator sees it in a list.
/// </summary>
/// <remarks>
/// Like <see cref="ListedIngestToken"/>, it carries nothing sealed and no
/// secret.
/// </remarks>
/// <param name="Name">
/// What the operator recognizes the agent by, and nothing the server acts on.
/// Two agent tokens may share one.
/// </param>
/// <param name="Kind">
/// What this token is, which the operator cannot read off the list any other
/// way and which decides whether revoking it is urgent. A list of long-lived
/// credentials whose powers are invisible is a list nobody prunes, which is the
/// argument the last use already won (ADR 0033).
/// </param>
/// <param name="MayDestroy">
/// Whether this token may delete a project or a host, or lower a retention
/// window. Never true of a reading token.
/// </param>
/// <param name="LastUsedAt">
/// The load-bearing field of ADR 0021: a token that has not been used in months
/// is one to revoke, and this list is the only place that fact is visible. It is
/// accurate to within five minutes and is not to be shown as though it were
/// finer (ADR 0033).
/// </param>
public sealed record ListedAgentToken(
    Guid Id,
    string Name,
    AgentTokenKind Kind,
    bool MayDestroy,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Every agent token the installation holds.
/// </summary>
/// <remarks>
/// There is no project to scope this by, which is why the operator finds it
/// under the installation's settings rather than inside a project: an agent
/// token reads all of them, and putting it under one would say something untrue
/// about what it can do (<c>docs/ui.md</c>).
/// </remarks>
public sealed class ListAgentTokens(ITokens tokens)
{
    public async Task<IReadOnlyList<ListedAgentToken>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await tokens.ListAgentTokensAsync(cancellationToken);

        return [.. held.Select(token => new ListedAgentToken(
            token.Id,
            token.Name,
            token.Kind,
            token.MayDestroy,
            token.IssuedAt,
            token.LastUsedAt))];
    }
}
