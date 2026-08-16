using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// The project rows an installation holds, found by the identity everything
/// else attaches to and by the name the operator typed.
/// </summary>
/// <remarks>
/// <para>
/// The list is read whole. An installation holds on the order of 10 to 30 of
/// them (<c>VISION.md</c>), it is read when a session starts and rarely again,
/// and paging a screen that fits is a management surface bought for nothing.
/// </para>
/// <para>
/// <see cref="FindAsync(string, CancellationToken)"/> is here because a name
/// taken is an answer the operator gets rather than an exception: the unique
/// index stays the thing that decides it, and this is what turns the ordinary
/// case into a sentence about a name instead of a failed request.
/// </para>
/// <para>
/// Removing takes the tokens with it. That is the database's doing — the
/// foreign key cascades — and it is half of ADR 0019: the project, its tokens
/// and its visibility go at once, and the entries follow in the background.
/// </para>
/// </remarks>
public interface IProjects
{
    /// <summary>Every project, oldest first, which is the operator's list.</summary>
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The project the caller named, or <c>null</c> when there is none — which
    /// is what a project deleted in another browser tab looks like.
    /// </summary>
    Task<Project?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The project holding this name in that group, or <c>null</c> when the name
    /// is free there. The name given is the one
    /// <see cref="Project.NormalizeName"/> produced, and
    /// <paramref name="groupId"/> is <c>null</c> for the projects in no group,
    /// among which a name is taken exactly as it is inside one.
    /// </summary>
    Task<Project?> FindAsync(string name, Guid? groupId, CancellationToken cancellationToken);

    Task AddAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back what was just changed on <paramref name="project"/> — a new
    /// name, a new retention window.
    /// </summary>
    Task RecordAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the project and, with it, the tokens that admitted deliveries to
    /// it. Its entries are not this act's business (ADR 0019).
    /// </summary>
    Task RemoveAsync(Project project, CancellationToken cancellationToken);
}
