using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// Which user reaches which project (ADR 0055).
/// </summary>
/// <remarks>
/// It is a store of its own rather than a collection on the project, because
/// what is asked of it is the other direction: a request resolves one identity's
/// whole reach, once, and every read narrows to that.
/// </remarks>
public interface IProjectAccess
{
    /// <summary>
    /// The projects this user was assigned, as the value every read is narrowed
    /// by. It is <see cref="Reach.Nothing"/> for somebody who has been given
    /// none, which is what an invitation leaves behind.
    /// </summary>
    Task<Reach> ReachOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Who reaches one project, which is what its assignment screen shows.</summary>
    Task<IReadOnlyList<ProjectAccess>> ListForProjectAsync(
        Guid projectId, CancellationToken cancellationToken);

    /// <summary>What one user reaches, as the rows rather than as a reach.</summary>
    Task<IReadOnlyList<ProjectAccess>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes an assignment, and answers <c>false</c> when it was already there —
    /// which is a second click rather than a failure.
    /// </summary>
    Task<bool> GrantAsync(ProjectAccess access, CancellationToken cancellationToken);

    /// <summary>
    /// Takes one away. It takes effect on that user's next request, because the
    /// reach is resolved per request and nothing caches it in front.
    /// </summary>
    Task<bool> RevokeAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);
}
