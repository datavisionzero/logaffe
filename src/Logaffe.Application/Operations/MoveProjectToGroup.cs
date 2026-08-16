using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// How moving a project between groups ended.
/// </summary>
public enum MoveProjectOutcome
{
    /// <summary>The project is listed under the group it was given, or under none.</summary>
    Moved,

    /// <summary>
    /// There is no such project. A second browser tab deleted it, or the address
    /// was typed.
    /// </summary>
    NoSuchProject,

    /// <summary>
    /// There is no such group, which is ordinarily one removed from another
    /// browser while this screen was open.
    /// </summary>
    NoSuchGroup,

    /// <summary>
    /// Where it was going already holds a project by that name. The move is
    /// refused rather than resolved, because the alternative is renaming a
    /// project the operator did not ask to rename.
    /// </summary>
    NameTaken,
}

/// <summary>
/// Listing a project under another group, or under none.
/// </summary>
/// <remarks>
/// <para>
/// It moves nothing but the heading the project appears under: entries, tokens
/// and queries are attached to its identity, so no sender notices, nothing is
/// redeployed and no log entry changes hands.
/// </para>
/// <para>
/// <b>A name taken in the destination refuses the move.</b> A project's name is
/// unique within its group (<c>docs/projects.md</c>), so moving <c>api</c> into a
/// group that already lists an <c>api</c> would put the operator in front of the
/// two rows the uniqueness exists to prevent. The operator renames one of them
/// first, which is a decision this act has no business making for them.
/// </para>
/// </remarks>
public sealed class MoveProjectToGroup(IProjects projects, IGroups groups)
{
    /// <param name="groupId">The group to list it under, or <c>null</c> for none.</param>
    public async Task<MoveProjectOutcome> ExecuteAsync(
        Guid id, Guid? groupId, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return MoveProjectOutcome.NoSuchProject;
        }

        // Moving a project to the group it is already in is a no-op rather than a
        // collision with itself: the operator opened the field and left it.
        if (project.GroupId == groupId)
        {
            return MoveProjectOutcome.Moved;
        }

        if (groupId is not null
            && await groups.FindAsync(groupId.Value, cancellationToken) is null)
        {
            return MoveProjectOutcome.NoSuchGroup;
        }

        var taken = await projects.FindAsync(project.Name, groupId, cancellationToken);
        if (taken is not null)
        {
            return MoveProjectOutcome.NameTaken;
        }

        project.MoveTo(groupId);
        await projects.RecordAsync(project, cancellationToken);

        return MoveProjectOutcome.Moved;
    }
}
