using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// One group as the operator sees it.
/// </summary>
/// <param name="Projects">
/// How many projects it holds. It is what the settings area lists beside a name
/// and what removing one says before it happens, and zero is an ordinary answer:
/// a group made before its first project, or left behind by its last.
/// </param>
public sealed record ListedGroup(Guid Id, string Name, DateTimeOffset CreatedAt, int Projects);

/// <summary>
/// Every group the installation holds.
/// </summary>
/// <remarks>
/// Two reads and no more: the groups, and the projects counted by the group they
/// point at. A group with nothing in it is in this answer rather than left out of
/// it — it is something the operator made and not a side effect of what the
/// projects say (ADR 0039), and a list that omitted it would answer <i>where did
/// the group I just created go</i>.
/// </remarks>
public sealed class ListGroups(IGroups groups, IProjects projects)
{
    public async Task<IReadOnlyList<ListedGroup>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var held = await groups.ListAsync(cancellationToken);
        var counts = await projects.CountByGroupAsync(cancellationToken);

        return held
            .Select(group => new ListedGroup(
                group.Id,
                group.Name,
                group.CreatedAt,
                counts.TryGetValue(group.Id, out var count) ? count : 0))
            .ToList();
    }
}
