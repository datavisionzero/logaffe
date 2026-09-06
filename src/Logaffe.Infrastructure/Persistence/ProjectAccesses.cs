using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Who reaches which project (ADR 0055).
/// </summary>
/// <remarks>
/// One read per request — the reach of the identity behind it — and a handful of
/// rows either way. Both directions are indexed because both are asked: by user
/// on every request, and by project on the screen that hands the assignments
/// out.
/// </remarks>
public sealed class ProjectAccesses(LogaffeDbContext context) : IProjectAccess
{
    public async Task<Reach> ReachOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Reach.Of(await context.ProjectAccess
            .Where(a => a.UserId == userId)
            .Select(a => a.ProjectId)
            .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<ProjectAccess>> ListForProjectAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        await context.ProjectAccess
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.GrantedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectAccess>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.ProjectAccess
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.GrantedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> GrantAsync(
        ProjectAccess access, CancellationToken cancellationToken)
    {
        context.ProjectAccess.Add(access);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Already there: a second click, or two administrators assigning the
            // same project at once. The key is what says so, and the state
            // afterwards is the state that was asked for.
            context.ChangeTracker.Clear();

            return false;
        }
    }

    public async Task<bool> RevokeAsync(
        Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        await context.ProjectAccess
            .Where(a => a.ProjectId == projectId && a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
