using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The project rows.
/// </summary>
/// <remarks>
/// A handful of rows read whole, and single-row lookups on the primary key or
/// on <c>ix_project_name</c>. Deleting is one statement: the tokens hanging off
/// the project go with it by the cascade on <c>fk_ingest_token_project</c>,
/// which is why they are not read first (ADR 0019).
/// <para>
/// <b>The reach is applied here and nowhere else</b> (ADR 0055). Both reads take
/// one, which is what makes a call site that forgot a call site that does not
/// compile; the installation's own reach — the retention sweep, the alert pass,
/// the ingest path — narrows nothing, and says so by name.
/// </para>
/// </remarks>
public sealed class Projects(LogaffeDbContext context) : IProjects
{
    public async Task<IReadOnlyList<Project>> ListAsync(
        Reach reach, CancellationToken cancellationToken) =>
        await Reachable(reach).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);

    public Task<Project?> FindAsync(Reach reach, Guid id, CancellationToken cancellationToken) =>
        Reachable(reach).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Project?> FindAsync(
        string name, Guid? groupId, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(
            p => p.Name == name && p.GroupId == groupId, cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAsync(Project project, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// The projects <paramref name="reach"/> holds, as the query everything else
    /// here is built on.
    /// </summary>
    /// <remarks>
    /// A reach naming nothing narrows to nothing rather than to everything,
    /// which is the direction a mistake here has to fail in. The set is a
    /// handful of ids on an installation of this size, so it goes into the
    /// statement as a list rather than as a join.
    /// </remarks>
    private IQueryable<Project> Reachable(Reach reach) =>
        reach.IsEverything
            ? context.Projects
            : context.Projects.Where(p => reach.Projects.Contains(p.Id));

    public async Task RemoveAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken);
    }
}
