using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// One project, by the identity everything else attaches to.
/// </summary>
/// <remarks>
/// The list is where a session starts and it carries the same fields, so this
/// exists for the address rather than for the screen: a project's settings are
/// reachable by URL, a reload lands on them, and the project a creation just
/// handed back has somewhere to point at.
/// </remarks>
public sealed class ReadProject(IProjects projects)
{
    /// <summary>The project, or <c>null</c> when there is no such project.</summary>
    public Task<Project?> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        projects.FindAsync(id, cancellationToken);
}
