using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// The group rows an installation holds, found by the identity a project points
/// at and by the name the operator typed.
/// </summary>
/// <remarks>
/// <para>
/// The list is read whole, for the same reason the projects are: there are fewer
/// groups than projects, they are read when a session starts and rarely again,
/// and paging a screen that fits is a management surface bought for nothing.
/// </para>
/// <para>
/// Removing one leaves its projects behind, in no group. That is the database's
/// doing — the foreign key on the project sets itself to null — and it is what
/// makes deleting a group an act that destroys nothing (ADR 0039).
/// </para>
/// </remarks>
public interface IGroups
{
    /// <summary>Every group, oldest first, which the screens sort as they show.</summary>
    Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The group the caller named, or <c>null</c> when there is none — which is
    /// what a group deleted in another browser tab looks like.
    /// </summary>
    Task<Group?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The group holding this name, or <c>null</c> when the name is free. The
    /// name given is the one <see cref="Group.NormalizeName"/> produced.
    /// </summary>
    Task<Group?> FindAsync(string name, CancellationToken cancellationToken);

    Task AddAsync(Group group, CancellationToken cancellationToken);

    /// <summary>Writes back what was just changed — which is only ever a name.</summary>
    Task RecordAsync(Group group, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the group. Its projects stay, in no group, and nothing else about
    /// them changes.
    /// </summary>
    Task RemoveAsync(Group group, CancellationToken cancellationToken);
}
