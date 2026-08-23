using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// One project as the operator sees it on the list a session starts at.
/// </summary>
/// <param name="IngestTokens">
/// How many tokens the project can receive on: one ordinarily, two while it is
/// being rotated, and none for a project whose door is closed. That last case is
/// why the number is on the list at all — an operator should not have to open
/// each project to find the one nothing can deliver to.
/// </param>
/// <param name="LastReceivedAt">
/// When the project last received an entry, or <c>null</c> when it has never
/// received one — the one fact both consumers want at a glance, because it is
/// what says whether an application is still delivering.
/// </param>
/// <param name="GroupId">
/// The group the project is listed under, or <c>null</c> for one in no group.
/// The name of that group is on the group list rather than repeated here, which
/// is also what lets a group with no projects in it be shown at all.
/// </param>
/// <param name="HostId">
/// The machine the project runs on, or <c>null</c> for one whose machine is not
/// tracked. It rides on the project for the reason the group does: a tool
/// listing hosts would be a second read path for a fact this one already carries
/// (<c>docs/mcp.md</c>), and it is what lets an agent go from the errors in a
/// project to the machine behind them.
/// </param>
/// <param name="Muted">
/// Whether this project's alert conditions are evaluated at all
/// (<c>docs/alerts.md</c>). It rides here because the screen that sets it reads
/// the project off this list, and it goes no further than the operator's own
/// surfaces: neither kind of agent token reaches the mute.
/// </param>
public sealed record ListedProject(
    Guid Id,
    string Name,
    Guid? GroupId,
    Guid? HostId,
    RetentionWindow Retention,
    DateTimeOffset CreatedAt,
    int IngestTokens,
    DateTimeOffset? LastReceivedAt,
    bool Muted);

/// <summary>
/// Every project the installation holds.
/// </summary>
/// <remarks>
/// <para>
/// This is where a session starts (<c>docs/ui.md</c>), and it is deliberately
/// not a dashboard: there is no count of entries beside a project, because that
/// is a query over the largest table in the database run for a number nobody
/// asked for.
/// </para>
/// <para>
/// Two reads that are not per row — the projects, and the tokens of all of them
/// at once, counted out of the one listing the settings tree is assembled from
/// too — and then one lookup per project for when it last received an entry.
/// That last one is per row on purpose: the reader takes the project because a
/// query always runs inside one, and an installation holds projects in tens
/// rather than in thousands, so the choice is between ten reads at the end of an
/// index and a read across every project the reader is not allowed to have.
/// </para>
/// </remarks>
public sealed class ListProjects(IProjects projects, ITokens tokens, IEntryReader entries)
{
    public async Task<IReadOnlyList<ListedProject>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await projects.ListAsync(cancellationToken);
        var heldTokens = await tokens.ListIngestTokensAsync(cancellationToken);

        var listed = new List<ListedProject>(held.Count);

        // One after another rather than all at once: these run on the one
        // connection the request holds, and asking it for ten answers in
        // parallel is the way to be told it is already in use.
        foreach (var project in held)
        {
            listed.Add(new ListedProject(
                project.Id,
                project.Name,
                project.GroupId,
                project.HostId,
                project.Retention,
                project.CreatedAt,
                heldTokens.TryGetValue(project.Id, out var holding) ? holding.Count : 0,
                await entries.LastReceivedAsync(project.Id, cancellationToken),
                project.Muted));
        }

        return listed;
    }
}
