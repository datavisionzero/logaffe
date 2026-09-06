using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// The project rows an installation holds, found by the identity everything
/// else attaches to and by the name somebody typed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading one takes a <see cref="Reach"/>, and that is the one filter</b>
/// (ADR 0055). Every act that touches a project comes through here, so
/// narrowing here narrows all of them, and a call site with no reach in hand
/// does not compile. A project outside it answers the way a project that was
/// never created answers.
/// </para>
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
    /// <summary>
    /// The projects <paramref name="reach"/> holds, oldest first, which is
    /// somebody's list.
    /// </summary>
    Task<IReadOnlyList<Project>> ListAsync(Reach reach, CancellationToken cancellationToken);

    /// <summary>
    /// The project the caller named, or <c>null</c> when there is none they can
    /// reach — which is what a project deleted in another browser tab looks
    /// like, and what a project belonging to somebody else looks like as well.
    /// </summary>
    Task<Project?> FindAsync(Reach reach, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The project holding this name in that group, or <c>null</c> when the name
    /// is free there. The name given is the one
    /// <see cref="Project.NormalizeName"/> produced, and
    /// <paramref name="groupId"/> is <c>null</c> for the projects in no group,
    /// among which a name is taken exactly as it is inside one.
    /// </summary>
    /// <remarks>
    /// <b>This one takes no reach and must not.</b> A name is taken across the
    /// installation whoever can see it, so narrowing here would let somebody
    /// create a second project under a name that is already held and meet the
    /// unique index instead of a sentence. What it answers is whether a name is
    /// free, never what the project holding it is called or contains — the
    /// callers use it as a yes or a no.
    /// </remarks>
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
