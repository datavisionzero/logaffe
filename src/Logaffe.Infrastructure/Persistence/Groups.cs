using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The group rows.
/// </summary>
/// <remarks>
/// A handful of rows read whole, and single-row lookups on the primary key or on
/// <c>ix_project_group_name</c>. Removing one is a single statement: the projects
/// that pointed at it are left in no group by the <c>on delete set null</c> on
/// <c>fk_project_project_group</c>, which is why they are not read first.
/// </remarks>
public sealed class Groups(LogaffeDbContext context) : IGroups
{
    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        await context.Groups.OrderBy(g => g.CreatedAt).ToListAsync(cancellationToken);

    public Task<Group?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<Group?> FindAsync(string name, CancellationToken cancellationToken) =>
        context.Groups.SingleOrDefaultAsync(g => g.Name == name, cancellationToken);

    public async Task AddAsync(Group group, CancellationToken cancellationToken)
    {
        context.Groups.Add(group);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAsync(Group group, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RemoveAsync(Group group, CancellationToken cancellationToken)
    {
        context.Groups.Remove(group);
        await context.SaveChangesAsync(cancellationToken);
    }
}
