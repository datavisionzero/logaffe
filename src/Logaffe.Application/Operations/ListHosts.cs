using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// One host as the operator sees it on the settings screen that holds them.
/// </summary>
/// <param name="HostTokens">
/// How many tokens can report to it: one ordinarily, two while it is being
/// rotated, and none for a machine nothing can deliver to. That last case is why
/// the number is on the list at all.
/// </param>
/// <param name="LastReportedAt">
/// When a sample last arrived from it, or <c>null</c> when none ever has — a
/// host between being created and its collector being started, or one whose
/// machine is switched off. It is read off the newest sample rather than stored
/// beside the host (ADR 0039).
/// </param>
/// <param name="Projects">
/// How many projects say they run on it. It is counted off the project list the
/// interface already has rather than answered twice, which is what keeps the two
/// from disagreeing.
/// </param>
public sealed record ListedHost(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    int HostTokens,
    DateTimeOffset? LastReportedAt,
    int Projects);

/// <summary>
/// Every host the installation holds.
/// </summary>
/// <remarks>
/// Three reads, none of them per row: the hosts, the tokens of all of them at
/// once — counted out of the one listing the settings tree is assembled from
/// too — and when each last reported. The last is one grouped statement over
/// the end of the sample key rather than one lookup per host, because unlike the
/// project list's equivalent there is no per-project reader standing in the way —
/// samples are not scoped the way entries are (ADR 0045).
/// </remarks>
public sealed class ListHosts(
    IHosts hosts, IProjects projects, ITokens tokens, ISampleReader samples)
{
    public async Task<IReadOnlyList<ListedHost>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await hosts.ListAsync(cancellationToken);
        var heldTokens = await tokens.ListHostTokensAsync(cancellationToken);
        var reported = await samples.LastReportedAsync(cancellationToken);

        var on = (await projects.ListAsync(cancellationToken))
            .Where(project => project.HostId is not null)
            .GroupBy(project => project.HostId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. held.Select(host => new ListedHost(
                host.Id,
                host.Name,
                host.CreatedAt,
                heldTokens.TryGetValue(host.Id, out var holding) ? holding.Count : 0,
                reported.TryGetValue(host.Id, out var last) ? last : null,
                on.TryGetValue(host.Id, out var running) ? running : 0)),
        ];
    }
}
