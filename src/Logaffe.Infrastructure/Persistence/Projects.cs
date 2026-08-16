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
/// </remarks>
public sealed class Projects(LogaffeDbContext context) : IProjects
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) =>
        await context.Projects.OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);

    public Task<Project?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

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

    public async Task RemoveAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken);
    }
}
