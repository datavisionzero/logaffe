using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// What the installation remembers about the conditions that have already
/// fired.
/// </summary>
/// <remarks>
/// Read tracked rather than with <c>AsNoTracking</c>, unlike most reads in this
/// assembly, and it is the write that wants it: a row already there is written
/// back as the change it is, and a subject nothing has been said about yet
/// arrives here detached and is added. The alternative is an upsert this table
/// has no use for — the installation is a single writer, and this pass is the
/// only thing in it that touches these rows.
/// </remarks>
public sealed class ConditionStates(LogaffeDbContext context) : IConditionStates
{
    public async Task<ConditionState?> FindAsync(
        Guid subjectId, AlertCondition condition, CancellationToken cancellationToken) =>
        await context.ConditionStates.SingleOrDefaultAsync(
            state => state.SubjectId == subjectId && state.Condition == condition,
            cancellationToken);

    /// <remarks>
    /// The one read here that is not tracked, and the one that is not the pass:
    /// the alerts screen is showing what it holds rather than about to write it
    /// back, and a change tracker full of rows nothing will alter is a cost with
    /// nothing on the other side of it.
    /// </remarks>
    public async Task<IReadOnlyList<ConditionState>> ListFiredAsync(
        CancellationToken cancellationToken) =>
        await context.ConditionStates
            .AsNoTracking()
            .Where(state => state.NotifiedAt != null)
            .OrderByDescending(state => state.NotifiedAt)
            .ToListAsync(cancellationToken);

    public Task RecordAsync(ConditionState state, CancellationToken cancellationToken)
    {
        if (context.Entry(state).State is EntityState.Detached)
        {
            context.ConditionStates.Add(state);
        }

        return context.SaveChangesAsync(cancellationToken);
    }
}
