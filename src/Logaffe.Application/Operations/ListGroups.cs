using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// One group as the operator sees it, which is a name and an identity.
/// </summary>
/// <remarks>
/// <b>It does not say how many projects it holds.</b> That is a fact about the
/// projects and not about the group (ADR 0039), and whoever asks this already
/// reads the project list — a second answer carrying the same fact is a second
/// answer to keep current, which is exactly what it failed to be.
/// </remarks>
public sealed record ListedGroup(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// Every group the installation holds.
/// </summary>
/// <remarks>
/// One read. A group with nothing in it is in this answer rather than left out
/// of it — it is something the operator made and not a side effect of what the
/// projects say (ADR 0039), and a list that omitted it would answer <i>where did
/// the group I just created go</i>.
/// </remarks>
public sealed class ListGroups(IGroups groups)
{
    public async Task<IReadOnlyList<ListedGroup>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var held = await groups.ListAsync(cancellationToken);

        return [.. held.Select(group => new ListedGroup(group.Id, group.Name, group.CreatedAt))];
    }
}
