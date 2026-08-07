using Logaffe.Application.Ports;

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
/// <param name="LastUsedAt">
/// The load-bearing field of ADR 0021: a token that has not been used in months
/// is one to revoke, and this list is the only place that fact is visible. It is
/// accurate to within five minutes and is not to be shown as though it were
/// finer (ADR 0033).
/// </param>
public sealed record ListedAgentToken(
    Guid Id,
    string Name,
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
            token.Id, token.Name, token.IssuedAt, token.LastUsedAt))];
    }
}
