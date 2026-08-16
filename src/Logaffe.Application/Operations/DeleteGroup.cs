using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Removing a group, which destroys nothing.
/// </summary>
/// <remarks>
/// <para>
/// A group holds no entries, no tokens and no settings. Its projects stay
/// exactly as they were and are left in no group — the foreign key sets itself
/// to null — so this is an act the operator can undo by making the group again
/// and moving them back (ADR 0039).
/// </para>
/// <para>
/// <b>There is no typed name to confirm it.</b> That guard belongs to deleting a
/// project, where it is proportionate to entries that do not come back; wearing
/// it here would teach the operator that both acts weigh the same. How many
/// projects this leaves in no group is on the list the screen is already
/// reading, and the screen says it before asking.
/// </para>
/// </remarks>
public sealed class DeleteGroup(IGroups groups)
{
    /// <summary>
    /// Whether there was a group to remove. <c>false</c> is one already gone — a
    /// second click, or another tab — and not a failure of anything.
    /// </summary>
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(id, cancellationToken);
        if (group is null)
        {
            return false;
        }

        await groups.RemoveAsync(group, cancellationToken);
        return true;
    }
}
