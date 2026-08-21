using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The tally rows: what a flush adds, what the two consumers read, and what the
/// sweep takes out.
/// </summary>
/// <remarks>
/// Through EF Core rather than around it, for the reason <see cref="Samples"/>
/// is: the log path earns a binary <c>COPY</c> at eleven thousand entries a
/// second (ADR 0003), and twenty projects writing a row an hour each earn
/// nothing of the sort.
/// </remarks>
public sealed class Tallies(LogaffeDbContext context) : ITallies
{
    public async Task AddAsync(
        IReadOnlyList<TallyIncrement> increments, CancellationToken cancellationToken)
    {
        // Read what is there, add to it, write once. There is no upsert here and
        // no concurrency control, and neither is an oversight: the installation
        // is a single writer and this act is the only thing in it that writes
        // this table, so the row cannot move underneath the read.
        //
        // The read is bounded by what one flush touches — a minute of an
        // installation's projects, in the one or two hours a minute can straddle
        // — so it is a handful of rows however long the history is.
        var projectIds = increments.Select(increment => increment.ProjectId).Distinct().ToList();
        var hours = increments.Select(increment => increment.Hour).Distinct().ToList();

        var open = await context.Tallies
            .Where(t => projectIds.Contains(t.ProjectId) && hours.Contains(t.Hour))
            .ToDictionaryAsync(t => (t.ProjectId, t.Hour), cancellationToken);

        foreach (var increment in increments)
        {
            if (!open.TryGetValue((increment.ProjectId, increment.Hour), out var tally))
            {
                tally = Tally.For(increment.ProjectId, increment.Hour);
                context.Tallies.Add(tally);
            }

            tally.Add(increment.Entries, increment.AtErrorOrAbove);
        }

        // One transaction over the whole flush, which is what lets a failure be
        // handed back to the counter it came from rather than lost: what threw
        // stored none of it.
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tally>> ReadAsync(
        Guid projectId,
        DateTimeOffset fromHour,
        DateTimeOffset toHour,
        CancellationToken cancellationToken) =>
        await context.Tallies
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.Hour >= fromHour && t.Hour < toHour)
            .OrderBy(t => t.Hour)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ProjectsWithTalliesAsync(
        CancellationToken cancellationToken) =>
        await context.Tallies
            .Select(t => t.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task RemoveHoursBeforeAsync(DateTimeOffset hour, CancellationToken cancellationToken) =>
        context.Tallies
            .Where(t => t.Hour < hour)
            .ExecuteDeleteAsync(cancellationToken);

    public Task RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        context.Tallies
            .Where(t => t.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
}
