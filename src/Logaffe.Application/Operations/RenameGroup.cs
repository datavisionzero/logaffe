using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a group's rename ended.
/// </summary>
public enum RenameGroupOutcome
{
    /// <summary>The group answers to the new name, and to nothing else.</summary>
    Renamed,

    /// <summary>
    /// There is no such group. A second browser tab removed it, or the address
    /// was typed.
    /// </summary>
    NoSuchGroup,

    /// <summary>
    /// Another group holds that name. It is the one refusal the operator acts
    /// on, so it is not the same answer as a group that is not there.
    /// </summary>
    NameTaken,
}

/// <summary>
/// Giving a group another name.
/// </summary>
/// <remarks>
/// It moves no project. A project points at the group's identity rather than at
/// its name (ADR 0039), which is the whole reason the identity is there, and a
/// rename is therefore a word on a heading changing and nothing else.
/// </remarks>
public sealed class RenameGroup(IGroups groups)
{
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="Group.NameMaxLength"/>.
    /// </exception>
    public async Task<RenameGroupOutcome> ExecuteAsync(
        Guid id, string name, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(id, cancellationToken);
        if (group is null)
        {
            return RenameGroupOutcome.NoSuchGroup;
        }

        // Renaming a group to the name it already has is a no-op rather than a
        // collision with itself: the operator opened the field and left it.
        var normalized = Group.NormalizeName(name);
        var taken = await groups.FindAsync(normalized, cancellationToken);
        if (taken is not null && taken.Id != group.Id)
        {
            return RenameGroupOutcome.NameTaken;
        }

        group.Rename(normalized);
        await groups.RecordAsync(group, cancellationToken);

        return RenameGroupOutcome.Renamed;
    }
}
