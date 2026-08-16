using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// The operator making a group, which is the only way one comes about.
/// </summary>
/// <remarks>
/// It makes the group and nothing else. A group is empty until a project is moved
/// into it, and being empty is an ordinary state rather than a step that has not
/// finished (ADR 0039).
/// </remarks>
public sealed class CreateGroup(IGroups groups, TimeProvider clock)
{
    /// <summary>
    /// The group, or <c>null</c> when the installation already holds one by that
    /// name. Two groups called <c>shop</c> are two headings that say nothing
    /// about which of them holds what.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="Group.NameMaxLength"/>.
    /// </exception>
    public async Task<Group?> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var taken = await groups.FindAsync(Group.NormalizeName(name), cancellationToken);
        if (taken is not null)
        {
            return null;
        }

        var group = Group.Create(name, clock.GetUtcNow());
        await groups.AddAsync(group, cancellationToken);

        return group;
    }
}
